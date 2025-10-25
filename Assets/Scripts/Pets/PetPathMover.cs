using System;
using System.Collections.Generic;
using NPC;
using UnityEngine;
using Util;

namespace Pets
{
    /// <summary>
    /// Lightweight navigation helper that requests pet-friendly paths from <see cref="PathfindingService"/> and
    /// exposes the next waypoint to callers. Unlike <see cref="NPC.NpcPathMover"/> this mover does not directly
    /// drive animation or ticker integration which keeps it lean enough for pets that primarily operate inside
    /// <see cref="PetFollower"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PetPathMover : MonoBehaviour, IPathMoverClient
    {
        private enum Mode
        {
            None,
            Follow,
            Attack,
            Wander
        }

        [Header("Path Settings")]
        [Tooltip("Distance considered \"close enough\" to a waypoint before the mover advances to the next entry.")]
        [SerializeField, Min(0.005f)] private float defaultWaypointTolerance = 0.05f;

        [Tooltip("Cooldown applied after a path request before another replan is permitted.")]
        [SerializeField, Min(0.05f)] private float repathCooldownSeconds = 0.6f;

        [Tooltip("If the mover fails to make progress for longer than this threshold it forces a replan.")]
        [SerializeField, Min(0.1f)] private float stuckTimeoutSeconds = 2f;

        [Tooltip("Optional logging toggle used when diagnosing navigation flow in-editor.")]
        [SerializeField] private bool enableDebugLogging;

        private readonly Queue<Vector2> waypointQueue = new Queue<Vector2>();

        private PathfindingService pathService;
        private Mode currentMode = Mode.None;
        private bool awaitingPath;
        private int activeRequestId = -1;
        private Vector2 resolvedDestination;
        private bool hasResolvedDestination;
        private Vector2 lastRequestedDestination;
        private bool hasLastRequestedDestination;
        private float nextAllowedRepathTime;
        private float lastProgressTimestamp;
        private Vector2 lastProgressPosition;
        private bool hasProgressSample;
        private Vector2 currentVelocity;
        private bool pendingTeleport;
        private Vector2 teleportDestination;
        private bool pendingWanderFailure;
        private bool pendingAttackFailure;
        // Tracks whether we have already warned about the missing pathfinding service to avoid log spam while waiting.
        private bool hasLoggedMissingService;
        private DynamicNavOccupancyService.ReservationHandle activeReservationHandle;

        private Func<Vector2> followAnchorResolver;
        private Func<Vector2> wanderDestinationResolver;

        /// <summary>
        /// Delegate used to resolve the current follow anchor. Typically assigned by <see cref="PetFollower"/>.
        /// </summary>
        public Func<Vector2> FollowAnchorResolver
        {
            get => followAnchorResolver;
            set
            {
                followAnchorResolver = value;
                hasLastRequestedDestination = false;
            }
        }

        /// <summary>
        /// Delegate used to resolve wander targets when a new idle path is needed.
        /// </summary>
        public Func<Vector2> WanderDestinationResolver
        {
            get => wanderDestinationResolver;
            set
            {
                wanderDestinationResolver = value;
                hasLastRequestedDestination = false;
            }
        }

        /// <summary>
        /// Latest velocity produced while consuming navigation waypoints. Used by <see cref="PetFollower"/> to
        /// drive sprites.
        /// </summary>
        public Vector2 CurrentVelocity => currentVelocity;

        /// <summary>
        /// Indicates whether the mover currently has an active navigation grid available. Callers can use this
        /// to decide when to fall back to direct movement.
        /// </summary>
        public bool HasActiveNavigationGrid
        {
            get
            {
                // Always attempt to bind the navigation service when queried so callers can
                // transition to grid-aware movement as soon as the service becomes available.
                if (!EnsureServiceReference())
                {
                    return false;
                }

                var grid = pathService.ActiveGrid;
                return grid != null && grid.HasGrid;
            }
        }

        /// <summary>
        /// Returns true if the most recent wander request failed because the goal was unreachable. Call
        /// <see cref="ConsumePendingWanderFailure"/> to clear the flag once it has been processed.
        /// </summary>
        public bool HasPendingWanderFailure => pendingWanderFailure;

        /// <summary>
        /// Clears the wander failure flag so it can fire again on subsequent path attempts.
        /// </summary>
        public void ConsumePendingWanderFailure()
        {
            pendingWanderFailure = false;
        }

        /// <summary>
        /// Resets the cached velocity so callers can clear animation drift when movement settles.
        /// </summary>
        public void ResetCachedVelocity()
        {
            currentVelocity = Vector2.zero;
        }

        /// <summary>
        /// Provides a follow step using the active navigation grid. When the grid is unavailable the method
        /// returns <c>false</c> so callers can fall back to smooth damp behaviour.
        /// </summary>
        /// <param name="deltaTime">Delta time supplied by <see cref="Time.fixedDeltaTime"/>.</param>
        /// <param name="moveSpeed">Move speed applied while traversing the path.</param>
        /// <param name="stopDistance">Preferred stand-off distance from the anchor.</param>
        /// <param name="waypointTolerance">Distance threshold used when consuming waypoints.</param>
        /// <param name="replanDistance">Distance the anchor must drift before the mover will queue a new path.</param>
        /// <param name="teleportDistance">Distance that indicates the anchor teleported, forcing a snap.</param>
        /// <param name="nextPosition">Next world position along the path.</param>
        /// <param name="velocity">Velocity to apply for sprite animation.</param>
        /// <param name="teleported">True if the mover requests an instant teleport to recover.</param>
        /// <returns>True when navigation data was used to compute movement.</returns>
        public bool TryStepFollow(
            float deltaTime,
            float moveSpeed,
            float stopDistance,
            float waypointTolerance,
            float replanDistance,
            float teleportDistance,
            out Vector2 nextPosition,
            out Vector2 velocity,
            out bool teleported)
        {
            nextPosition = transform.position;
            velocity = Vector2.zero;
            teleported = false;
            currentVelocity = Vector2.zero;

            if (followAnchorResolver == null)
            {
                ResetFollowTracking();
                return false;
            }

            Vector2 anchor = followAnchorResolver();
            Vector2 currentPosition = transform.position;

            SwitchMode(Mode.Follow);

            if (pendingTeleport)
            {
                pendingTeleport = false;
                teleported = true;
                nextPosition = teleportDestination;
                ClearPathData();
                return true;
            }

            if (!EnsureServiceReference())
            {
                ResetFollowTracking();
                return false;
            }

            var grid = pathService.ActiveGrid;
            if (grid == null || !grid.HasGrid)
            {
                ResetFollowTracking();
                return false;
            }

            float anchorDelta = hasLastRequestedDestination
                ? Vector2.Distance(anchor, lastRequestedDestination)
                : float.MaxValue;

            bool anchorTeleported = Vector2.Distance(currentPosition, anchor) >= teleportDistance;
            bool anchorShifted = anchorDelta >= replanDistance;
            bool destinationShifted = hasResolvedDestination && Vector2.Distance(resolvedDestination, anchor) >= replanDistance;

            if (anchorTeleported)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"{name} detected follow anchor teleport. Snapping to {anchor}.", this);
                }

