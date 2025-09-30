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
    public sealed class NpcPathMover : MonoBehaviour, ITickable
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
        private float lastProgressTimestamp;
        private float nextAllowedRepathTime;
        private float lastDestinationUpdate;

        private Vector2 desiredDestination;
        private Vector2 lastRequestedDestination;
        private bool hasDestination;
        private int activeRequestId = -1;
        private bool wandererSuspended;

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
            float duration = Mathf.Max(0.01f, tileTraverseDuration);
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
            hasDestination = false;
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
        internal void HandlePathResult(int requestId, PathfindingService.PathStatus status, List<Vector2> worldPath, Vector2 goalWorld)
        {
            if (requestId != activeRequestId)
            {
                return;
            }

            awaitingPath = false;
            activeRequestId = -1;
            desiredDestination = goalWorld;

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

                ScheduleRetry();
                UpdateMovementVisuals(Vector2.zero);
                return;
            }

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

            Vector2 direction = currentStepTarget - currentStepStart;
            if (direction.sqrMagnitude > Mathf.Epsilon)
            {
                facing?.FaceDirection(direction);
                float duration = Mathf.Max(0.0001f, tileTraverseDuration);
                UpdateMovementVisuals(direction / duration);
            }
            else
            {
                UpdateMovementVisuals(Vector2.zero);
            }
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
        private void EvaluateArrival()
        {
            Vector2 current = GetCurrentPosition();
            UpdateMovementVisuals(Vector2.zero);
            if (Vector2.Distance(current, desiredDestination) <= stopDistance + waypointTolerance)
            {
                DestinationReached?.Invoke(this);
                hasDestination = false;
                ResumeWandererIfNeeded();
            }
            else if (Time.time >= nextAllowedRepathTime)
            {
                RequestPathInternal(desiredDestination, true);
            }
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
            desiredDestination = destination;
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

            return snapToGrid ? wanderer.ClampToMovementBounds(position) : wanderer.ClampToMovementBoundsNoSnap(position);
        }

        /// <summary>
        /// Temporarily disables the wanderer so tick-driven roaming does not fight manual pathing.
        /// </summary>
        private void SuspendWanderer()
        {
            if (wanderer != null && wanderer.enabled)
            {
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
                return;
            }

            if (wanderer.enabled)
            {
                wanderer.SyncToExternalPosition(syncedPosition);
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
