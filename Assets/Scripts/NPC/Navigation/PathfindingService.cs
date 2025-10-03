using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Util;
using World;

namespace NPC
{
    /// <summary>
    /// Centralised pathfinding service that owns the baked navigation grid, processes queued path requests,
    /// and steps an A* search incrementally each OSRS tick so NPC navigation integrates with the global ticker cadence.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PathfindingService : ScenePersistentObject, ITickable
    {
        /// <summary>
        /// Result of a path query.
        /// </summary>
        public enum PathStatus
        {
            Success,
            GridUnavailable,
            GoalUnreachable
        }

        private sealed class PathRequest
        {
            public readonly int Id;
            public readonly WeakReference<IPathMoverClient> MoverReference;
            public readonly Vector2 StartWorld;
            public readonly Vector2 GoalWorld;

            /// <summary>
            /// Resolved world position that the navigation grid determined was reachable.
            /// Cached so the mover can align its destination with the actual walkable cell even if the
            /// goal had to be clamped or redirected during preparation.
            /// </summary>
            public Vector2 ResolvedGoalWorld;

            public AStarSearch Search;
            public Vector2Int StartCell;
            public Vector2Int GoalCell;
            public Vector2Int DesiredGoalCell;
            public bool Prepared;
            public bool UsedStartFallback;
            public int GridRevisionAtStart;

            public PathRequest(int id, IPathMoverClient mover, Vector2 start, Vector2 goal)
            {
                Id = id;
                MoverReference = new WeakReference<IPathMoverClient>(mover);
                StartWorld = start;
                GoalWorld = goal;
                Search = new AStarSearch();
            }
        }

        private sealed class MoverReferenceComparer : IEqualityComparer<WeakReference<IPathMoverClient>>
        {
            public static readonly MoverReferenceComparer Instance = new MoverReferenceComparer();

            public bool Equals(WeakReference<IPathMoverClient> x, WeakReference<IPathMoverClient> y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                bool xValid = x.TryGetTarget(out var xTarget) && xTarget != null;
                bool yValid = y.TryGetTarget(out var yTarget) && yTarget != null;

                if (xValid && yValid)
                {
                    return ReferenceEquals(xTarget, yTarget);
                }

                if (!xValid && !yValid)
                {
                    return ReferenceEquals(x, y);
                }

                return false;
            }

            public int GetHashCode(WeakReference<IPathMoverClient> obj)
            {
                if (obj == null)
                {
                    return 0;
                }

                if (obj.TryGetTarget(out var target) && target != null)
                {
                    return RuntimeHelpers.GetHashCode(target);
                }

                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        /// <summary>
        /// Lightweight binary min-heap used to manage open set exploration order without repeatedly scanning lists.
        /// Stores generic payloads that provide a comparable priority (f-cost in our case).
        /// </summary>
        private sealed class MinHeap<T>
        {
            private readonly List<T> elements = new List<T>();
            private readonly IComparer<T> comparer;

            public MinHeap()
                : this(null)
            {
            }

            public MinHeap(IComparer<T> customComparer)
            {
                comparer = customComparer ?? Comparer<T>.Default;
            }

            /// <summary>
            /// Number of elements currently stored in the heap.
            /// </summary>
            public int Count => elements.Count;

            /// <summary>
            /// Removes all entries while keeping the allocated buffer for reuse.
            /// </summary>
            public void Clear()
            {
                elements.Clear();
            }

            /// <summary>
            /// Inserts a new element and restores the heap invariant by bubbling it upward as needed.
            /// </summary>
            public void Insert(T value)
            {
                elements.Add(value);
                HeapifyUp(elements.Count - 1);
            }

            /// <summary>
            /// Extracts the smallest element. Returns false when the heap is empty so callers can gracefully abort.
            /// </summary>
            public bool TryExtractMin(out T value)
            {
                if (elements.Count == 0)
                {
                    value = default;
                    return false;
                }

                value = elements[0];
                int lastIndex = elements.Count - 1;
                elements[0] = elements[lastIndex];
                elements.RemoveAt(lastIndex);
                if (elements.Count > 0)
                {
                    HeapifyDown(0);
                }

                return true;
            }

            private void HeapifyUp(int index)
            {
                while (index > 0)
                {
                    int parentIndex = (index - 1) / 2;
                    if (comparer.Compare(elements[index], elements[parentIndex]) >= 0)
                    {
                        break;
                    }

                    (elements[index], elements[parentIndex]) = (elements[parentIndex], elements[index]);
                    index = parentIndex;
                }
            }

            private void HeapifyDown(int index)
            {
                while (true)
                {
                    int leftChild = index * 2 + 1;
                    int rightChild = leftChild + 1;
                    int smallest = index;

                    if (leftChild < elements.Count && comparer.Compare(elements[leftChild], elements[smallest]) < 0)
                    {
                        smallest = leftChild;
                    }

                    if (rightChild < elements.Count && comparer.Compare(elements[rightChild], elements[smallest]) < 0)
                    {
                        smallest = rightChild;
                    }

                    if (smallest == index)
                    {
                        break;
                    }

                    (elements[index], elements[smallest]) = (elements[smallest], elements[index]);
                    index = smallest;
                }
            }
        }

        /// <summary>
        /// Compact node descriptor pushed onto the heap so we can compare entries by f-cost while
        /// still resolving the authoritative <see cref="NodeRecord"/> data from the dictionary when expanding nodes.
        /// </summary>
        private readonly struct NodeRecordWrapper : IComparable<NodeRecordWrapper>
        {
            public NodeRecordWrapper(Vector2Int node, float fCost, float hCost)
            {
                Node = node;
                FCost = fCost;
                HCost = hCost;
            }

            public Vector2Int Node { get; }

            public float FCost { get; }

            private float HCost { get; }

            public int CompareTo(NodeRecordWrapper other)
            {
                int fComparison = FCost.CompareTo(other.FCost);
                if (fComparison != 0)
                {
                    return fComparison;
                }

                return HCost.CompareTo(other.HCost);
            }
        }

        private sealed class AStarSearch
        {
            public readonly MinHeap<NodeRecordWrapper> OpenSet = new MinHeap<NodeRecordWrapper>();
            public readonly HashSet<Vector2Int> ClosedSet = new HashSet<Vector2Int>();
            public readonly Dictionary<Vector2Int, NodeRecord> Records = new Dictionary<Vector2Int, NodeRecord>();
            public Vector2Int Start;
            public Vector2Int Goal;
        }

        private struct NodeRecord
        {
            public float GCost;
            public float HCost;
            public Vector2Int Parent;
            public bool HasParent;

            public float FCost => GCost + HCost;
        }

        /// <summary>
        /// Tolerance used when comparing f-costs fetched from the heap against the authoritative node record.
        /// Prevents floating point precision drift from flagging fresh entries as stale.
        /// </summary>
        private const float HeapCostEpsilon = 0.0001f;

        /// <summary>
        /// Cost assigned to a single diagonal step. Matches the distance covered when traversing a square grid.
        /// </summary>
        private static readonly float DiagonalStepCost = Mathf.Sqrt(2f);

        private static PathfindingService instance;

        [Header("Grid Source")]
        [Tooltip("Reference grid used for pathfinding. When unset the service will search the scene for a NavGridBuilder.")]
        [SerializeField] private NavGridBuilder navGrid;

        [Tooltip("Maximum number of nodes expanded per tick. Lower values spread work across more ticks at the cost of latency.")]
        [SerializeField, Range(4, 512)] private int maxNodesPerTick = 128;

        [Tooltip("Maximum number of path searches stepped in parallel each tick.")]
        [SerializeField, Range(1, 16)] private int maxConcurrentRequests = 4;

        [Header("Smoothing")]
        [Tooltip("Removes redundant intermediate cells from generated paths so movers follow cleaner corridors.")]
        [SerializeField] private bool enablePathSmoothing = true;

        [Tooltip("Attempts to merge straight corridors whenever a clear line exists between cell endpoints.")]
        [SerializeField] private bool useLineOfSightForSmoothing = true;

        [Header("Debug")]
        [Tooltip("Writes verbose logging for path requests and failures.")]
        [SerializeField] private bool enableDebugLogging;

        private readonly Queue<PathRequest> pendingRequests = new Queue<PathRequest>();
        private readonly Dictionary<WeakReference<IPathMoverClient>, int> latestQueuedRequestIdByMover =
            new Dictionary<WeakReference<IPathMoverClient>, int>(MoverReferenceComparer.Instance);
        private readonly List<WeakReference<IPathMoverClient>> moverCleanupBuffer = new List<WeakReference<IPathMoverClient>>();
        /// <summary>
        /// Reusable frontier used when resolving the nearest walkable fallback cell so we avoid per-call queue allocations.
        /// </summary>
        private readonly Queue<Vector2Int> resolveFrontier = new Queue<Vector2Int>();

        /// <summary>
        /// Reusable visited set used by <see cref="ResolveNearestWalkable"/> to prevent revisiting cells while keeping GC churn minimal.
        /// </summary>
        private readonly HashSet<Vector2Int> resolveVisited = new HashSet<Vector2Int>();
        private readonly List<PathRequest> activeRequests = new List<PathRequest>();
        private int nextRequestId = 1;
        private bool subscribedToTicker;
        private Coroutine tickerSubscriptionRoutine;
        private int nextActiveRequestIndex;

        /// <summary>
        /// Active singleton instance.
        /// </summary>
        public static PathfindingService Instance => instance != null || !Application.isPlaying
            ? instance
            : BootstrapImmediate();

        /// <summary>
        /// Current grid assigned to the service.
        /// </summary>
        public NavGridBuilder ActiveGrid => navGrid;

        /// <summary>
        /// Revision counter for the active grid, incremented every time it is rebuilt.
        /// </summary>
        public int GridRevision => navGrid != null ? navGrid.Revision : 0;

        protected override void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            base.Awake();
            instance = this;
            EnsureGridReference();
        }

        private void Start()
        {
            SubscribeToTicker();
        }

        private void OnEnable()
        {
            if (instance == null)
            {
                instance = this;
            }

            SubscribeToTicker();
            EnsureGridReference();
        }

        private void OnDisable()
        {
            UnsubscribeFromTicker();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                UnsubscribeFromTicker();
                if (navGrid != null)
                {
                    navGrid.GridRebuilt -= HandleGridRebuilt;
                }
                instance = null;
            }
        }

        /// <summary>
        /// Registers a navigation grid with the service.
        /// </summary>
        public void RegisterNavGrid(NavGridBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            if (navGrid == builder)
            {
                return;
            }

            if (navGrid != null)
            {
                navGrid.GridRebuilt -= HandleGridRebuilt;
            }

            navGrid = builder;
            if (navGrid != null)
            {
                navGrid.GridRebuilt += HandleGridRebuilt;
                if (navGrid.NeedsRebuild)
                {
                    navGrid.BuildGrid();
                }
            }

            if (activeRequests.Count > 0)
            {
                for (int i = 0; i < activeRequests.Count; i++)
                {
                    RequeueRequest(activeRequests[i]);
                }

                activeRequests.Clear();
                nextActiveRequestIndex = 0;
            }

            if (enableDebugLogging && navGrid != null)
            {
                Debug.Log($"PathfindingService registered grid '{navGrid.name}'.", this);
            }
        }

        /// <summary>
        /// Queues a path request. The service will deliver the result asynchronously via the supplied mover.
        /// </summary>
        public int RequestPath(IPathMoverClient mover, Vector2 start, Vector2 goal)
        {
            if (mover == null)
            {
                return -1;
            }

            PruneDeadMoverReferences();

            int id = nextRequestId++;
            RemovePendingRequestsForMover(mover, id);

            var request = new PathRequest(id, mover, start, goal);
            latestQueuedRequestIdByMover[request.MoverReference] = id;
            pendingRequests.Enqueue(request);

            if (enableDebugLogging)
            {
                Debug.Log($"Queued path request {id} for {GetMoverName(mover)} -> {goal}.", this);
            }

            return id;
        }

        /// <inheritdoc />
        public void OnTick()
        {
            PruneDeadMoverReferences();

            if (!EnsureGridReference())
            {
                if (activeRequests.Count > 0)
                {
                    for (int i = 0; i < activeRequests.Count; i++)
                    {
                        CompleteRequest(activeRequests[i], PathStatus.GridUnavailable, null);
                    }

                    activeRequests.Clear();
                    nextActiveRequestIndex = 0;
                }
                return;
            }

            int remainingBudget = Mathf.Max(0, maxNodesPerTick);
            while (remainingBudget > 0)
            {
                bool startedAny = StartRequestsWhilePossible();
                if (activeRequests.Count == 0)
                {
                    if (!startedAny)
                    {
                        break;
                    }

                    continue;
                }

                int budgetBeforeStep = remainingBudget;
                StepActiveRequests(ref remainingBudget);

                if (activeRequests.Count == 0 && pendingRequests.Count == 0)
                {
                    break;
                }

                if (remainingBudget == budgetBeforeStep)
                {
                    if (activeRequests.Count == 0 && pendingRequests.Count > 0)
                    {
                        continue;
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Pulls queued requests until either the concurrency limit is reached or no more entries are available.
        /// Returns whether at least one request was activated.
        /// </summary>
        private bool StartRequestsWhilePossible()
        {
            bool startedAny = false;

            while (activeRequests.Count < maxConcurrentRequests && pendingRequests.Count > 0)
            {
                var request = pendingRequests.Dequeue();
                if (!request.MoverReference.TryGetTarget(out var mover) || mover == null)
                {
                    RemoveMoverTracking(request, onlyWhenIdMatches: true);
                    continue;
                }

                if (IsRequestSuperseded(request, out int supersedingId))
                {
                    if (enableDebugLogging)
                    {
                        Debug.Log(
                            $"Discarded stale path request {request.Id} for {GetMoverName(mover)} because request {supersedingId} is newer.",
                            this);
                    }

                    continue;
                }

                if (!PrepareRequest(request))
                {
                    CompleteRequest(request, PathStatus.GridUnavailable, null);
                    continue;
                }

                if (request.UsedStartFallback && request.DesiredGoalCell != request.StartCell)
                {
                    if (enableDebugLogging)
                    {
                        Debug.LogWarning($"Path request {request.Id} goal unreachable. Fallback returned start cell.", this);
                    }

                    CompleteRequest(request, PathStatus.GoalUnreachable, null);
                    continue;
                }

                request.GridRevisionAtStart = navGrid != null ? navGrid.Revision : 0;
                activeRequests.Add(request);
                startedAny = true;
            }

            return startedAny;
        }

        private enum RequestStepOutcome
        {
            Continue,
            Completed,
            Abandoned,
            Requeued
        }

        /// <summary>
        /// Steps all active requests while distributing the remaining budget across them.
        /// </summary>
        private void StepActiveRequests(ref int remainingBudget)
        {
            if (remainingBudget <= 0 || activeRequests.Count == 0)
            {
                return;
            }

            if (nextActiveRequestIndex >= activeRequests.Count)
            {
                nextActiveRequestIndex = 0;
            }

            int processedThisCycle = 0;
            int currentIndex = nextActiveRequestIndex;

            while (remainingBudget > 0 && activeRequests.Count > 0 && processedThisCycle < activeRequests.Count)
            {
                if (currentIndex >= activeRequests.Count)
                {
                    currentIndex = 0;
                }

                var request = activeRequests[currentIndex];
                int remainingRequests = activeRequests.Count - processedThisCycle;
                int allocation = Mathf.Max(1, remainingBudget / remainingRequests);

                RequestStepOutcome outcome;
                int spent = StepRequest(request, allocation, out outcome);
                remainingBudget -= spent;

                if (outcome == RequestStepOutcome.Completed || outcome == RequestStepOutcome.Abandoned || outcome == RequestStepOutcome.Requeued)
                {
                    if (outcome == RequestStepOutcome.Requeued)
                    {
                        RequeueRequest(request);
                    }

                    activeRequests.RemoveAt(currentIndex);

                    if (activeRequests.Count == 0)
                    {
                        nextActiveRequestIndex = 0;
                        break;
                    }

                    if (currentIndex >= activeRequests.Count)
                    {
                        currentIndex = 0;
                    }

                    // The element that shifted into the current index has not been processed this cycle yet,
                    // so do not advance the processed counter to guarantee it receives time this tick.
                    continue;
                }

                currentIndex++;
                processedThisCycle++;
            }

            if (activeRequests.Count > 0)
            {
                nextActiveRequestIndex = currentIndex % activeRequests.Count;
            }
        }

        /// <summary>
        /// Advances a single request by expanding up to <paramref name="allocation"/> nodes.
        /// Returns how many nodes were actually expanded.
        /// </summary>
        private int StepRequest(PathRequest request, int allocation, out RequestStepOutcome outcome)
        {
            outcome = RequestStepOutcome.Continue;

            if (request == null || allocation <= 0)
            {
                return 0;
            }

            if (!request.MoverReference.TryGetTarget(out var mover) || mover == null)
            {
                RemoveMoverTracking(request, onlyWhenIdMatches: true);
                outcome = RequestStepOutcome.Abandoned;
                return 0;
            }

            if (IsRequestSuperseded(request, out int supersedingId))
            {
                if (enableDebugLogging)
                {
                    Debug.Log(
                        $"Abandoning active path request {request.Id} for {GetMoverName(mover)} because request {supersedingId} superseded it.",
                        this);
                }

                outcome = RequestStepOutcome.Abandoned;
                return 0;
            }

            if (navGrid == null)
            {
                CompleteRequest(request, PathStatus.GridUnavailable, null);
                outcome = RequestStepOutcome.Completed;
                return 0;
            }

            if (request.GridRevisionAtStart != navGrid.Revision)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"Nav grid changed while processing request {request.Id}. Re-queueing.", this);
                }

                outcome = RequestStepOutcome.Requeued;
                return 0;
            }

            var search = request.Search;
            var grid = navGrid;

            if (search.OpenSet.Count == 0)
            {
                CompleteRequest(request, PathStatus.GoalUnreachable, null);
                outcome = RequestStepOutcome.Completed;
                return 0;
            }

            int expanded = 0;
            while (expanded < allocation)
            {
                if (search.OpenSet.Count == 0)
                {
                    CompleteRequest(request, PathStatus.GoalUnreachable, null);
                    outcome = RequestStepOutcome.Completed;
                    return expanded;
                }

                if (!search.OpenSet.TryExtractMin(out var currentWrapper))
                {
                    CompleteRequest(request, PathStatus.GoalUnreachable, null);
                    outcome = RequestStepOutcome.Completed;
                    return expanded;
                }

                Vector2Int current = currentWrapper.Node;
                if (!search.Records.TryGetValue(current, out var currentRecord))
                {
                    continue;
                }

                if (search.ClosedSet.Contains(current))
                {
                    continue;
                }

                if (currentWrapper.FCost > currentRecord.FCost + HeapCostEpsilon)
                {
                    continue;
                }

                expanded++;

                if (current == search.Goal)
                {
                    if (IsRequestSuperseded(request, out int supersededDuringCompletion))
                    {
                        if (enableDebugLogging)
                        {
                            Debug.Log(
                                $"Discarded completed path for request {request.Id} because request {supersededDuringCompletion} superseded it before dispatch.",
                                this);
                        }

                        outcome = RequestStepOutcome.Abandoned;
                        return expanded;
                    }

                    var pathCells = ReconstructPath(search, current);
                    var smoothedCells = SmoothPathCells(pathCells, request.StartCell) ?? pathCells;
                    var worldPath = ConvertCellsToWorld(smoothedCells, request.StartCell);
                    CompleteRequest(request, PathStatus.Success, worldPath);
                    outcome = RequestStepOutcome.Completed;
                    return expanded;
                }

                search.ClosedSet.Add(current);

                foreach (var neighbour in EnumerateNeighbours(current))
                {
                    if (!grid.IsCellWithinBounds(neighbour))
                    {
                        continue;
                    }

                    if (!grid.IsCellWalkable(neighbour) && neighbour != search.Goal)
                    {
                        continue;
                    }

                    if (!HasClearDiagonal(current, neighbour, grid))
                    {
                        // Prevent cutting through corners by only allowing diagonal traversal when both flank tiles are free.
                        continue;
                    }

                    if (search.ClosedSet.Contains(neighbour))
                    {
                        continue;
                    }

                    float stepCost = IsDiagonalMove(current, neighbour) ? DiagonalStepCost : 1f;
                    float tentativeG = currentRecord.GCost + stepCost;
                    if (!search.Records.TryGetValue(neighbour, out var neighbourRecord) || tentativeG < neighbourRecord.GCost)
                    {
                        neighbourRecord.GCost = tentativeG;
                        neighbourRecord.HCost = Heuristic(neighbour, search.Goal);
                        neighbourRecord.Parent = current;
                        neighbourRecord.HasParent = true;
                        search.Records[neighbour] = neighbourRecord;

                        search.OpenSet.Insert(new NodeRecordWrapper(neighbour, neighbourRecord.FCost, neighbourRecord.HCost));
                    }
                }
            }

            return expanded;
        }

        /// <summary>
        /// Re-enqueues a request so it can restart after a grid rebuild or other interruption.
        /// </summary>
        private void RequeueRequest(PathRequest request)
        {
            if (request == null)
            {
                return;
            }

            request.Search.OpenSet.Clear();
            request.Search.ClosedSet.Clear();
            request.Search.Records.Clear();
            request.Prepared = false;
            latestQueuedRequestIdByMover[request.MoverReference] = request.Id;
            pendingRequests.Enqueue(request);
        }

        private void RemovePendingRequestsForMover(IPathMoverClient mover, int supersedingRequestId)
        {
            if (mover == null)
            {
                return;
            }

            int pendingCount = pendingRequests.Count;
            for (int i = 0; i < pendingCount; i++)
            {
                var existing = pendingRequests.Dequeue();
                bool discard = false;

                if (!existing.MoverReference.TryGetTarget(out var existingMover) || existingMover == null)
                {
                    discard = true;
                }
                else if (ReferenceEquals(existingMover, mover))
                {
                    discard = true;
                    if (enableDebugLogging)
                    {
                        Debug.Log($"Discarded pending path request {existing.Id} for {GetMoverName(existingMover)} because request {supersedingRequestId} superseded it.", this);
                    }
                }

                if (discard)
                {
                    RemoveMoverTracking(existing, onlyWhenIdMatches: true);
                    continue;
                }

                pendingRequests.Enqueue(existing);
            }
        }

        private static string GetMoverName(IPathMoverClient mover)
        {
            if (mover is Component component)
            {
                return component.name;
            }

            return mover != null ? mover.ToString() : "<null>";
        }

        private void RemoveMoverTracking(PathRequest request, bool onlyWhenIdMatches)
        {
            if (request == null)
            {
                return;
            }

            if (latestQueuedRequestIdByMover.TryGetValue(request.MoverReference, out var latestId))
            {
                if (!onlyWhenIdMatches || latestId == request.Id)
                {
                    latestQueuedRequestIdByMover.Remove(request.MoverReference);
                }
            }
        }

        private bool IsRequestSuperseded(PathRequest request, out int latestId)
        {
            latestId = -1;
            if (request == null)
            {
                return false;
            }

            if (!latestQueuedRequestIdByMover.TryGetValue(request.MoverReference, out latestId))
            {
                return false;
            }

            return latestId != request.Id;
        }

        private void PruneDeadMoverReferences()
        {
            if (latestQueuedRequestIdByMover.Count == 0)
            {
                return;
            }

            moverCleanupBuffer.Clear();
            foreach (var entry in latestQueuedRequestIdByMover)
            {
                if (!entry.Key.TryGetTarget(out var mover) || mover == null)
                {
                    moverCleanupBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < moverCleanupBuffer.Count; i++)
            {
                latestQueuedRequestIdByMover.Remove(moverCleanupBuffer[i]);
            }

            moverCleanupBuffer.Clear();
        }

        /// <summary>
        /// Converts world positions into grid coordinates and seeds the search structures.
        /// </summary>
        private bool PrepareRequest(PathRequest request)
        {
            if (!EnsureGridReference())
            {
                return false;
            }

            var grid = navGrid;
            Vector2Int startCell = grid.TryGetCell(request.StartWorld, out var tempStart)
                ? tempStart
                : grid.WorldToCellClamped(request.StartWorld);

            Vector2Int goalCell = grid.TryGetCell(request.GoalWorld, out var tempGoal)
                ? tempGoal
                : grid.WorldToCellClamped(request.GoalWorld);

            Vector2Int resolvedGoal = ResolveNearestWalkable(goalCell, startCell, out bool usedStartFallback);
            if (resolvedGoal == startCell && !grid.IsCellWalkable(resolvedGoal))
            {
                return false;
            }

            request.StartCell = startCell;
            request.GoalCell = resolvedGoal;
            request.DesiredGoalCell = goalCell;
            request.ResolvedGoalWorld = grid.GetCellCenter(resolvedGoal);
            request.UsedStartFallback = usedStartFallback;
            request.Search.OpenSet.Clear();
            request.Search.ClosedSet.Clear();
            request.Search.Records.Clear();
            request.Search.Start = startCell;
            request.Search.Goal = resolvedGoal;

            var startRecord = new NodeRecord
            {
                GCost = 0f,
                HCost = Heuristic(startCell, resolvedGoal),
                Parent = startCell,
                HasParent = false
            };
            request.Search.Records[startCell] = startRecord;
            request.Search.OpenSet.Insert(new NodeRecordWrapper(startCell, startRecord.FCost, startRecord.HCost));
            request.Prepared = true;
            return true;
        }

        /// <summary>
        /// Finds the closest walkable cell to the desired goal, falling back toward the start when necessary.
        /// Outputs whether the start cell had to be used as that fallback so callers can surface unreachable goals.
        /// </summary>
        private Vector2Int ResolveNearestWalkable(Vector2Int desired, Vector2Int start, out bool usedStartFallback)
        {
            var grid = navGrid;
            usedStartFallback = false;
            resolveFrontier.Clear();
            resolveVisited.Clear();

            if (grid.IsCellWalkable(desired))
            {
                return desired;
            }

            // The service ticks on the main thread, so reusing these collections is safe and avoids GC churn for frequent path requests.
            resolveFrontier.Enqueue(desired);
            resolveVisited.Add(desired);

            while (resolveFrontier.Count > 0)
            {
                Vector2Int current = resolveFrontier.Dequeue();
                foreach (var neighbour in EnumerateNeighbours(current))
                {
                    if (!grid.IsCellWithinBounds(neighbour))
                    {
                        continue;
                    }

                    if (!HasClearDiagonal(current, neighbour, grid))
                    {
                        // Skip diagonals that would clip through blocked corners when looking for a fallback target.
                        continue;
                    }

                    if (!resolveVisited.Add(neighbour))
                    {
                        continue;
                    }

                    bool isStart = neighbour == start;
                    bool isWalkable = grid.IsCellWalkable(neighbour);

                    if (!isWalkable && !isStart)
                    {
                        // Skip blocked neighbours entirely so we only explore reachable space.
                        continue;
                    }

                    if (isStart)
                    {
                        // Track that we brushed past the start cell but keep exploring in case another walkable target exists.
                        resolveFrontier.Enqueue(neighbour);
                        continue;
                    }

                    if (!grid.HasClearLineBetweenCells(neighbour, desired))
                    {
                        // The neighbour is walkable but a straight corridor back to the desired goal is obstructed.
                        // Re-enqueue it so the breadth-first search can continue expanding outward from this tile.
                        resolveFrontier.Enqueue(neighbour);
                        continue;
                    }

                    // Found the nearest reachable walkable neighbour.
                    return neighbour;
                }
            }

            // No valid neighbour could be resolved; fall back to the start cell and flag the dead-end.
            usedStartFallback = start != desired;
            return start;
        }

        /// <summary>
        /// Dispatches the resolved path back to the requesting mover.
        /// </summary>
        private void CompleteRequest(PathRequest request, PathStatus status, List<Vector2> worldPath)
        {
            if (!request.MoverReference.TryGetTarget(out var mover) || mover == null)
            {
                RemoveMoverTracking(request, onlyWhenIdMatches: true);
                return;
            }

            Vector2 resolvedGoalWorld = request.ResolvedGoalWorld;
            if (navGrid != null && navGrid.HasGrid)
            {
                resolvedGoalWorld = navGrid.GetCellCenter(request.GoalCell);
            }
            else if (!request.Prepared)
            {
                // If preparation never succeeded we cannot reliably compute a resolved goal, so fall back to
                // the originally requested world-space target.
                resolvedGoalWorld = request.GoalWorld;
            }

            RemoveMoverTracking(request, onlyWhenIdMatches: true);
            mover.HandlePathResult(request.Id, status, worldPath, resolvedGoalWorld);
        }

        /// <summary>
        /// Maps a list of grid cells back into world-space positions.
        /// </summary>
        private static List<Vector2> ConvertCellsToWorld(List<Vector2Int> cells, Vector2Int startCell)
        {
            var grid = instance?.navGrid;
            var path = new List<Vector2>();
            if (grid == null || cells == null || cells.Count == 0)
            {
                return path;
            }

            Vector2Int? lastAddedCell = null;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                if (cell == startCell && !lastAddedCell.HasValue)
                {
                    continue;
                }

                if (lastAddedCell.HasValue && lastAddedCell.Value == cell)
                {
                    continue;
                }

                path.Add(grid.GetCellCenter(cell));
                lastAddedCell = cell;
            }

            return path;
        }

        /// <summary>
        /// Octile distance heuristic for an 8-way grid. Remains admissible when diagonal movement is allowed.
        /// </summary>
        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            int min = Mathf.Min(dx, dy);
            int max = Mathf.Max(dx, dy);
            return (DiagonalStepCost - 1f) * min + max;
        }

        /// <summary>
        /// Enumerates the eight neighbours (four orthogonal + four diagonal) of a grid cell.
        /// </summary>
        private static IEnumerable<Vector2Int> EnumerateNeighbours(Vector2Int cell)
        {
            yield return new Vector2Int(cell.x + 1, cell.y);
            yield return new Vector2Int(cell.x - 1, cell.y);
            yield return new Vector2Int(cell.x, cell.y + 1);
            yield return new Vector2Int(cell.x, cell.y - 1);
            yield return new Vector2Int(cell.x + 1, cell.y + 1);
            yield return new Vector2Int(cell.x + 1, cell.y - 1);
            yield return new Vector2Int(cell.x - 1, cell.y + 1);
            yield return new Vector2Int(cell.x - 1, cell.y - 1);
        }

        /// <summary>
        /// Returns whether the move between two cells is diagonal on the grid.
        /// </summary>
        private static bool IsDiagonalMove(Vector2Int origin, Vector2Int target)
        {
            return origin.x != target.x && origin.y != target.y;
        }

        /// <summary>
        /// Verifies that a diagonal traversal between two cells is valid by checking the orthogonal flank cells.
        /// </summary>
        private static bool HasClearDiagonal(Vector2Int origin, Vector2Int target, NavGridBuilder grid)
        {
            if (!IsDiagonalMove(origin, target))
            {
                return true;
            }

            if (grid == null)
            {
                return true;
            }

            Vector2Int horizontal = new Vector2Int(target.x, origin.y);
            Vector2Int vertical = new Vector2Int(origin.x, target.y);
            return grid.IsCellWalkable(horizontal) && grid.IsCellWalkable(vertical);
        }

        /// <summary>
        /// Builds the final path by walking parent pointers back from the goal node.
        /// </summary>
        private static List<Vector2Int> ReconstructPath(AStarSearch search, Vector2Int goal)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            Vector2Int current = goal;
            path.Add(current);

            while (search.Records.TryGetValue(current, out var record) && record.HasParent && record.Parent != current)
            {
                current = record.Parent;
                path.Add(current);
            }

            path.Reverse();
            return path;
        }

