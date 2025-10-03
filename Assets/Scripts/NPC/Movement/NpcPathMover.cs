using System.Collections.Generic;
using UnityEngine;
using Util;

namespace NPC
{
    /// <summary>
    /// Tick-driven movement helper that consumes waypoint queues from <see cref="PathfindingService"/>
    /// and walks NPCs tile-to-tile. The mover requests replans whenever the active route becomes invalid
    /// so NPCs can steer around newly placed fences or blockers.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class NpcPathMover : MonoBehaviour, ITickable, IPathMoverClient
    {
        [Header("Movement")]
        [Tooltip("Seconds spent traversing a single tile. Defaults to the OSRS tick length for grid-accurate pacing.")]
        [SerializeField] private float tileTraverseDuration = Ticker.TickDuration;

        [Tooltip("Distance considered \"close enough\" to consume a waypoint.")]
        [SerializeField] private float waypointTolerance = 0.05f;

        [Tooltip("Desired stand-off distance from the destination. Updated by combat controllers when chasing targets.")]
        [SerializeField] private float stopDistance = 1f;

        [Header("Repathing")]
        [Tooltip("Minimum time between automatic replans.")]
        [SerializeField] private float repathCooldownSeconds = 0.6f;

        [Tooltip("If progress stalls for longer than this value the mover forces a replan.")]
        [SerializeField] private float stuckTimeoutSeconds = 3f;

        [Tooltip("Destination drift (in world units) that triggers a replan when the goal moves.")]
        [SerializeField] private float destinationRepathThreshold = 0.75f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging;
        [SerializeField] private bool drawDebugPath = true;
        [SerializeField] private Color debugPathColor = new Color(1f, 0.75f, 0f, 0.9f);

        private readonly Queue<Vector2> waypointQueue = new Queue<Vector2>();
        private readonly List<Vector2> debugPath = new List<Vector2>();

        private Rigidbody2D body;
        private NpcWanderer wanderer;
        private NpcFacing facing;
        private NpcSpriteAnimator spriteAnimator;
        private PathfindingService pathService;

        private bool subscribedToTicker;
        private Coroutine tickerSubscriptionRoutine;

        private bool awaitingPath;
        private bool stepping;
        private Vector2 currentStepStart;
        private Vector2 currentStepTarget;
        private Vector2 previousStepPosition;
        private float stepTimer;
        private float currentStepDuration = Ticker.TickDuration;
        private float lastProgressTimestamp;
        private float nextAllowedRepathTime;
        private float lastDestinationUpdate;

        private Vector2 desiredDestination;
        private Vector2 lastRequestedDestination;
        private Vector2 resolvedPathDestination;
        private bool hasDestination;
        private bool hasResolvedPathDestination;
        private int activeRequestId = -1;
        private bool wandererSuspended;
        private Vector2 lastManualPosition;
        private bool hasManualPositionSample;

        private const float ManualResyncTolerance = 0.01f;
        private const float ManualResyncToleranceSqr = ManualResyncTolerance * ManualResyncTolerance;

        /// <summary>
        /// Raised whenever the mover reaches its destination within the configured stop distance.
        /// </summary>
        public event System.Action<NpcPathMover> DestinationReached;

        /// <summary>
        /// Current stop distance applied when approaching the destination.
        /// </summary>
        public float StopDistance => stopDistance;

        /// <summary>
        /// Returns true while the mover is waiting on or following a path.
        /// </summary>
        public bool IsFollowingPath => awaitingPath || stepping || waypointQueue.Count > 0;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
            }