                teleportDestination = anchor;
                pendingTeleport = true;
                teleported = true;
                nextPosition = teleportDestination;
                ClearPathData();
                return true;
            }

            if (!awaitingPath && (waypointQueue.Count == 0 || anchorShifted || destinationShifted))
            {
                RequestPath(currentPosition, anchor, Mode.Follow);
            }
            else if (awaitingPath && anchorShifted)
            {
                CancelOutstandingRequest();
                RequestPath(currentPosition, anchor, Mode.Follow);
            }

            bool awaitingWithDestination = awaitingPath && hasResolvedDestination;

            if (hasResolvedDestination)
            {
                float distanceToDestination = Vector2.Distance(currentPosition, resolvedDestination);
                if (distanceToDestination <= Mathf.Max(stopDistance, waypointTolerance))
                {
                    ClearPathData();
                    return false;
                }
            }

            if (awaitingWithDestination)
            {
                // Continue advancing toward the last known destination while a refreshed path is in-flight.
                return StepToward(resolvedDestination, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, anchor);
            }

            if (awaitingPath)
            {
                return false;
            }

            if (waypointQueue.Count == 0)
            {
                Vector2 target = hasResolvedDestination ? resolvedDestination : anchor;
                return StepToward(target, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, anchor);
            }

            Vector2 waypoint = waypointQueue.Peek();
            bool stepped = StepToward(waypoint, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, anchor);

            if (stepped && Vector2.Distance(nextPosition, waypoint) <= Mathf.Max(waypointTolerance, defaultWaypointTolerance))
            {
                waypointQueue.Dequeue();
                activeReservationHandle?.MarkWaypointConsumed();
            }

            if (Time.time - lastProgressTimestamp >= stuckTimeoutSeconds)
            {
                ForceReplan(anchor, currentPosition, Mode.Follow);
            }

            return stepped;
        }

        /// <summary>
        /// Provides an attack step mirroring the follow logic while allowing callers to
        /// supply a bespoke resolver for the target position. This keeps combat code free
        /// from navigation concerns while ensuring pets correctly path around obstacles to
        /// reach NPCs.
        /// </summary>
        /// <param name="deltaTime">Frame delta used when advancing along the path.</param>
        /// <param name="moveSpeed">Movement speed applied while pursuing the target.</param>
        /// <param name="stopDistance">Preferred distance to stop from the resolved goal.</param>
        /// <param name="waypointTolerance">Tolerance applied when consuming waypoints.</param>
        /// <param name="targetResolver">Resolver that returns the target position each frame.</param>
        /// <param name="replanDistance">Distance the target must move before forcing a replan.</param>
        /// <param name="teleportDistance">Distance that indicates the target teleported.</param>
        /// <param name="nextPosition">Next world position produced by the path.</param>
        /// <param name="velocity">Velocity to forward to movement/animation systems.</param>
        /// <param name="teleported">True if the mover should snap directly to the goal.</param>
        /// <param name="goalUnreachable">True if the pathfinder reported the goal as unreachable.</param>
        /// <returns>True when navigation data produced a movement step.</returns>
        public bool TryStepAttack(
            float deltaTime,
            float moveSpeed,
            float stopDistance,
            float waypointTolerance,
            Func<Vector2> targetResolver,
            float replanDistance,
            float teleportDistance,
            out Vector2 nextPosition,
            out Vector2 velocity,
            out bool teleported,
            out bool goalUnreachable)
        {
            nextPosition = transform.position;
            velocity = Vector2.zero;
            teleported = false;
            goalUnreachable = pendingAttackFailure;
            currentVelocity = Vector2.zero;

            if (pendingAttackFailure)
            {
                return false;
            }

            if (targetResolver == null)
            {
                ResetAttackTracking();
                return false;
            }

            Vector2 target = targetResolver();
            Vector2 currentPosition = transform.position;

            SwitchMode(Mode.Attack);

            if (pendingTeleport)
            {
                pendingTeleport = false;
                teleported = true;
                nextPosition = teleportDestination;
                ClearPathData();
                return true;
            }

            if (!EnsureServiceReference())
            {
                ResetAttackTracking();
                return false;
            }

            var grid = pathService.ActiveGrid;
            if (grid == null || !grid.HasGrid)
            {
                ResetAttackTracking();
                return false;
            }

            float targetDelta = hasLastRequestedDestination
                ? Vector2.Distance(target, lastRequestedDestination)
                : float.MaxValue;

            bool targetTeleported = Vector2.Distance(currentPosition, target) >= teleportDistance;
            bool targetShifted = targetDelta >= replanDistance;
            bool destinationShifted = hasResolvedDestination && Vector2.Distance(resolvedDestination, target) >= replanDistance;

            if (targetTeleported)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"{name} detected attack target teleport. Snapping to {target}.", this);
                }

                teleportDestination = target;
                pendingTeleport = true;
                teleported = true;
                nextPosition = teleportDestination;
                ClearPathData();
                return true;
            }

            if (!awaitingPath && (waypointQueue.Count == 0 || targetShifted || destinationShifted))
            {
                RequestPath(currentPosition, target, Mode.Attack);
            }
            else if (awaitingPath && targetShifted)
            {
                CancelOutstandingRequest();
                RequestPath(currentPosition, target, Mode.Attack);
            }

            bool awaitingWithDestination = awaitingPath && hasResolvedDestination;

            if (hasResolvedDestination)
            {
                float distanceToDestination = Vector2.Distance(currentPosition, resolvedDestination);
                if (distanceToDestination <= Mathf.Max(stopDistance, waypointTolerance))
                {
                    ClearPathData();
                    return false;
                }
            }

            if (awaitingWithDestination)
            {
                // Continue pursuing the previous destination while the new path request is pending.
                return StepToward(resolvedDestination, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, target);
            }

            if (awaitingPath)
            {
                return false;
            }

            if (waypointQueue.Count == 0)
            {
                Vector2 destination = hasResolvedDestination ? resolvedDestination : target;
                return StepToward(destination, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, target);
            }

            Vector2 waypoint = waypointQueue.Peek();
            bool stepped = StepToward(waypoint, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, target);

            if (stepped && Vector2.Distance(nextPosition, waypoint) <= Mathf.Max(waypointTolerance, defaultWaypointTolerance))
            {
                waypointQueue.Dequeue();
                activeReservationHandle?.MarkWaypointConsumed();
            }

            if (Time.time - lastProgressTimestamp >= stuckTimeoutSeconds)
            {
                ForceReplan(target, currentPosition, Mode.Attack);
            }

            return stepped;
        }

        /// <summary>
        /// Provides a wander step when navigation data is available.
        /// </summary>
        public bool TryStepWander(
            float deltaTime,
            float moveSpeed,
            float stopDistance,
            float waypointTolerance,
            out Vector2 nextPosition,
            out Vector2 velocity)
        {
            nextPosition = transform.position;
            velocity = Vector2.zero;
            currentVelocity = Vector2.zero;

            if (wanderDestinationResolver == null)
            {
                ResetWanderTracking();
                return false;
            }

            Vector2 target = wanderDestinationResolver();
            Vector2 currentPosition = transform.position;

            SwitchMode(Mode.Wander);

            if (!EnsureServiceReference())
            {
                ResetWanderTracking();
                return false;
            }

            var grid = pathService.ActiveGrid;
            if (grid == null || !grid.HasGrid)
            {
                ResetWanderTracking();
                return false;
            }

            bool targetShifted = hasLastRequestedDestination
                ? Vector2.Distance(target, lastRequestedDestination) >= waypointTolerance
                : true;

            if (!awaitingPath && (waypointQueue.Count == 0 || targetShifted))
            {
                RequestPath(currentPosition, target, Mode.Wander);
            }
            else if (awaitingPath && targetShifted)
            {
                CancelOutstandingRequest();
                RequestPath(currentPosition, target, Mode.Wander);
            }

            bool awaitingWithDestination = awaitingPath && hasResolvedDestination;

            if (hasResolvedDestination)
            {
                float distanceToDestination = Vector2.Distance(currentPosition, resolvedDestination);
                if (distanceToDestination <= Mathf.Max(stopDistance, waypointTolerance))
                {
                    ClearPathData();
                    return false;
                }
            }

            if (awaitingWithDestination)
            {
                // Keep drifting toward the last resolved wander destination while awaiting the refreshed path.
                return StepToward(resolvedDestination, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, target);
            }

            if (awaitingPath)
            {
                return false;
            }

            if (waypointQueue.Count == 0)
            {
                Vector2 destination = hasResolvedDestination ? resolvedDestination : target;
                return StepToward(destination, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, target);
            }

            Vector2 waypoint = waypointQueue.Peek();
            bool stepped = StepToward(waypoint, currentPosition, moveSpeed, deltaTime, out nextPosition, out velocity, waypointTolerance, target);

            if (stepped && Vector2.Distance(nextPosition, waypoint) <= Mathf.Max(waypointTolerance, defaultWaypointTolerance))
            {
                waypointQueue.Dequeue();
                activeReservationHandle?.MarkWaypointConsumed();
            }

            if (Time.time - lastProgressTimestamp >= stuckTimeoutSeconds)
            {
                ForceReplan(target, currentPosition, Mode.Wander);
            }

            return stepped;
        }

        /// <summary>
        /// Clears follow state and abandons any queued requests.
        /// </summary>
        public void ResetFollowTracking()
        {
            if (currentMode == Mode.Follow)
            {
                ClearPathData();
            }

            pendingTeleport = false;
            hasLastRequestedDestination = false;
            ReleaseReservationHandle();
        }

        /// <summary>
        /// Clears attack state so future engagements start with a clean navigation slate.
        /// </summary>
        public void ResetAttackTracking()
        {
            if (currentMode == Mode.Attack)
            {
                ClearPathData();
            }

            pendingTeleport = false;
            pendingAttackFailure = false;
            hasLastRequestedDestination = false;
            ReleaseReservationHandle();
        }

        /// <summary>
        /// Clears wander state and abandons pending requests.
        /// </summary>
        public void ResetWanderTracking()
        {
            if (currentMode == Mode.Wander)
            {
                ClearPathData();
            }

            pendingWanderFailure = false;
            hasLastRequestedDestination = false;
            ReleaseReservationHandle();
        }

        /// <inheritdoc />
        public void HandlePathResult(int requestId, PathfindingService.PathStatus status, List<Vector2> worldPath, Vector2 resolvedGoalWorld)
        {
            if (requestId != activeRequestId)
            {
                return;
            }

            awaitingPath = false;
            activeRequestId = -1;
            waypointQueue.Clear();
            hasResolvedDestination = false;
            currentVelocity = Vector2.zero;

            if (status == PathfindingService.PathStatus.Success && worldPath != null)
            {
                hasResolvedDestination = true;
                resolvedDestination = resolvedGoalWorld;

                for (int i = 0; i < worldPath.Count; i++)
                {
                    waypointQueue.Enqueue(worldPath[i]);
                }

                if (enableDebugLogging)
                {
                    Debug.Log($"{name} received pet path with {waypointQueue.Count} waypoints ({currentMode}).", this);
                }

                pendingWanderFailure = false;
                pendingAttackFailure = false;
                lastProgressTimestamp = Time.time;
                return;
            }

            if (status == PathfindingService.PathStatus.GoalUnreachable)
            {
                ReleaseReservationHandle();
                if (currentMode == Mode.Follow)
                {
                    teleportDestination = resolvedGoalWorld;
                    pendingTeleport = true;
                }
                else if (currentMode == Mode.Wander)
                {
                    pendingWanderFailure = true;
                }
                else if (currentMode == Mode.Attack)
                {
                    pendingAttackFailure = true;
                }

                if (enableDebugLogging)
                {
                    Debug.LogWarning($"{name} pet path request {requestId} unreachable during {currentMode}.", this);
                }

                return;
            }

            ReleaseReservationHandle();
            if (enableDebugLogging)
            {
                Debug.LogWarning($"{name} pet path request {requestId} failed: {status}.", this);
            }
        }

        private bool EnsureServiceReference()
        {
            if (pathService != null)
            {
                return true;
            }

            PathfindingService instance = PathfindingService.Instance;
            if (instance == null)
            {
                if (!hasLoggedMissingService && enableDebugLogging)
                {
                    Debug.LogWarning($"{name} is waiting for PathfindingService to initialise before processing pet navigation.", this);
                    hasLoggedMissingService = true;
                }

                return false;
            }

            pathService = instance;
            hasLoggedMissingService = false;
            return true;
        }

        private void RequestPath(Vector2 start, Vector2 goal, Mode mode)
        {
            if (!EnsureServiceReference())
            {
                return;
            }

            if (Time.time < nextAllowedRepathTime)
            {
                return;
            }

            int requestId = pathService.RequestPath(this, start, goal);
            if (requestId < 0)
            {
                return;
            }

            currentMode = mode;
            awaitingPath = true;
            activeRequestId = requestId;
            lastRequestedDestination = goal;
            hasLastRequestedDestination = true;
            nextAllowedRepathTime = Time.time + repathCooldownSeconds;
            lastProgressTimestamp = Time.time;

            if (enableDebugLogging)
            {
                Debug.Log($"{name} queued pet path {requestId} -> {goal}.", this);
            }
        }

        private void CancelOutstandingRequest()
        {
            awaitingPath = false;
            activeRequestId = -1;
            ReleaseReservationHandle();
        }

        private void ForceReplan(Vector2 goal, Vector2 currentPosition, Mode mode)
        {
            if (Time.time < nextAllowedRepathTime)
            {
                return;
            }

            CancelOutstandingRequest();
            RequestPath(currentPosition, goal, mode);
        }

        private void SwitchMode(Mode mode)
        {
            if (currentMode == mode)
            {
                return;
            }

            ClearPathData();
            currentMode = mode;
            pendingTeleport = false;
            pendingWanderFailure = false;
            pendingAttackFailure = false;
            hasLastRequestedDestination = false;
        }

        private void ClearPathData()
        {
            ReleaseReservationHandle();
            waypointQueue.Clear();
            awaitingPath = false;
            activeRequestId = -1;
            hasResolvedDestination = false;
            currentVelocity = Vector2.zero;
            lastProgressTimestamp = Time.time;
            hasProgressSample = false;
        }

        private bool StepToward(
            Vector2 target,
            Vector2 currentPosition,
            float moveSpeed,
            float deltaTime,
            out Vector2 nextPosition,
            out Vector2 velocity,
            float waypointTolerance,
            Vector2 goalForReplan)
        {
            float maxDistance = Mathf.Max(0.0001f, moveSpeed * Mathf.Max(deltaTime, 0.0001f));
            Vector2 stepped = Vector2.MoveTowards(currentPosition, target, maxDistance);
            velocity = (stepped - currentPosition) / Mathf.Max(deltaTime, 0.0001f);
            nextPosition = stepped;
            currentVelocity = velocity;

            if (velocity.sqrMagnitude > 0.0001f)
            {
                lastProgressTimestamp = Time.time;
                lastProgressPosition = stepped;
                hasProgressSample = true;
            }
            else if (hasProgressSample && Vector2.Distance(stepped, lastProgressPosition) <= waypointTolerance)
            {
                if (Time.time - lastProgressTimestamp >= stuckTimeoutSeconds)
                {
                    ForceReplan(goalForReplan, currentPosition, currentMode);
                }
            }

            return true;
        }

        private void ReleaseReservationHandle()
        {
            if (activeReservationHandle == null)
            {
                return;
            }

            activeReservationHandle.ReleaseAll();
            activeReservationHandle = null;
        }

        public int GetReservationRadius()
        {
            return 0;
        }

        public int GetReservationDurationTicks()
        {
            float durationTicks = stuckTimeoutSeconds / Mathf.Max(Ticker.TickDuration, 0.0001f);
            return Mathf.Max(1, Mathf.CeilToInt(durationTicks));
        }

        public void BindReservationHandle(int requestId, DynamicNavOccupancyService.ReservationHandle handle)
        {
            if (handle == activeReservationHandle)
            {
                return;
            }

            if (activeReservationHandle != null)
            {
                activeReservationHandle.ReleaseAll();
            }

            activeReservationHandle = handle;
        }
    }
}