        /// <summary>
        /// Collapses redundant cells from the reconstructed path so movers receive the minimal set of bends.
        /// Optionally applies a line-of-sight pass that merges straight corridors when nothing blocks the route.
        /// </summary>
        private List<Vector2Int> SmoothPathCells(List<Vector2Int> pathCells, Vector2Int startCell)
        {
            if (!enablePathSmoothing || pathCells == null || pathCells.Count <= 2)
            {
                return pathCells;
            }

            var collapsed = new List<Vector2Int>(pathCells.Count)
            {
                pathCells[0]
            };

            if (pathCells[0] != startCell)
            {
                collapsed.Insert(0, startCell);
            }

            if (pathCells.Count > 1)
            {
                Vector2Int previousDirection = NormalizeDirection(pathCells[1] - pathCells[0]);
                for (int i = 1; i < pathCells.Count; i++)
                {
                    Vector2Int previous = pathCells[i - 1];
                    Vector2Int current = pathCells[i];
                    Vector2Int direction = NormalizeDirection(current - previous);

                    if (i > 1 && direction != previousDirection)
                    {
                        Vector2Int turnCell = previous;
                        if (collapsed[collapsed.Count - 1] != turnCell)
                        {
                            collapsed.Add(turnCell);
                        }

                        previousDirection = direction;
                    }
                    else if (i == 1)
                    {
                        previousDirection = direction;
                    }

                    if (i == pathCells.Count - 1)
                    {
                        if (collapsed[collapsed.Count - 1] != current)
                        {
                            collapsed.Add(current);
                        }
                    }
                }
            }

            if (!useLineOfSightForSmoothing || navGrid == null || !navGrid.HasGrid || collapsed.Count <= 2)
            {
                return collapsed;
            }

            var optimised = new List<Vector2Int>(collapsed.Count)
            {
                collapsed[0]
            };

            int anchorIndex = 0;
            while (anchorIndex < collapsed.Count - 1)
            {
                int nextIndex = anchorIndex + 1;
                for (int i = collapsed.Count - 1; i > anchorIndex; i--)
                {
                    if (navGrid.HasClearLineBetweenCells(collapsed[anchorIndex], collapsed[i]))
                    {
                        nextIndex = i;
                        break;
                    }
                }

                Vector2Int nextCell = collapsed[nextIndex];
                if (optimised[optimised.Count - 1] != nextCell)
                {
                    optimised.Add(nextCell);
                }

                anchorIndex = nextIndex;
            }

            return optimised;
        }

