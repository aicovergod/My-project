using System;
using System.Collections.Generic;
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
            public readonly WeakReference<NpcPathMover> MoverReference;
            public readonly Vector2 StartWorld;
            public readonly Vector2 GoalWorld;

            public AStarSearch Search;
            public Vector2Int StartCell;
            public Vector2Int GoalCell;
            public Vector2Int DesiredGoalCell;
            public bool Prepared;
            public bool UsedStartFallback;

            public PathRequest(int id, NpcPathMover mover, Vector2 start, Vector2 goal)
            {
                Id = id;
                MoverReference = new WeakReference<NpcPathMover>(mover);
                StartWorld = start;
                GoalWorld = goal;
                Search = new AStarSearch();
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

        private static PathfindingService instance;

        [Header("Grid Source")]
        [Tooltip("Reference grid used for pathfinding. When unset the service will search the scene for a NavGridBuilder.")]
        [SerializeField] private NavGridBuilder navGrid;

        [Tooltip("Maximum number of nodes expanded per tick. Lower values spread work across more ticks at the cost of latency.")]
        [SerializeField, Range(4, 512)] private int maxNodesPerTick = 128;

        [Header("Debug")]
        [Tooltip("Writes verbose logging for path requests and failures.")]
        [SerializeField] private bool enableDebugLogging;

        private readonly Queue<PathRequest> pendingRequests = new Queue<PathRequest>();
        private PathRequest activeRequest;
        private int nextRequestId = 1;
        private bool subscribedToTicker;
        private Coroutine tickerSubscriptionRoutine;
        private int cachedGridRevision;

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
            cachedGridRevision = navGrid != null ? navGrid.Revision : 0;
            if (navGrid != null)
            {
                navGrid.GridRebuilt += HandleGridRebuilt;
                if (navGrid.NeedsRebuild)
                {
                    navGrid.BuildGrid();
                }
            }

            if (enableDebugLogging && navGrid != null)
            {
                Debug.Log($"PathfindingService registered grid '{navGrid.name}'.", this);
            }
        }

        /// <summary>
        /// Queues a path request. The service will deliver the result asynchronously via the supplied mover.
        /// </summary>
        public int RequestPath(NpcPathMover mover, Vector2 start, Vector2 goal)
        {
            if (mover == null)
            {
                return -1;
            }

            int id = nextRequestId++;
            var request = new PathRequest(id, mover, start, goal);
            pendingRequests.Enqueue(request);

            if (enableDebugLogging)
            {
                Debug.Log($"Queued path request {id} for {mover.name} -> {goal}.", this);
            }

            return id;
        }

        /// <inheritdoc />
        public void OnTick()
        {
            if (!EnsureGridReference())
            {
                if (activeRequest != null)
                {
                    CompleteRequest(activeRequest, PathStatus.GridUnavailable, null);
                    activeRequest = null;
                }
                return;
            }

            if (activeRequest == null)
            {
                TryBeginNextRequest();
            }

            if (activeRequest == null)
            {
                return;
            }

            if (cachedGridRevision != navGrid.Revision)
            {
                if (enableDebugLogging)
                {
                    Debug.Log("Nav grid changed while processing a request. Restarting search.", this);
                }

                // Re-queue the request so it can restart with the new grid data.
                RequeueActiveRequest();
                TryBeginNextRequest();
                return;
            }

            StepActiveRequest();
        }

        /// <summary>
        /// Advances the active A* search, expanding up to <see cref="maxNodesPerTick"/> nodes.
        /// </summary>
        private void StepActiveRequest()
        {
            var search = activeRequest.Search;
            var grid = navGrid;
            if (search.OpenSet.Count == 0)
            {
                CompleteRequest(activeRequest, PathStatus.GoalUnreachable, null);
                activeRequest = null;
                TryBeginNextRequest();
                return;
            }

            int expandedThisTick = 0;
            while (expandedThisTick < maxNodesPerTick)
            {
                if (search.OpenSet.Count == 0)
                {
                    CompleteRequest(activeRequest, PathStatus.GoalUnreachable, null);
                    activeRequest = null;
                    TryBeginNextRequest();
                    return;
                }

                if (!search.OpenSet.TryExtractMin(out var currentWrapper))
                {
                    CompleteRequest(activeRequest, PathStatus.GoalUnreachable, null);
                    activeRequest = null;
                    TryBeginNextRequest();
                    return;
                }

                Vector2Int current = currentWrapper.Node;
                if (!search.Records.TryGetValue(current, out var currentRecord))
                {
                    // The entry is stale (the node was removed from the record dictionary after a cheaper insertion).
                    continue;
                }

                if (search.ClosedSet.Contains(current))
                {
                    // A better path already expanded this node; ignore the stale heap entry.
                    continue;
                }

                if (currentWrapper.FCost > currentRecord.FCost + HeapCostEpsilon)
                {
                    // The wrapper references an older, more expensive cost. Skip until the cheapest version surfaces.
                    continue;
                }

                expandedThisTick++;

                if (current == search.Goal)
                {
                    var pathCells = ReconstructPath(search, current);
                    var worldPath = ConvertCellsToWorld(pathCells, activeRequest.StartCell);
                    CompleteRequest(activeRequest, PathStatus.Success, worldPath);
                    activeRequest = null;
                    TryBeginNextRequest();
                    return;
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

                    if (search.ClosedSet.Contains(neighbour))
                    {
                        continue;
                    }

                    float tentativeG = currentRecord.GCost + 1f;
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
        }

        /// <summary>
        /// Pulls the next queued request and prepares it for execution.
        /// </summary>
        private void TryBeginNextRequest()
        {
            while (pendingRequests.Count > 0)
            {
                var request = pendingRequests.Dequeue();
                if (!request.MoverReference.TryGetTarget(out var mover) || mover == null)
                {
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

                activeRequest = request;
                cachedGridRevision = navGrid != null ? navGrid.Revision : 0;
                return;
            }
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
            if (grid.IsCellWalkable(desired))
            {
                return desired;
            }

            Queue<Vector2Int> frontier = new Queue<Vector2Int>();
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            frontier.Enqueue(desired);
            visited.Add(desired);

            while (frontier.Count > 0)
            {
                Vector2Int current = frontier.Dequeue();
                foreach (var neighbour in EnumerateNeighbours(current))
                {
                    if (!grid.IsCellWithinBounds(neighbour))
                    {
                        continue;
                    }

                    if (!visited.Add(neighbour))
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
                        frontier.Enqueue(neighbour);
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
                return;
            }

            mover.HandlePathResult(request.Id, status, worldPath, request.GoalWorld);
        }

        /// <summary>
        /// Re-enqueues the active request so it can be retried after the grid changes.
        /// </summary>
        private void RequeueActiveRequest()
        {
            if (activeRequest == null)
            {
                return;
            }

            activeRequest.Search.OpenSet.Clear();
            activeRequest.Search.ClosedSet.Clear();
            activeRequest.Search.Records.Clear();
            activeRequest.Prepared = false;
            pendingRequests.Enqueue(activeRequest);
            activeRequest = null;
        }

        /// <summary>
        /// Maps a list of grid cells back into world-space positions.
        /// </summary>
        private static List<Vector2> ConvertCellsToWorld(List<Vector2Int> cells, Vector2Int startCell)
        {
            var grid = instance?.navGrid;
            var path = new List<Vector2>();
            if (grid == null)
            {
                return path;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                if (cell == startCell)
                {
                    continue;
                }

                path.Add(grid.GetCellCenter(cell));
            }

            return path;
        }

        /// <summary>
        /// Manhattan distance heuristic for the 4-way grid.
        /// </summary>
        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        /// <summary>
        /// Enumerates the four orthogonal neighbours of a grid cell.
        /// </summary>
        private static IEnumerable<Vector2Int> EnumerateNeighbours(Vector2Int cell)
        {
            yield return new Vector2Int(cell.x + 1, cell.y);
            yield return new Vector2Int(cell.x - 1, cell.y);
            yield return new Vector2Int(cell.x, cell.y + 1);
            yield return new Vector2Int(cell.x, cell.y - 1);
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

            cachedGridRevision = navGrid.Revision;
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

            cachedGridRevision = builder.Revision;
            if (enableDebugLogging)
            {
                Debug.Log($"Nav grid rebuilt (revision {cachedGridRevision}).", this);
            }

            if (activeRequest != null)
            {
                RequeueActiveRequest();
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
