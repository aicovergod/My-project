using System;
using System.Collections;
using System.Collections.Generic;
using Companions.Equipment;
using Skills;
using UI.Chat;
using UnityEngine;
using Util;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions
{
    /// <summary>
    /// Provides shared utilities for gathering-focused companion controllers. Consolidates the
    /// repeated follower hold bookkeeping, area command helpers, and node blocking buffers that
    /// fishing, mining, and woodcutting previously maintained independently.
    /// </summary>
    /// <typeparam name="TNode">Concrete node component (fish spot, rock, tree).</typeparam>
    /// <typeparam name="TCommandResult">Enum describing command outcomes for the derived skill.</typeparam>
    public abstract class CompanionGatheringControllerBase<TNode, TCommandResult> : CompanionSkillingControllerBase
        where TNode : Component
    {
        /// <summary>Distance in tiles required to interact with the target node.</summary>
        protected virtual float GatheringRange => 1.5f;

        /// <summary>Threshold distance that triggers a path recalculation while approaching a node.</summary>
        protected virtual float ReplanDistance => GatheringRange * 0.75f;

        /// <summary>Maximum distance allowed from a navigation waypoint before the companion considers it reached.</summary>
        protected virtual float WaypointTolerance => 0.1f;

        /// <summary>Minimum progress delta required before stuck timers reset.</summary>
        protected const float ProgressResetThreshold = 0.1f;

        /// <summary>Multiplier applied to the gather range when checking if the companion is effectively next to the node.</summary>
        protected const float CloseEnoughDistanceMultiplier = 0.9f;

        /// <summary>Number of consecutive stuck detections before area gathering cancels itself.</summary>
        protected const int ConsecutiveStuckCancelThreshold = 2;

        /// <summary>Cached list of candidate nodes discovered during area gathering scans.</summary>
        protected readonly List<TNode> areaCandidates = new List<TNode>();

        /// <summary>Tile centres corresponding to <see cref="areaCandidates"/> used for gizmo rendering.</summary>
        protected readonly List<Vector3> areaCandidateTileCenters = new List<Vector3>();

        /// <summary>Tracks nodes that were recently blocked so the companion can avoid immediately retrying them.</summary>
        protected readonly Dictionary<TNode, float> blockedNodes = new Dictionary<TNode, float>();

        /// <summary>Reusable buffer for pruning entries from <see cref="blockedNodes"/>.</summary>
        protected readonly List<TNode> blockedNodePruneBuffer = new List<TNode>();

        /// <summary>Coroutine responsible for sweeping candidates during area gathering.</summary>
        protected Coroutine areaRoutine;

        /// <summary>True while <see cref="areaRoutine"/> is running.</summary>
        protected bool areaRoutineActive;

        /// <summary>Scan radius used for the active area routine.</summary>
        protected float activeAreaRadius;

        /// <summary>Tracks whether the most recent area pass blocked every candidate node.</summary>
        protected bool areaAllCandidatesBlocked;

        /// <summary>Indicates whether this controller currently owns the follower hold.</summary>
        protected bool followerDisabledForGathering;

        /// <summary>Cooldown tracker shared across the gathering controllers.</summary>
        protected CompanionSkillCooldownTracker skillCooldownTracker;

        /// <summary>Last node that triggered the stuck handler.</summary>
        protected TNode lastStuckNode;

        /// <summary>Number of consecutive stuck detections recorded for <see cref="lastStuckNode"/>.</summary>
        protected int consecutiveStuckNodeCount;

        [Header("Stuck Detection")]
        [Tooltip("Grace period before a lack of progress is considered \"stuck\".")]
        [SerializeField, Min(0.1f)] protected float stuckTimeoutSeconds = 2.5f;

        /// <summary>
        /// Exposes whether any systems currently hold the follower disabled so other controllers can
        /// respect outstanding locks.
        /// </summary>
        public bool HasActiveFollowerHold => followerDisableLockCount > 0;

        /// <summary>
        /// Encapsulates the outcome of an attempted gathering command.
        /// </summary>
        protected struct CommandAttempt
        {
            public bool Accepted;
            public TCommandResult Result;
        }

        /// <summary>
        /// Provides a disposable handle that keeps the follower disabled until the command completes.
        /// </summary>
        private sealed class FollowerHold : IDisposable
        {
            private readonly CompanionGatheringControllerBase<TNode, TCommandResult> controller;

            public FollowerHold(CompanionGatheringControllerBase<TNode, TCommandResult> controller)
            {
                this.controller = controller;
            }

            public void Dispose()
            {
                controller?.ReleaseTemporaryFollowerHoldInternal();
            }
        }

        /// <summary>
        /// Disposable that intentionally does nothing. Returned when the follower component is missing.
        /// </summary>
        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new NoOpDisposable();

            private NoOpDisposable()
            {
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Determines the appropriate movement speed when advancing toward a gathering node. Falls back to a
        /// sensible default when the follower component is not available so manual controllers remain responsive.
        /// </summary>
        /// <returns>The speed, in Unity units per second, that should be applied to the navigation routine.</returns>
        protected float ResolveMoveSpeed()
        {
            return petFollower != null ? Mathf.Max(0.1f, petFollower.moveSpeed) : 5f;
        }

        /// <summary>
        /// Publishes a companion chat message by evaluating the provided resolver after confirming the
        /// chat service is present. This centralises the null checks that every gathering controller
        /// previously duplicated when emitting skill-specific dialogue lines.
        /// </summary>
        /// <param name="lineResolver">Delegate that returns the line which should be spoken.</param>
        protected void PublishCompanionChatLine(Func<string> lineResolver)
        {
            if (lineResolver == null)
                throw new ArgumentNullException(nameof(lineResolver));

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = lineResolver();
            if (string.IsNullOrEmpty(message))
                return;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Publishes the supplied companion chat line when the chat service is available.
        /// </summary>
        /// <param name="line">The text that should be broadcast to the chat window.</param>
        protected void PublishCompanionChatLine(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            PublishCompanionChatLine(() => line);
        }

        /// <summary>
        /// Encapsulates the callbacks required to drive the shared gathering movement routine. The
        /// delegates supplied here allow skill-specific controllers (fishing, mining, woodcutting)
        /// to inject their bespoke start/stop hooks while reusing the common navigation loop.
        /// </summary>
        protected struct GatheringMovementRoutineParameters
        {
            /// <summary>Accessor that returns the node currently targeted by the routine.</summary>
            public Func<TNode> GetTargetNode;

            /// <summary>Determines whether the command should continue executing.</summary>
            public Func<bool> IsCommandActive;

            /// <summary>Validates whether the supplied node remains interactable.</summary>
            public Func<TNode, bool> IsNodeValid;

            /// <summary>Resolves the world position the companion should approach.</summary>
            public Func<TNode, Vector3> GetTargetPosition;

            /// <summary>Indicates whether the bound skill is currently performing its gather action.</summary>
            public Func<bool> IsSkillActive;

            /// <summary>Invoked when the companion reaches the gather range and must begin the skill.</summary>
            public Action<TNode> StartSkill;

            /// <summary>Stops the bound skill when the companion moves out of range.</summary>
            public Action StopSkill;

            /// <summary>Invoked when progress stalls while approaching the node.</summary>
            public Action<TNode> OnProgressStalled;

            /// <summary>Invoked immediately when navigation reports an unreachable goal.</summary>
            public Action<TNode> OnGoalUnreachableDetected;

            /// <summary>Invoked when the navigation stack reports an unreachable goal.</summary>
            public Action<TNode> OnGoalUnreachable;

            /// <summary>Invoked when the stuck timer expires.</summary>
            public Action<TNode> OnStuck;

            /// <summary>Receives the final routine result after cleanup completes.</summary>
            public Action<GatheringMovementRoutineResult> OnRoutineComplete;

            /// <summary>Multiplier used when deciding to stop the active skill due to distance.</summary>
            public float OutOfRangeSkillStopMultiplier;
        }

        /// <summary>
        /// Communicates the outcome of the shared gathering movement routine to the calling
        /// controller so it can perform any follow-up cleanup or area gathering book-keeping.
        /// </summary>
        protected struct GatheringMovementRoutineResult
        {
            /// <summary>True when the companion declared a stuck state.</summary>
            public bool Stuck;

            /// <summary>True when the navigation layer reported the goal as unreachable.</summary>
            public bool GoalUnreachable;

            /// <summary>The node associated with the final state (stuck or successful completion).</summary>
            public TNode Node;
        }

        /// <summary>
        /// Drives the shared navigation, stuck detection, and follower hold logic used by all
        /// gathering-focused companion controllers. Concrete skills supply the delegates required
        /// to start/stop their skill actions while this helper keeps the movement loop consistent.
        /// </summary>
        /// <param name="parameters">Callbacks and configuration describing the active command.</param>
        /// <returns>Enumerator consumed by Unity's coroutine scheduler.</returns>
        protected IEnumerator CompanionGatheringMovementRoutine(GatheringMovementRoutineParameters parameters)
        {
            var followerHold = EnterTemporaryFollowerHold();
            bool stuckTriggered = false;
            bool goalUnreachable = false;
            TNode stuckNode = null;
            float noProgressTimer = 0f;
            float lastRecordedDistance = 0f;
            float cumulativeDistanceClosed = 0f;
            bool hasDistanceSample = false;

            try
            {
                pathMover?.ResetAttackTracking();

                while (true)
                {
                    if (!isActiveAndEnabled)
                        break;

                    if (parameters.IsCommandActive != null && !parameters.IsCommandActive())
                        break;

                    TNode targetNode = parameters.GetTargetNode != null ? parameters.GetTargetNode() : null;
                    if (targetNode == null)
                        break;

                    if (parameters.IsNodeValid != null && !parameters.IsNodeValid(targetNode))
                        break;

                    Vector3 targetPosition = parameters.GetTargetPosition != null
                        ? parameters.GetTargetPosition(targetNode)
                        : targetNode.transform.position;

                    float distance = Vector2.Distance(transform.position, targetPosition);

                    if (!hasDistanceSample)
                    {
                        hasDistanceSample = true;
                        lastRecordedDistance = distance;
                        cumulativeDistanceClosed = 0f;
                        noProgressTimer = 0f;
                    }
                    else
                    {
                        float delta = lastRecordedDistance - distance;
                        if (delta > 0f)
                        {
                            cumulativeDistanceClosed += delta;
                        }
                        else if (delta < 0f)
                        {
                            cumulativeDistanceClosed = 0f;
                        }

                        bool closedGap = cumulativeDistanceClosed >= ProgressResetThreshold;
                        bool effectivelyClose = distance <= GatheringRange * CloseEnoughDistanceMultiplier;
                        bool activelyGathering = parameters.IsSkillActive != null && parameters.IsSkillActive();

                        if (closedGap || effectivelyClose || activelyGathering)
                        {
                            noProgressTimer = 0f;
                            cumulativeDistanceClosed = 0f;
                        }
                        else
                        {
                            noProgressTimer += Time.deltaTime;
                        }

                        lastRecordedDistance = distance;
                    }

                    if (noProgressTimer >= stuckTimeoutSeconds)
                    {
                        parameters.OnProgressStalled?.Invoke(targetNode);

                        stuckTriggered = true;
                        stuckNode = targetNode;
                        break;
                    }

                    if (distance > GatheringRange)
                    {
                        float moveSpeed = ResolveMoveSpeed();
                        float deltaTime = body != null
                            ? Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon)
                            : Mathf.Max(Time.deltaTime, Mathf.Epsilon);

                        bool navigationStepTaken = false;
                        bool navigationUnavailable = true;

                        if (pathMover != null && pathMover.isActiveAndEnabled)
                        {
                            navigationUnavailable = !pathMover.HasActiveNavigationGrid;

                            if (!navigationUnavailable)
                            {
                                Vector2 nextPosition;
                                Vector2 navVelocity;
                                bool teleported;
                                bool unreachable;
                                float teleportDetectionDistance = float.PositiveInfinity;

                                navigationStepTaken = pathMover.TryStepAttack(
                                    deltaTime,
                                    moveSpeed,
                                    GatheringRange,
                                    WaypointTolerance,
                                    () =>
                                    {
                                        var refreshedNode = parameters.GetTargetNode != null ? parameters.GetTargetNode() : null;
                                        if (refreshedNode == null)
                                            return (Vector2)transform.position;

                                        Vector3 refreshedPosition = parameters.GetTargetPosition != null
                                            ? parameters.GetTargetPosition(refreshedNode)
                                            : refreshedNode.transform.position;
                                        return (Vector2)refreshedPosition;
                                    },
                                    ReplanDistance,
                                    teleportDetectionDistance,
                                    out nextPosition,
                                    out navVelocity,
                                    out teleported,
                                    out unreachable);

                                if (unreachable)
                                {
                                    parameters.OnGoalUnreachableDetected?.Invoke(targetNode);

                                    stuckTriggered = true;
                                    goalUnreachable = true;
                                    stuckNode = targetNode;
                                    break;
                                }

                                if (navigationStepTaken)
                                    ApplyMovement(nextPosition, navVelocity, teleported);
                            }
                        }

                        if (stuckTriggered)
                            break;

                        if (!navigationStepTaken)
                        {
                            if (navigationUnavailable)
                            {
                                Vector3 startPosition = transform.position;
                                Vector3 nextPosition = Vector3.MoveTowards(startPosition, targetPosition, moveSpeed * deltaTime);
                                Vector2 velocity = deltaTime > Mathf.Epsilon
                                    ? (Vector2)((nextPosition - startPosition) / deltaTime)
                                    : Vector2.zero;
                                ApplyMovement(nextPosition, velocity, false);
                            }
                            else if (body != null)
                            {
                                body.linearVelocity = Vector2.zero;
                            }
                        }

                        if (parameters.IsSkillActive != null && parameters.IsSkillActive())
                        {
                            float stopMultiplier = parameters.OutOfRangeSkillStopMultiplier <= 0f
                                ? 1.2f
                                : parameters.OutOfRangeSkillStopMultiplier;

                            if (distance > GatheringRange * stopMultiplier)
                                parameters.StopSkill?.Invoke();
                        }
                    }
                    else
                    {
                        if (body != null)
                            body.linearVelocity = Vector2.zero;

                        if (parameters.IsCommandActive != null && !parameters.IsCommandActive())
                            break;

                        if (parameters.IsSkillActive == null || !parameters.IsSkillActive())
                        {
                            if (parameters.IsCommandActive != null && !parameters.IsCommandActive())
                                break;

                            parameters.StartSkill?.Invoke(targetNode);
                        }

                        if (parameters.IsCommandActive != null && !parameters.IsCommandActive())
                            break;

                        if (parameters.IsSkillActive != null && !parameters.IsSkillActive())
                            break;
                    }

                    if (parameters.IsNodeValid != null && !parameters.IsNodeValid(targetNode))
                        break;

                    yield return null;
                }
            }
            finally
            {
                followerHold.Dispose();
            }

            var result = new GatheringMovementRoutineResult
            {
                Stuck = stuckTriggered,
                GoalUnreachable = goalUnreachable,
                Node = stuckNode != null
                    ? stuckNode
                    : parameters.GetTargetNode != null ? parameters.GetTargetNode() : null,
            };

            if (stuckTriggered)
            {
                if (goalUnreachable && parameters.OnGoalUnreachable != null)
                {
                    parameters.OnGoalUnreachable(stuckNode);
                }
                else
                {
                    parameters.OnStuck?.Invoke(stuckNode);
                }
            }

            parameters.OnRoutineComplete?.Invoke(result);
        }

        /// <summary>
        /// Handles the shared player-skill subscription workflow for gathering controllers. Ensures existing
        /// subscriptions are removed, invokes any cleanup callbacks, rebinds the cached skill reference against
        /// the supplied player transform, and finally wires the provided subscribe delegate when a skill is
        /// discovered.
        /// </summary>
        /// <typeparam name="TSkill">Concrete skill component that should be resolved from the player transform.</typeparam>
        /// <param name="playerTransform">Active player transform supplying the skill component.</param>
        /// <param name="cachedSkill">Reference that stores the currently bound player skill.</param>
        /// <param name="subscribe">Delegate invoked once a valid skill instance has been resolved.</param>
        /// <param name="unsubscribe">Delegate used to detach event handlers from the previously cached skill.</param>
        /// <param name="onUnbound">Optional callback executed whenever an existing skill binding is removed.</param>
        /// <param name="onBound">Optional callback executed after a new skill binding has been resolved.</param>
        protected void BindPlayerSkillEvents<TSkill>(
            Transform playerTransform,
            ref TSkill cachedSkill,
            Action<TSkill> subscribe,
            Action<TSkill> unsubscribe,
            Action onUnbound = null,
            Action<TSkill> onBound = null)
            where TSkill : Component
        {
            TSkill previousSkill = cachedSkill;

            if (previousSkill != null)
            {
                unsubscribe?.Invoke(previousSkill);
                onUnbound?.Invoke();
            }

            cachedSkill = null;

            if (playerTransform == null)
                return;

            RebindPlayerSkill(playerTransform, ref cachedSkill);

            if (cachedSkill == null)
                return;

            onBound?.Invoke(cachedSkill);
            subscribe?.Invoke(cachedSkill);
        }

        /// <summary>
        /// Temporarily disables the follower component so the companion remains stationary until gathering resumes.
        /// Dispose the returned handle to release the lock.
        /// </summary>
        public IDisposable EnterTemporaryFollowerHold()
        {
            if (petFollower == null)
                return NoOpDisposable.Instance;

            if (followerDisableLockCount > 0)
            {
                followerDisableLockCount++;
                followerDisabledForGathering = true;
                return new FollowerHold(this);
            }

            bool toggledFollower = false;

            if (petFollower.enabled)
            {
                petFollower.enabled = false;
                toggledFollower = true;
            }

            followerDisableLockCount = 1;
            followerDisabledForGathering = true;
            followerHoldToggledFollower = toggledFollower;
            return new FollowerHold(this);
        }

        /// <summary>
        /// Consolidates the shared gathering skill initialisation sequence so fishing, mining, and woodcutting
        /// controllers only provide skill-specific wiring.
        /// </summary>
        /// <typeparam name="TSkill">Concrete skill component that should be bound to the controller.</typeparam>
        /// <param name="ownerController">Controller that owns this component.</param>
        /// <param name="skills">Skill manager used for level validations.</param>
        /// <param name="inventoryComponent">Inventory wrapper exposing the companion backpack.</param>
        /// <param name="equipmentComponent">Equipment window associated with the companion.</param>
        /// <param name="cooldownTracker">Shared cooldown tracker used to coordinate gathering requests.</param>
        /// <param name="debugLabel">Label appended to debug output.</param>
        /// <param name="configureSkill">Invoked with the resolved skill to wire events and chat hooks.</param>
        /// <param name="resetSkillSpecificFlags">Callback that resets derived-class state after the shared reset.</param>
        /// <param name="resolvedInventory">Outputs the resolved runtime inventory.</param>
        /// <param name="resolvedEquipment">Outputs the resolved companion equipment reference.</param>
        /// <returns>The resolved skill component or <c>null</c> when it could not be created.</returns>
        protected TSkill ConfigureGatheringSkill<TSkill>(
            CompanionController ownerController,
            SkillManager skills,
            CompanionInventory inventoryComponent,
            CompanionEquipment equipmentComponent,
            CompanionSkillCooldownTracker cooldownTracker,
            string debugLabel,
            Action<TSkill> configureSkill,
            Action resetSkillSpecificFlags,
            out RuntimeInventory resolvedInventory,
            out CompanionEquipment resolvedEquipment)
            where TSkill : Component
        {
            if (ownerController == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning($"[{debugLabel}] Initialise invoked without a companion controller reference.", this);

            if (skills == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning($"[{debugLabel}] Initialise received a null SkillManager reference.", this);

            resolvedInventory = inventoryComponent != null ? inventoryComponent.InventoryComponent : null;
            if (resolvedInventory == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning($"[{debugLabel}] No inventory available for tool checks.", this);

            resolvedEquipment = equipmentComponent ?? ownerController?.Equipment;

            var skill = GetComponent<TSkill>() ?? gameObject.AddComponent<TSkill>();

            if (skill != null)
            {
                configureSkill?.Invoke(skill);
            }
            else if (CompanionManager.EnableDebugLogging)
            {
                Debug.LogError($"[{debugLabel}] Failed to resolve {typeof(TSkill).Name} component.", this);
            }

            InitialiseMovementComponents();
            ResetFollowerState();
            ResetStuckHistoryInternal();

            areaRoutineActive = false;
            followerDisabledForGathering = false;
            areaAllCandidatesBlocked = false;
            activeAreaRadius = 0f;

            skillCooldownTracker = cooldownTracker;

            resetSkillSpecificFlags?.Invoke();

            return skill;
        }

        /// <summary>
        /// Executes the supplied command and treats inventory-full outcomes as success so the caller can
        /// surface the appropriate chat messaging without forcing the user to retry.
        /// </summary>
        protected bool TryCommandAllowingInventoryFull(TNode node)
        {
            return TryCommandAllowingInventoryFull(node, false);
        }

        /// <summary>
        /// Executes the supplied command while optionally preserving follower holds.
        /// </summary>
        protected bool TryCommandAllowingInventoryFull(TNode node, bool preserveFollowerHold)
        {
            var attempt = PerformGatheringCommand(node, preserveFollowerHold);
            return attempt.Accepted || ShouldTreatInventoryFullAsSuccess(attempt.Result);
        }

        /// <summary>
        /// Executes the supplied command and returns the detailed outcome to the caller.
        /// </summary>
        protected bool TryCommandWithResult(TNode node, out TCommandResult result)
        {
            return TryCommandWithResult(node, out result, false);
        }

        /// <summary>
        /// Executes the supplied command, preserving follower holds when requested, and returns the detailed outcome.
        /// </summary>
        protected bool TryCommandWithResult(TNode node, out TCommandResult result, bool preserveFollowerHold)
        {
            var attempt = PerformGatheringCommand(node, preserveFollowerHold);
            result = attempt.Result;
            return attempt.Accepted;
        }

        /// <summary>
        /// Cancels the active area routine, clears cached candidates, and optionally restores the follower state.
        /// </summary>
        protected void CancelAreaInternal(bool restoreFollower, bool preserveFollowerLocks = false, Action onCancelExisting = null)
        {
            if (areaRoutine != null)
            {
                StopCoroutine(areaRoutine);
                areaRoutine = null;
            }

            areaRoutineActive = false;
            activeAreaRadius = 0f;
            areaAllCandidatesBlocked = false;
            areaCandidates.Clear();
            areaCandidateTileCenters.Clear();

            onCancelExisting?.Invoke();

            if (restoreFollower)
                CleanupFollowerAfterGathering(true, preserveFollowerLocks);
        }

        /// <summary>
        /// Starts a new area-gathering routine once prerequisites have been validated.
        /// </summary>
        protected bool TryStartAreaGathering(
            float radius,
            out TCommandResult failureReason,
            TCommandResult acceptedResult,
            Func<float, (bool success, TCommandResult failureReason)> candidateBuilder,
            Action<TCommandResult> onFailure,
            Func<IEnumerator> routineFactory,
            Action onStarted,
            string debugLabel,
            Action onCancelExisting = null,
            bool preserveFollowerLocks = true)
        {
            failureReason = acceptedResult;

            float clampedRadius = Mathf.Max(0.1f, radius);

            CancelAreaInternal(true, preserveFollowerLocks, onCancelExisting);

            var buildOutcome = candidateBuilder(clampedRadius);
            if (!buildOutcome.success)
            {
                failureReason = buildOutcome.failureReason;
                onFailure?.Invoke(failureReason);
                return false;
            }

            failureReason = acceptedResult;

            activeAreaRadius = clampedRadius;
            areaRoutine = StartCoroutine(routineFactory());
            areaRoutineActive = true;

            onStarted?.Invoke();

            if (CompanionManager.EnableDebugLogging && !string.IsNullOrEmpty(debugLabel))
            {
                Debug.Log($"[{debugLabel}] Area routine started with {areaCandidates.Count} candidates (radius {activeAreaRadius}).", this);
            }

            return true;
        }

        /// <summary>
        /// Builds the cached area candidate list using the supplied delegates so derived controllers can
        /// share the standard radius, blocking, and validation flow while still injecting skill-specific
        /// rules. The helper clears the shared buffers, prunes expired block entries, applies distance
        /// checks, removes temporarily blocked nodes, sorts the surviving candidates by proximity, and
        /// caches their tile centres for gizmo rendering.
        /// </summary>
        /// <param name="radius">Scan radius used to qualify nearby nodes.</param>
        /// <param name="retrieveNodes">Delegate that returns the full set of potential gathering nodes.</param>
        /// <param name="shouldSkipNode">
        /// Optional predicate used for skill-specific skip logic (busy spots, protected nodes). Return
        /// <c>true</c> to skip validation for the supplied node.
        /// </param>
        /// <param name="tryPrepareCommand">
        /// Delegate that executes the per-node validation command and returns its outcome.
        /// </param>
        /// <param name="acceptedResultFactory">Factory that returns the enum value representing a successful command.</param>
        /// <param name="defaultFailureResultFactory">
        /// Factory that returns the enum value used when no specific failure was observed.
        /// </param>
        /// <param name="isInventoryFullResult">
        /// Predicate that identifies the enum value signalling an inventory full state.
        /// </param>
        /// <param name="isAcceptedResult">Optional predicate that determines whether a validation result should be treated as accepted.</param>
        /// <param name="onCandidateAccepted">Optional callback invoked whenever a node survives validation.</param>
        /// <returns>
        /// Tuple describing whether at least one candidate was discovered and, when not, the most relevant
        /// failure reason observed during the scan.
        /// </returns>
        protected (bool success, TCommandResult failureReason) BuildAreaCandidates(
            float radius,
            Func<TNode[]> retrieveNodes,
            Func<TNode, bool> shouldSkipNode,
            Func<TNode, (bool accepted, TCommandResult validationResult)> tryPrepareCommand,
            Func<TCommandResult> acceptedResultFactory,
            Func<TCommandResult> defaultFailureResultFactory,
            Func<TCommandResult, bool> isInventoryFullResult,
            Func<TCommandResult, bool> isAcceptedResult = null,
            Action<TNode> onCandidateAccepted = null)
        {
            if (retrieveNodes == null)
                throw new ArgumentNullException(nameof(retrieveNodes));
            if (tryPrepareCommand == null)
                throw new ArgumentNullException(nameof(tryPrepareCommand));
            if (acceptedResultFactory == null)
                throw new ArgumentNullException(nameof(acceptedResultFactory));
            if (defaultFailureResultFactory == null)
                throw new ArgumentNullException(nameof(defaultFailureResultFactory));
            if (isInventoryFullResult == null)
                throw new ArgumentNullException(nameof(isInventoryFullResult));

            areaCandidates.Clear();
            areaCandidateTileCenters.Clear();
            areaAllCandidatesBlocked = false;

            var nodes = retrieveNodes();
            var acceptedResult = acceptedResultFactory();
            var defaultFailure = defaultFailureResultFactory();
            var acceptsResultPredicate = isAcceptedResult ??
                (result => EqualityComparer<TCommandResult>.Default.Equals(result, acceptedResult));

            if (nodes == null || nodes.Length == 0)
                return (false, defaultFailure);

            float radiusSqr = radius * radius;
            Vector2 controllerPosition2D = (Vector2)transform.position;
            bool observedNonInventoryFailure = false;
            TCommandResult lastNonInventoryFailure = defaultFailure;
            int blockedByStuckCount = 0;

            float now = Time.time;
            PruneExpiredBlockedNodes();

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null || IsNodeDepleted(node))
                    continue;

                Vector2 nodePosition2D = (Vector2)node.transform.position;
                if ((nodePosition2D - controllerPosition2D).sqrMagnitude > radiusSqr)
                    continue;

                if (shouldSkipNode != null && shouldSkipNode(node))
                    continue;

                if (IsNodeTemporarilyBlocked(node, now))
                {
                    blockedByStuckCount++;
                    continue;
                }

                var attempt = tryPrepareCommand(node);
                if (!attempt.accepted)
                {
                    var validationResult = attempt.validationResult;
                    if (isInventoryFullResult(validationResult))
                        return (false, validationResult);

                    if (!acceptsResultPredicate(validationResult))
                    {
                        observedNonInventoryFailure = true;
                        lastNonInventoryFailure = validationResult;
                    }

                    continue;
                }

                areaCandidates.Add(node);
                onCandidateAccepted?.Invoke(node);
            }

            areaCandidates.Sort((a, b) =>
            {
                if (a == null && b == null)
                    return 0;
                if (a == null)
                    return 1;
                if (b == null)
                    return -1;

                Vector2 aPosition2D = (Vector2)a.transform.position;
                Vector2 bPosition2D = (Vector2)b.transform.position;
                float da = (aPosition2D - controllerPosition2D).sqrMagnitude;
                float db = (bPosition2D - controllerPosition2D).sqrMagnitude;
                return da.CompareTo(db);
            });

            for (int i = 0; i < areaCandidates.Count; i++)
            {
                var candidate = areaCandidates[i];
                if (candidate == null)
                    continue;

                areaCandidateTileCenters.Add(GetTileCentre(candidate.transform.position));
            }

            if (areaCandidates.Count == 0)
            {
                if (blockedByStuckCount > 0 && !observedNonInventoryFailure)
                    areaAllCandidatesBlocked = true;

                var failure = observedNonInventoryFailure ? lastNonInventoryFailure : defaultFailure;
                return (false, failure);
            }

            return (true, acceptedResult);
        }

        /// <summary>
        /// Converts a world position to the centre of the tile it belongs to. Used for gizmo rendering.
        /// </summary>
        protected Vector3 GetTileCentre(Vector3 worldPosition)
        {
            float x = Mathf.Round(worldPosition.x);
            float y = Mathf.Round(worldPosition.y);
            return new Vector3(x, y, worldPosition.z);
        }

        /// <summary>
        /// Releases or restores the follower component depending on the supplied flags.
        /// </summary>
        protected void CleanupFollowerAfterGathering(bool restoreFollower, bool preserveFollowerLocks)
        {
            if (restoreFollower)
            {
                if (preserveFollowerLocks)
                {
                    followerDisabledForGathering = HasActiveFollowerHold;

                    if (!HasActiveFollowerHold && followerHoldToggledFollower && petFollower != null && !petFollower.enabled)
                    {
                        petFollower.enabled = true;
                        followerHoldToggledFollower = false;
                    }
                }
                else
                {
                    ForceReleaseAllFollowerHoldsInternal();
                }
            }
            else if (!preserveFollowerLocks)
            {
                followerDisableLockCount = 0;
                followerDisabledForGathering = false;
                followerHoldToggledFollower = false;
            }
            else
            {
                followerDisabledForGathering = HasActiveFollowerHold;
            }
        }

        /// <summary>
        /// Handles the standard disable-time cleanup shared across the gathering controllers. Invokes
        /// the supplied delegates before clearing the cached node state so derived classes only need
        /// to forward their cancellation and unsubscribe routines.
        /// </summary>
        /// <param name="cancel">Routine that cancels the active gathering operation.</param>
        /// <param name="unsubscribe">Delegate used to release external subscriptions (player skill hooks).</param>
        /// <param name="additionalCleanup">Optional callback for skill-specific teardown logic.</param>
        protected void HandleDisable(Action cancel, Action unsubscribe, Action additionalCleanup = null)
        {
            cancel?.Invoke();
            unsubscribe?.Invoke();
            additionalCleanup?.Invoke();

            blockedNodes.Clear();
            blockedNodePruneBuffer.Clear();
            areaAllCandidatesBlocked = false;
            ResetStuckHistoryInternal();
        }

        /// <summary>
        /// Performs destruction-time cleanup for gathering controllers. Ensures skill-specific
        /// subscriptions are released before running the shared disable flow, guaranteeing that
        /// cancellation cannot re-trigger callbacks on disposed listeners.
        /// </summary>
        /// <param name="cancel">Routine that cancels the active gathering operation.</param>
        /// <param name="unsubscribe">Delegate used to release external subscriptions (player skill hooks).</param>
        /// <param name="removeSkillCallback">Optional action that removes skill event handlers.</param>
        /// <param name="additionalCleanup">Optional callback for skill-specific teardown logic.</param>
        protected void HandleDestroy(
            Action cancel,
            Action unsubscribe,
            Action removeSkillCallback = null,
            Action additionalCleanup = null)
        {
            removeSkillCallback?.Invoke();
            HandleDisable(cancel, unsubscribe, additionalCleanup);
        }

        /// <summary>
        /// Clears the cached stuck history so future attempts treat the next stall as the first occurrence.
        /// </summary>
        protected void ResetStuckHistoryInternal()
        {
            lastStuckNode = null;
            consecutiveStuckNodeCount = 0;
        }

        /// <summary>
        /// Encapsulates the shared parameters required to recover from a stuck gathering sequence.
        /// </summary>
        protected struct GatheringStuckRecoveryParameters
        {
            /// <summary>The node that triggered the stuck handler.</summary>
            public TNode Node;

            /// <summary>Label appended to debug output when logging is enabled.</summary>
            public string DebugLabel;

            /// <summary>
            /// Optional delegate that returns a debug string describing the stuck state. When omitted, a
            /// standard message using <see cref="DebugLabel"/> and the node name is emitted.
            /// </summary>
            public Func<TNode, string> BuildDebugMessage;

            /// <summary>
            /// Predicate that determines whether the active skill should be stopped. When null the skill is
            /// always stopped if a stop delegate is provided.
            /// </summary>
            public Func<bool> ShouldStopSkill;

            /// <summary>
            /// Invoked with <c>true</c> before stopping the skill and <c>false</c> afterwards to toggle
            /// suppression flags in derived controllers. Optional.
            /// </summary>
            public Action<bool> SetStopCallbackSuppressed;

            /// <summary>Delegate that stops the active gathering skill.</summary>
            public Action StopSkill;

            /// <summary>Skill-specific cleanup callback executed after the skill is stopped.</summary>
            public Action CleanupCallback;

            /// <summary>
            /// Additional reset invoked after cleanup so derived controllers can clear routine state.
            /// </summary>
            public Action AdditionalStateReset;

            /// <summary>
            /// Invoked when the consecutive stuck threshold is reached. Receives the node and the updated
            /// consecutive count.
            /// </summary>
            public Action<TNode, int> OnThresholdReached;
        }

        /// <summary>
        /// Executes the shared stuck recovery sequence used by the individual gathering controllers.
        /// </summary>
        /// <param name="parameters">Parameter bundle describing the recovery behaviour.</param>
        protected void ExecuteGatheringStuckRecovery(GatheringStuckRecoveryParameters parameters)
        {
            TNode node = parameters.Node;

            if (CompanionManager.EnableDebugLogging)
            {
                string message = null;

                if (parameters.BuildDebugMessage != null)
                {
                    message = parameters.BuildDebugMessage(node);
                }
                else
                {
                    string label = string.IsNullOrEmpty(parameters.DebugLabel) ? "Companion" : parameters.DebugLabel;
                    string nodeName = node != null ? node.name : "<null>";
                    message = $"[{label}] Detected a stuck state while targeting {nodeName}.";
                }

                if (!string.IsNullOrEmpty(message))
                    Debug.Log(message, this);
            }

            if (node != null)
            {
                float now = Time.time;
                MarkNodeBlocked(node, now + stuckTimeoutSeconds);
            }

            bool shouldStopSkill = parameters.ShouldStopSkill == null || parameters.ShouldStopSkill();

            if (parameters.StopSkill != null && shouldStopSkill)
            {
                bool suppressionApplied = false;

                if (parameters.SetStopCallbackSuppressed != null)
                {
                    parameters.SetStopCallbackSuppressed(true);
                    suppressionApplied = true;
                }

                try
                {
                    parameters.StopSkill();
                }
                finally
                {
                    if (suppressionApplied)
                        parameters.SetStopCallbackSuppressed(false);
                }
            }

            parameters.CleanupCallback?.Invoke();
            parameters.AdditionalStateReset?.Invoke();

            pathMover?.ResetFollowTracking();

            if (petFollower != null)
            {
                Transform followerPlayer = petFollower.Player;
                if (followerPlayer != null)
                    petFollower.SetPlayer(followerPlayer);
            }

            if (node != null)
            {
                if (node == lastStuckNode)
                {
                    consecutiveStuckNodeCount++;
                }
                else
                {
                    lastStuckNode = node;
                    consecutiveStuckNodeCount = 1;
                }
            }
            else
            {
                lastStuckNode = null;
                consecutiveStuckNodeCount = 0;
            }

            if (consecutiveStuckNodeCount >= ConsecutiveStuckCancelThreshold)
                parameters.OnThresholdReached?.Invoke(node, consecutiveStuckNodeCount);
        }

        /// <summary>
        /// Removes any blocked nodes whose expiry has passed or whose node depleted while on cooldown.
        /// </summary>
        protected void PruneExpiredBlockedNodes()
        {
            float now = Time.time;
            blockedNodePruneBuffer.Clear();

            foreach (var kvp in blockedNodes)
            {
                var node = kvp.Key;
                bool expired = node == null || kvp.Value <= now || IsNodeDepleted(node);
                if (expired)
                    blockedNodePruneBuffer.Add(node);
            }

            for (int i = 0; i < blockedNodePruneBuffer.Count; i++)
            {
                blockedNodes.Remove(blockedNodePruneBuffer[i]);
            }

            blockedNodePruneBuffer.Clear();
        }

        /// <summary>
        /// Returns whether the supplied node is temporarily blocked by the stuck handler.
        /// </summary>
        protected bool IsNodeTemporarilyBlocked(TNode node, float now)
        {
            if (node == null)
                return false;

            if (!blockedNodes.TryGetValue(node, out float expiry))
                return false;

            if (expiry <= now || IsNodeDepleted(node))
            {
                blockedNodes.Remove(node);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Records that a node should be skipped until the supplied expiry time.
        /// </summary>
        protected void MarkNodeBlocked(TNode node, float expiry)
        {
            if (node == null)
                return;

            blockedNodes[node] = expiry;
        }

        /// <summary>
        /// Removes the candidate at the provided index from both cached area lists.
        /// </summary>
        protected void RemoveAreaCandidateAt(int index)
        {
            if (index < 0 || index >= areaCandidates.Count)
                return;

            areaCandidates.RemoveAt(index);
            if (index < areaCandidateTileCenters.Count)
                areaCandidateTileCenters.RemoveAt(index);
        }

        /// <summary>
        /// Derived classes must implement the core command handling so the base class can manage overload logic.
        /// </summary>
        protected abstract CommandAttempt PerformGatheringCommand(TNode node, bool preserveFollowerHold);

        /// <summary>
        /// Determines whether a specific command result should be treated as a successful attempt when
        /// resolving the public overloads that return a simple boolean.
        /// </summary>
        protected abstract bool ShouldTreatInventoryFullAsSuccess(TCommandResult result);

        /// <summary>
        /// Determines whether the provided node is depleted. Implemented per skill.
        /// </summary>
        protected abstract bool IsNodeDepleted(TNode node);

        private void ReleaseTemporaryFollowerHoldInternal()
        {
            if (followerDisableLockCount <= 0)
            {
                followerDisableLockCount = 0;
                followerDisabledForGathering = false;
                followerHoldToggledFollower = false;
                return;
            }

            followerDisableLockCount = Mathf.Max(0, followerDisableLockCount - 1);
            followerDisabledForGathering = followerDisableLockCount > 0;

            if (!HasActiveFollowerHold)
            {
                if (followerHoldToggledFollower && petFollower != null && !petFollower.enabled)
                    petFollower.enabled = true;

                followerHoldToggledFollower = false;
            }
        }

        private void ForceReleaseAllFollowerHoldsInternal()
        {
            if (followerDisableLockCount <= 0)
            {
                followerDisableLockCount = 0;
                followerDisabledForGathering = false;
                followerHoldToggledFollower = false;
                return;
            }

            followerDisableLockCount = 0;
            followerDisabledForGathering = false;

            if (followerHoldToggledFollower && petFollower != null && !petFollower.enabled)
                petFollower.enabled = true;

            followerHoldToggledFollower = false;
        }
    }
}