        /// <summary>
        /// Normalises a grid-space delta so each axis is clamped to -1, 0, or 1 for direction comparisons.
        /// </summary>
        private static Vector2Int NormalizeDirection(Vector2Int delta)
        {
            int x = delta.x == 0 ? 0 : (delta.x > 0 ? 1 : -1);
            int y = delta.y == 0 ? 0 : (delta.y > 0 ? 1 : -1);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// Resolves and validates the navigation grid used by the service.
        /// </summary>
        private bool EnsureGridReference()
        {
            if (navGrid != null && navGrid.HasGrid)
            {
                return true;
            }

            if (navGrid == null)
            {
                navGrid = FindObjectOfType<NavGridBuilder>();
                if (navGrid != null)
                {
                    navGrid.GridRebuilt += HandleGridRebuilt;
                }
            }

            if (navGrid == null)
            {
                return false;
            }

            if (navGrid.NeedsRebuild)
            {
                navGrid.BuildGrid();
            }

            return navGrid.HasGrid;
        }

        /// <summary>
        /// Handles grid rebuild notifications so outstanding requests can restart against the new data.
        /// </summary>
        private void HandleGridRebuilt(NavGridBuilder builder)
        {
            if (builder != navGrid)
            {
                return;
            }

            if (enableDebugLogging)
            {
                Debug.Log($"Nav grid rebuilt (revision {builder.Revision}).", this);
            }

            if (activeRequests.Count > 0)
            {
                for (int i = 0; i < activeRequests.Count; i++)
                {
                    RequeueRequest(activeRequests[i]);
                }

                activeRequests.Clear();
                nextActiveRequestIndex = 0;
            }
        }

        /// <summary>
        /// Registers the service with the global ticker to process queued requests each game tick.
        /// </summary>
        private void SubscribeToTicker()
        {
            if (subscribedToTicker)
            {
                return;
            }

            if (Ticker.Instance == null)
            {
                if (tickerSubscriptionRoutine == null && isActiveAndEnabled)
                {
                    tickerSubscriptionRoutine = StartCoroutine(WaitForTicker());
                }
                return;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
        }

        /// <summary>
        /// Removes the ticker subscription when the service is disabled or destroyed.
        /// </summary>
        private void UnsubscribeFromTicker()
        {
            if (tickerSubscriptionRoutine != null)
            {
                StopCoroutine(tickerSubscriptionRoutine);
                tickerSubscriptionRoutine = null;
            }

            if (!subscribedToTicker)
            {
                return;
            }

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }

            subscribedToTicker = false;
        }

        /// <summary>
        /// Defers ticker registration until the singleton instance becomes available.
        /// </summary>
        private System.Collections.IEnumerator WaitForTicker()
        {
            while (Ticker.Instance == null)
            {
                yield return null;
            }

            tickerSubscriptionRoutine = null;

            if (!isActiveAndEnabled)
            {
                yield break;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
        }

        /// <summary>
        /// Ensures a service instance exists when requested before the scene bootstrap completes.
        /// </summary>
        private static PathfindingService BootstrapImmediate()
        {
            var existing = FindObjectOfType<PathfindingService>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(nameof(PathfindingService));
            var service = go.AddComponent<PathfindingService>();
            return service;
        }
    }
}
