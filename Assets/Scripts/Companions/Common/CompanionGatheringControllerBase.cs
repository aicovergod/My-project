using System;
using System.Collections;
using System.Collections.Generic;
using Companions.Equipment;
using Skills;
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

        /// <summary>Squared magnitude below which facing adjustments are ignored to prevent jitter.</summary>
        protected virtual float FacingDeadzoneSqrMagnitude => 0.0001f;

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
        /// Applies the supplied movement step to the rigidbody (when present) and keeps the companion visuals in sync
        /// with the last displacement so sprite facing and idle orientation remain consistent across gathering skills.
        /// </summary>
        /// <param name="nextPosition">Target world position for the current step.</param>
        /// <param name="velocity">Velocity that should be assigned to the rigidbody when advancing toward the step.</param>
        /// <param name="teleported">When true, the rigidbody is repositioned without velocity to avoid interpolation spikes.</param>
        protected void ApplyMovement(Vector3 nextPosition, Vector2 velocity, bool teleported)
        {
            Vector3 currentPosition = body != null ? (Vector3)body.position : transform.position;
            Vector2 displacement = (Vector2)(nextPosition - currentPosition);
            Vector2 appliedVelocity = teleported ? Vector2.zero : velocity;

            if (body != null)
            {
                if (teleported)
                {
                    body.position = nextPosition;
                    body.linearVelocity = Vector2.zero;
                }
                else
                {
                    body.MovePosition(nextPosition);
                    body.linearVelocity = appliedVelocity;
                }
            }
            else
            {
                transform.position = nextPosition;
            }

            UpdateMovementVisuals(displacement, appliedVelocity);
        }

        /// <summary>
        /// Updates the sprite orientation and animation vector so the companion continues to face its movement direction,
        /// even when the step length is extremely small (common when approaching a node).
        /// </summary>
        /// <param name="displacement">Raw displacement applied during this step.</param>
        /// <param name="appliedVelocity">Velocity forwarded to the rigidbody when advancing toward the step.</param>
        protected void UpdateMovementVisuals(Vector2 displacement, Vector2 appliedVelocity)
        {
            Vector2 visualVector = displacement.sqrMagnitude > FacingDeadzoneSqrMagnitude
                ? displacement
                : appliedVelocity;

            if (visualVector.sqrMagnitude > FacingDeadzoneSqrMagnitude)
                lastFacing = Direction8Utility.FromVector(visualVector, allowDiagonals: true, fallback: lastFacing);

            if (petSpriteAnimator != null)
            {
                petSpriteAnimator.UpdateVisuals(visualVector);
                return;
            }

            if (fallbackSpriteRenderer == null)
                return;

            fallbackSpriteRenderer.flipX = Direction8Utility.IsFacingLeft(lastFacing);
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
        /// Clears the cached stuck history so future attempts treat the next stall as the first occurrence.
        /// </summary>
        protected void ResetStuckHistoryInternal()
        {
            lastStuckNode = null;
            consecutiveStuckNodeCount = 0;
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