            wanderer = GetComponent<NpcWanderer>();
            facing = GetComponent<NpcFacing>();
            spriteAnimator = facing != null ? facing.Animator : null;
            if (spriteAnimator == null)
            {
                spriteAnimator = GetComponent<NpcSpriteAnimator>() ?? GetComponentInChildren<NpcSpriteAnimator>();
            }
            pathService = PathfindingService.Instance;
        }

        private void OnValidate()
        {
            tileTraverseDuration = Mathf.Max(0.01f, tileTraverseDuration);

            if (stuckTimeoutSeconds <= tileTraverseDuration)
            {
                float correctedValue = Mathf.Max(tileTraverseDuration + 0.01f, tileTraverseDuration * 1.1f);
                if (!Mathf.Approximately(stuckTimeoutSeconds, correctedValue))
                {
                    Debug.LogWarning(
                        $"NpcPathMover '{name}' adjusted stuck timeout from {stuckTimeoutSeconds:F2}s to {correctedValue:F2}s so it exceeds the tile traverse duration.",
                        this);
                }

                stuckTimeoutSeconds = correctedValue;
            }
        }

        private void OnEnable()
        {
            SubscribeToTicker();
        }

        private void Start()
        {
            SubscribeToTicker();
            EnsureServiceReference();
        }

        private void OnDisable()
        {
            UnsubscribeFromTicker();
            CancelPath();
        }

        private void OnDestroy()
        {
            UnsubscribeFromTicker();
        }

        private void Update()
        {
            if (!stepping)
            {
                return;
            }

            stepTimer += Time.deltaTime;
            float duration = Mathf.Max(0.01f, currentStepDuration);
            float progress = Mathf.Clamp01(stepTimer / duration);
            Vector2 interpolated = Vector2.Lerp(currentStepStart, currentStepTarget, progress);
            ApplyPosition(interpolated, snapToGrid: false);
            UpdateFacingDuringStep(interpolated);

            if (progress >= 1f - Mathf.Epsilon)
            {
                ApplyPosition(currentStepTarget, snapToGrid: true);
                previousStepPosition = currentStepTarget;
                stepping = false;
                lastProgressTimestamp = Time.time;
                bool advanced = TryAdvanceToNextWaypoint();
                if (!advanced)
                {
                    UpdateMovementVisuals(Vector2.zero);
                }
            }
        }

        /// <summary>
        /// Configures the preferred stop distance (usually the NPC's preferred attack range).
        /// </summary>
        public void SetPreferredStopDistance(float distance)
        {
            stopDistance = Mathf.Max(0.05f, distance);
        }

        /// <summary>
        /// Queues a path request to the supplied destination. Existing paths are cancelled.
        /// </summary>
        public void RequestPathTo(Vector2 destination)
        {
            hasDestination = true;
            desiredDestination = destination;
            RequestPathInternal(destination, false);
        }

        /// <summary>
        /// Convenience overload that updates the preferred stop distance before requesting a path.
        /// </summary>
        public void RequestPathTo(Vector2 destination, float desiredStopDistance)
        {
            SetPreferredStopDistance(desiredStopDistance);
            RequestPathTo(destination);
        }

        /// <summary>
        /// Cancels any active path request and clears outstanding waypoints.
        /// </summary>
        public void CancelPath()
        {
            awaitingPath = false;
            waypointQueue.Clear();
            debugPath.Clear();
            stepping = false;
            activeRequestId = -1;
            currentStepDuration = Mathf.Max(0.01f, tileTraverseDuration);
            hasDestination = false;
            hasResolvedPathDestination = false;
            resolvedPathDestination = default;
            TryAdvanceToNextWaypoint();
            UpdateMovementVisuals(Vector2.zero);
        }

        /// <inheritdoc />
        public void OnTick()
        {
            if (hasDestination)
            {
                EnsureServiceReference();
                EvaluateDestinationDrift();
            }

            if (!stepping)
            {
                TryAdvanceToNextWaypoint();
            }

            if (stepping)
            {
                if (!IsWaypointWalkable(currentStepTarget))
                {
                    ForceReplan();
                    return;
                }

                Vector2 currentPosition = GetCurrentPosition();
                if (!HasClearStepLine(currentPosition, currentStepTarget))
                {
                    // Path smoothing can collapse long straight corridors into a single waypoint. If a new
                    // blocker appears mid-segment we need to detect it immediately instead of waiting until
                    // the next waypoint is consumed, otherwise the NPC keeps marching into the obstacle.
                    ForceReplan(ignoreCooldown: true);
                    return;
                }

                float timeSinceProgress = Time.time - lastProgressTimestamp;
                if (lastProgressTimestamp > 0f && timeSinceProgress >= stuckTimeoutSeconds)
                {
                    ForceReplan(true);
                    return;
                }
            }
            else if (hasDestination && !awaitingPath && waypointQueue.Count == 0)
            {
                EvaluateArrival();
            }
        }

        /// <summary>
        /// Consumes the outcome of a path request issued through <see cref="PathfindingService"/>.
        /// </summary>
        public void HandlePathResult(int requestId, PathfindingService.PathStatus status, List<Vector2> worldPath, Vector2 resolvedGoalWorld)
        {
            if (requestId != activeRequestId)
            {
                return;
            }

            awaitingPath = false;
            activeRequestId = -1;
            hasResolvedPathDestination = false;
            resolvedPathDestination = default;

            if (status == PathfindingService.PathStatus.GoalUnreachable)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"NPC {name} path request {requestId} reached fallback -> goal unreachable.", this);
                }

                waypointQueue.Clear();
                debugPath.Clear();
                stepping = false;
                hasDestination = false;
                hasResolvedPathDestination = false;
                resolvedPathDestination = default;
                TryAdvanceToNextWaypoint();
                UpdateMovementVisuals(Vector2.zero);
                return;
            }

            if (status != PathfindingService.PathStatus.Success || worldPath == null)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"NPC {name} path request {requestId} failed: {status}.", this);
                }

                hasResolvedPathDestination = false;
                resolvedPathDestination = default;
                ScheduleRetry();
                UpdateMovementVisuals(Vector2.zero);
                return;
            }

            resolvedPathDestination = resolvedGoalWorld;
            hasResolvedPathDestination = true;

            waypointQueue.Clear();
            debugPath.Clear();

            for (int i = 0; i < worldPath.Count; i++)
            {
                Vector2 waypoint = worldPath[i];
                Vector2 clampedWaypoint = ClampWithWanderer(waypoint);
                waypointQueue.Enqueue(clampedWaypoint);
                debugPath.Add(clampedWaypoint);
            }

            if (enableDebugLogging)
            {
                Debug.Log($"NPC {name} received path {requestId} with {debugPath.Count} waypoints.", this);
            }

            if (waypointQueue.Count == 0)
            {
                EvaluateArrival();
                return;
            }

            BeginStep(waypointQueue.Dequeue());
        }

        /// <summary>
        /// Starts interpolating toward the supplied waypoint, suspending any wanderer behaviour.
        /// </summary>
        private void BeginStep(Vector2 destination)
        {
            SuspendWanderer();
            currentStepStart = ClampWithWanderer(GetCurrentPosition());
            currentStepTarget = ClampWithWanderer(destination);
            previousStepPosition = currentStepStart;
            stepTimer = 0f;
            stepping = true;
            lastProgressTimestamp = Time.time;
            currentStepDuration = ResolveStepDuration(currentStepStart, currentStepTarget);

            Vector2 direction = currentStepTarget - currentStepStart;
            if (direction.sqrMagnitude > Mathf.Epsilon)
            {
                facing?.FaceDirection(direction);
                float duration = Mathf.Max(0.0001f, currentStepDuration);
                UpdateMovementVisuals(direction / duration);
            }
            else
            {
                UpdateMovementVisuals(Vector2.zero);
            }
        }

        /// <summary>
        /// Calculates how long the mover should spend traversing the current segment based on grid distance.
        /// </summary>
        private float ResolveStepDuration(Vector2 start, Vector2 target)
        {
            float baseDuration = Mathf.Max(0.01f, tileTraverseDuration);
            var grid = pathService != null ? pathService.ActiveGrid : PathfindingService.Instance?.ActiveGrid;

            if (grid != null && grid.HasGrid)
            {
                if (grid.TryGetCell(start, out var startCell) && grid.TryGetCell(target, out var targetCell))
                {
                    int dx = Mathf.Abs(targetCell.x - startCell.x);
                    int dy = Mathf.Abs(targetCell.y - startCell.y);
                    int steps = Mathf.Max(dx, dy);
                    if (steps <= 0)
                    {
                        return baseDuration;
                    }

                    return baseDuration * steps;
                }

                float approxSteps = Vector2.Distance(start, target) / Mathf.Max(0.0001f, grid.TileSize);
                if (approxSteps > 1f)
                {
                    return baseDuration * approxSteps;
                }

                return baseDuration;
            }

            float worldDistance = Vector2.Distance(start, target);
            if (worldDistance <= Mathf.Epsilon)
            {
                return baseDuration;
            }

            return baseDuration * Mathf.Max(1f, worldDistance);
        }

        /// <summary>
        /// Updates the sprite animator (when present) so locomotion visuals match movement velocity.
        /// </summary>
        /// <param name="velocity">World-space velocity used to drive directional animation.</param>
        private void UpdateMovementVisuals(Vector2 velocity)
        {
            if (spriteAnimator == null)
            {
                return;
            }

            Vector2 resolvedVelocity = velocity.sqrMagnitude > 0.0001f ? velocity : Vector2.zero;
            spriteAnimator.UpdateVisuals(resolvedVelocity);
        }

        /// <summary>
        /// Keeps the NPC's facing direction aligned with the most recent movement delta.
        /// </summary>
        /// <param name="currentPosition">Interpolated position applied during the current step.</param>
        private void UpdateFacingDuringStep(Vector2 currentPosition)
        {
            Vector2 delta = currentPosition - previousStepPosition;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                // When interpolation produces an extremely small delta (e.g., during the final frame
                // of a step), fall back to the remaining distance so the NPC still faces the goal.
                delta = currentStepTarget - currentPosition;
                if (delta.sqrMagnitude <= Mathf.Epsilon)
                {
                    UpdateMovementVisuals(Vector2.zero);
                    return;
                }
            }

            if (facing != null)
            {
                facing.FaceDirection(delta);
            }

            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            UpdateMovementVisuals(delta / deltaTime);
            if (delta.sqrMagnitude > 0.000001f)
            {
                // Only treat meaningful deltas as progress so the stuck timer ignores sub-pixel noise.
                lastProgressTimestamp = Time.time;
            }
            previousStepPosition = currentPosition;
        }

        /// <summary>
        /// Attempts to advance to the next waypoint immediately, resuming the wanderer when no waypoints remain.
        /// </summary>
        /// <returns>True if a new waypoint step began; otherwise false.</returns>
        private bool TryAdvanceToNextWaypoint()
        {
            if (stepping)
            {
                return true;
            }

            if (waypointQueue.Count == 0)
            {
                ResumeWandererIfNeeded();
                UpdateMovementVisuals(Vector2.zero);
                return false;
            }

            Vector2 next = ClampWithWanderer(waypointQueue.Peek());

            if (hasDestination)
            {
                Vector2 currentPosition = GetCurrentPosition();
                bool hasClearStopLine = HasClearStopLine(currentPosition, desiredDestination);

                if (IsWithinStopRange(currentPosition))
                {
                    if (hasClearStopLine)
                    {
                        waypointQueue.Clear();
                        EvaluateArrival();
                        return false;
                    }
                }

                float distanceToNext = Vector2.Distance(currentPosition, next);
                // Guard against consuming a waypoint that is already inside the preferred stop range when it overlaps the NPC's
                // current position (common when replans enqueue redundant nodes).
                if (IsWithinStopRange(next) && distanceToNext <= waypointTolerance * 2f)
                {
                    if (hasClearStopLine)
                    {
                        waypointQueue.Clear();
                        EvaluateArrival(next);
                        return false;
                    }
                }
            }

            if (!IsWaypointWalkable(next))
            {
                ForceReplan();
                UpdateMovementVisuals(Vector2.zero);
                return false;
            }

            waypointQueue.Dequeue();
            BeginStep(next);
            return true;
        }

        /// <summary>
        /// Checks whether the destination has moved sufficiently to warrant a fresh path request.
        /// </summary>
        private void EvaluateDestinationDrift()
        {
            if (awaitingPath)
            {
                return;
            }

            if (Time.time < lastDestinationUpdate + repathCooldownSeconds)
            {
                return;
            }

            float drift = Vector2.Distance(desiredDestination, lastRequestedDestination);
            if (drift >= destinationRepathThreshold)
            {
                RequestPathInternal(desiredDestination, true);
            }
        }

        /// <summary>
        /// Validates whether the NPC is close enough to its destination or if another replan is required.
        /// </summary>
        /// <param name="stopRangeSampleOverride">Optional position sample (e.g., peeked waypoint) to evaluate against the stop range.</param>
        private void EvaluateArrival(Vector2? stopRangeSampleOverride = null)
        {
            Vector2 current = GetCurrentPosition();
            UpdateMovementVisuals(Vector2.zero);
            bool withinStopRange = IsWithinStopRange(current);
            if (!withinStopRange && stopRangeSampleOverride.HasValue)
            {
                withinStopRange = IsWithinStopRange(stopRangeSampleOverride.Value);
            }

            if (withinStopRange)
            {
                if (HasClearStopLine(current, desiredDestination))
                {
                    DestinationReached?.Invoke(this);
                    hasDestination = false;
                    hasResolvedPathDestination = false;
                    resolvedPathDestination = default;
                    waypointQueue.Clear();
                    ResumeWandererIfNeeded();
                }
                else
                {
                    bool atResolvedFallback = hasResolvedPathDestination &&
                        Vector2.Distance(current, resolvedPathDestination) <= waypointTolerance;

                    if (atResolvedFallback)
                    {
                        if (enableDebugLogging)
                        {
                            Debug.LogWarning($"NPC {name} reached resolved fallback but cannot see desired destination. Marking goal unreachable.", this);
                        }

                        hasDestination = false;
                        hasResolvedPathDestination = false;
                        resolvedPathDestination = default;
                        waypointQueue.Clear();
                        ResumeWandererIfNeeded();
                    }
                    else
                    {
                        ForceReplan(ignoreCooldown: true);
                    }
                }
            }
            else if (Time.time >= nextAllowedRepathTime)
            {
                RequestPathInternal(desiredDestination, true);
            }
        }

        /// <summary>
        /// Determines whether a position falls within the configured stop distance (including tolerance).
        /// </summary>
        /// <param name="position">World position to evaluate.</param>
        private bool IsWithinStopRange(Vector2 position)
        {
            return Vector2.Distance(position, desiredDestination) <= stopDistance + waypointTolerance;
        }

        /// <summary>
        /// Forces the mover to re-request a path, optionally bypassing the standard cooldown.
        /// </summary>
        private void ForceReplan(bool ignoreCooldown = false)
        {
            if (!hasDestination)
            {
                return;
            }

            if (!ignoreCooldown && Time.time < nextAllowedRepathTime)
            {
                return;
            }

            if (enableDebugLogging)
            {
                Debug.Log($"NPC {name} forcing path replan.", this);
            }

            RequestPathInternal(desiredDestination, ignoreCooldown);
        }

        /// <summary>
        /// Internal helper that queues a path request with the shared service and tracks cooldowns.
        /// </summary>
        private void RequestPathInternal(Vector2 destination, bool force)
        {
            EnsureServiceReference();
            if (pathService == null)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"NPC {name} cannot request path: service missing.", this);
                }
                UpdateMovementVisuals(Vector2.zero);
                return;
            }

            if (!force && Time.time < nextAllowedRepathTime)
            {
                return;
            }

            SuspendWanderer();
            awaitingPath = true;
            stepping = false;
            waypointQueue.Clear();
            debugPath.Clear();
            hasResolvedPathDestination = false;
            resolvedPathDestination = default;
            lastRequestedDestination = destination;
            nextAllowedRepathTime = Time.time + repathCooldownSeconds;
            lastDestinationUpdate = Time.time;
            UpdateMovementVisuals(Vector2.zero);

            Vector2 start = GetCurrentPosition();
            activeRequestId = pathService.RequestPath(this, start, destination);

            if (activeRequestId < 0)
            {
                awaitingPath = false;
                ScheduleRetry();
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"NPC {name} failed to queue a path request.", this);
                }
                UpdateMovementVisuals(Vector2.zero);
                return;
            }

            if (enableDebugLogging)
            {
                Debug.Log($"NPC {name} requested path {activeRequestId} -> {destination}.", this);
            }
        }

        /// <summary>
        /// Bumps the replan timer so the mover can retry once any transient issue clears.
        /// </summary>
        private void ScheduleRetry()
        {
            nextAllowedRepathTime = Time.time + repathCooldownSeconds;
        }

        /// <summary>
        /// Verifies that a waypoint remains walkable according to the active navigation grid.
        /// </summary>
        private bool IsWaypointWalkable(Vector2 waypoint)
        {
            var grid = pathService != null ? pathService.ActiveGrid : PathfindingService.Instance?.ActiveGrid;
            if (grid == null)
            {
                return true;
            }

            return grid.IsWorldPositionWalkable(waypoint);
        }

        /// <summary>
        /// Checks whether the straight corridor between two positions remains free of nav-grid blockers.
        /// </summary>
        private bool HasClearStopLine(Vector2 origin, Vector2 goal)
        {
            var grid = pathService != null ? pathService.ActiveGrid : PathfindingService.Instance?.ActiveGrid;
            if (grid == null || !grid.HasGrid)
            {
                return true;
            }

            if (!grid.TryGetCell(origin, out var startCell) || !grid.TryGetCell(goal, out var goalCell))
            {
                // When either endpoint lies outside the baked grid we cannot evaluate blockers reliably,
                // so allow the move completion to proceed and fall back to the standard arrival logic.
                return true;
            }

            return grid.HasClearLineBetweenCells(startCell, goalCell);
        }

        /// <summary>
        /// Verifies that the straight corridor to the current step target remains unobstructed.
        /// This keeps smoothed paths responsive when new blockers appear mid-segment.
        /// </summary>
        /// <param name="origin">NPC position sample used for the corridor test.</param>
        /// <param name="goal">Current waypoint being traversed.</param>
        private bool HasClearStepLine(Vector2 origin, Vector2 goal)
        {
            var grid = pathService != null ? pathService.ActiveGrid : PathfindingService.Instance?.ActiveGrid;
            if (grid == null || !grid.HasGrid)
            {
                return true;
            }

            if (!grid.TryGetCell(origin, out var startCell) || !grid.TryGetCell(goal, out var goalCell))
            {
                // When either endpoint lies outside the grid we cannot conclusively evaluate the corridor,
                // so fall back to allowing the current step to continue. Arrival logic will perform the
                // standard validation once the NPC reaches the waypoint.
                return true;
            }

            return grid.HasClearLineBetweenCells(startCell, goalCell);
        }

        /// <summary>
        /// Moves the NPC to the supplied position, using the rigidbody when available.
        /// </summary>
        private void ApplyPosition(Vector2 position, bool snapToGrid)
        {
            Vector2 clampedPosition = ClampWithWanderer(position, snapToGrid);

            if (body == null)
            {
                transform.position = new Vector3(clampedPosition.x, clampedPosition.y, transform.position.z);
                return;
            }

            if (body.bodyType == RigidbodyType2D.Dynamic)
            {
                // Dynamic bodies are advanced during FixedUpdate, so defer to MovePosition to keep physics contacts stable.
                body.MovePosition(clampedPosition);
                return;
            }

            // Kinematic (and other non-dynamic) bodies expect direct position assignment so they respect the Update-driven lerp.
            body.position = clampedPosition;
        }

        /// <summary>
        /// Returns the NPC's current 2D position.
        /// </summary>
        private Vector2 GetCurrentPosition()
        {
            return body != null ? body.position : (Vector2)transform.position;
        }

        /// <summary>
        /// Resolves the provided position against the owning wanderer's bounds when available.
        /// </summary>
        private Vector2 ClampWithWanderer(Vector2 position, bool snapToGrid = true)
        {
            if (wanderer == null)
            {
                return position;
            }

            bool shouldSnap = snapToGrid && wanderer.NavValidationEnabled;
            return shouldSnap ? wanderer.ClampToMovementBounds(position) : wanderer.ClampToMovementBoundsNoSnap(position);
        }

        /// <summary>
        /// Temporarily disables the wanderer so tick-driven roaming does not fight manual pathing.
        /// </summary>
        private void SuspendWanderer()
        {
            if (wanderer != null && wanderer.enabled)
            {
                lastManualPosition = ClampWithWanderer(GetCurrentPosition());
                hasManualPositionSample = true;
                wanderer.enabled = false;
                wandererSuspended = true;
            }
        }

        /// <summary>
        /// Restores wanderer behaviour after manual movement completes.
        /// </summary>
        private void ResumeWandererIfNeeded()
        {
            if (wanderer == null)
            {
                return;
            }

            Vector2 syncedPosition = ClampWithWanderer(GetCurrentPosition());

            if (wandererSuspended)
            {
                wanderer.enabled = true;
                wandererSuspended = false;
                wanderer.SyncToExternalPosition(syncedPosition);
                lastManualPosition = syncedPosition;
                hasManualPositionSample = true;
                return;
            }

            if (!wanderer.enabled)
            {
                return;
            }

            if (!hasManualPositionSample)
            {
                lastManualPosition = syncedPosition;
                hasManualPositionSample = true;
                return;
            }

            Vector2 displacement = syncedPosition - lastManualPosition;
            if (displacement.sqrMagnitude > ManualResyncToleranceSqr)
            {
                wanderer.SyncToExternalPosition(syncedPosition);
                lastManualPosition = syncedPosition;
            }
        }

        /// <summary>
        /// Caches the shared pathfinding service, bootstrapping it if necessary.
        /// </summary>
        private void EnsureServiceReference()
        {
            if (pathService == null)
            {
                pathService = PathfindingService.Instance;
            }
        }

        /// <summary>
        /// Subscribes the mover to the global ticker so it receives OSRS tick callbacks.
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
        /// Removes the mover from the global ticker subscription list.
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
        /// Waits for the ticker singleton to become available before registering.
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

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawDebugPath)
            {
                return;
            }

            if (debugPath == null || debugPath.Count == 0)
            {
                return;
            }

            Gizmos.color = debugPathColor;
            Vector3 previous = transform.position;
            for (int i = 0; i < debugPath.Count; i++)
            {
                Vector3 target = new Vector3(debugPath[i].x, debugPath[i].y, previous.z);
                Gizmos.DrawLine(previous, target);
                Gizmos.DrawSphere(target, 0.1f);
                previous = target;
            }
        }
#endif
    }
}
