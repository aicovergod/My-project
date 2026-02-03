using System.Collections;
using UnityEngine;
using NPC;
using Skills.Thieving.Core;
using Skills.Thieving.Data;
using Util;

namespace Skills.Thieving
{
    /// <summary>
    ///     Component attached to NPCs that can be pickpocketed. Tracks the assigned definition, manages lockout state and
    ///     coordinates temporary disabling of the pickpocket context menu entry when the NPC is stunned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcThievingTarget : MonoBehaviour
    {
        [Header("Definition")]
        [SerializeField, Tooltip("Primary definition describing the pickpocket behaviour.")]
        private ThievingNpcDefinition definition;

        [SerializeField, Tooltip("Optional definition override used to tweak the base definition per-NPC.")]
        private ThievingNpcDefinition definitionOverride;

        [Header("Dialogue")]
        [SerializeField, Tooltip("Optional transform used as the anchor for floating dialogue popups.")]
        private Transform dialogueAnchor;

        [Header("State")]
        [SerializeField, Tooltip("Runtime counter tracking consecutive failures so the lockout trigger can be enforced.")]
        private int consecutiveFailures;

        [SerializeField, Tooltip("Timestamp (Time.time) when the NPC becomes available again after a lockout.")]
        private float lockoutEndTime;

        [SerializeField, Tooltip("True while the player is mid pickpocket attempt.")]
        private bool isBusy;

        [Header("Counter Attack")]
        [SerializeField, Tooltip("Optional facing component used to rotate towards the player when countering failed pickpockets.")]
        private NpcFacing npcFacing;

        [SerializeField, Tooltip("Optional sprite animator override used to drive counter-attack animations when no facing component is present.")]
        private NpcSpriteAnimator spriteAnimator;

        private Coroutine lockoutCoroutine;
        private Coroutine counterAttackRoutine;
        private NpcInteractionOptions interactionOptions;

        /// <summary>
        ///     True when the NPC can be pickpocketed right now. Considers lockouts, busy state and definition availability.
        /// </summary>
        public bool CanPickpocket => Definition != null && !isBusy && Time.time >= lockoutEndTime;

        /// <summary>
        ///     Definition resolved for this NPC (override falls back to the base definition).
        /// </summary>
        public ThievingNpcDefinition Definition => definitionOverride != null ? definitionOverride : definition;

        /// <summary>
        ///     Transform used as the anchor when displaying pickpocket dialogue above the NPC.
        /// </summary>
        public Transform DialogueAnchor => dialogueAnchor != null ? dialogueAnchor : transform;

        private void Awake()
        {
            if (interactionOptions == null)
                interactionOptions = GetComponent<NpcInteractionOptions>();

            EnsureAnimationReferences();
        }

        private void OnValidate()
        {
            if (interactionOptions == null)
                interactionOptions = GetComponent<NpcInteractionOptions>();

            if (interactionOptions != null && !interactionOptions.IsPickpocketEnabled)
                interactionOptions.SetPickpocketEnabled(true);

            if (Definition == null)
                Debug.LogWarning($"{name} is configured with {nameof(NpcThievingTarget)} but no definition is assigned.", this);

            EnsureAnimationReferences();
        }

        private void OnDisable()
        {
            if (counterAttackRoutine != null)
            {
                StopCoroutine(counterAttackRoutine);
                counterAttackRoutine = null;
            }
        }

        /// <summary>
        ///     Called by <see cref="ThievingSkill"/> when a pickpocket attempt begins. Marks the NPC as busy so other
        ///     interactions cannot start concurrently.
        /// </summary>
        public void NotifyAttemptStarted()
        {
            isBusy = true;
        }

        /// <summary>
        ///     Called by <see cref="ThievingSkill"/> when a pickpocket attempt completes.
        /// </summary>
        /// <param name="success">True when the pickpocket succeeded.</param>
        /// <param name="triggeredLockout">True when the attempt should trigger the definition's cooldown.</param>
        public void NotifyAttemptFinished(bool success, bool triggeredLockout)
        {
            isBusy = false;

            if (success)
            {
                consecutiveFailures = 0;
                if (ThievingSkill.GlobalDebugLogging)
                    Debug.Log($"[{name}] Pickpocket success. Consecutive failures reset.", this);
                return;
            }

            consecutiveFailures++;
            if (ThievingSkill.GlobalDebugLogging)
            {
                Debug.Log($"[{name}] Pickpocket failed. Consecutive failures: {consecutiveFailures}.", this);
            }

            if (!triggeredLockout)
                return;

            ThievingNpcDefinition resolved = Definition;
            if (resolved == null)
                return;

            float cooldownSeconds = resolved.CooldownTicks * Ticker.TickDuration;
            lockoutEndTime = Time.time + cooldownSeconds;
            if (interactionOptions != null)
                interactionOptions.SetPickpocketEnabled(false);

            if (lockoutCoroutine != null)
                StopCoroutine(lockoutCoroutine);
            lockoutCoroutine = StartCoroutine(RestorePickpocketAfterDelay(cooldownSeconds));

            if (ThievingSkill.GlobalDebugLogging)
            {
                Debug.Log($"[{name}] Pickpocket lockout triggered for {cooldownSeconds:F2}s.", this);
            }
        }

        private IEnumerator RestorePickpocketAfterDelay(float delaySeconds)
        {
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);

            if (interactionOptions != null)
                interactionOptions.SetPickpocketEnabled(true);

            lockoutCoroutine = null;
            consecutiveFailures = 0;

            if (ThievingSkill.GlobalDebugLogging)
            {
                Debug.Log($"[{name}] Pickpocket lockout expired.", this);
            }
        }

        /// <summary>
        ///     Resets the lockout timer immediately, typically used by tests.
        /// </summary>
        public void ForceClearLockout()
        {
            lockoutEndTime = 0f;
            consecutiveFailures = 0;
            if (lockoutCoroutine != null)
            {
                StopCoroutine(lockoutCoroutine);
                lockoutCoroutine = null;
            }

            if (interactionOptions != null)
                interactionOptions.SetPickpocketEnabled(true);
        }

        /// <summary>
        ///     Exposes the current consecutive failure counter for diagnostic tooling.
        /// </summary>
        public int ConsecutiveFailures => consecutiveFailures;

        /// <summary>
        ///     Indicates when the lockout expires (Time.time). Primarily surfaced for debugging and tests.
        /// </summary>
        public float LockoutEndTime => lockoutEndTime;

        /// <summary>
        ///     Faces the supplied player transform and plays an attack animation when available so the NPC visibly retaliates
        ///     after a failed pickpocket that deals damage.
        /// </summary>
        /// <param name="playerTransform">Transform describing the player's world position.</param>
        public void TriggerPickpocketCounterAttack(Transform playerTransform)
        {
            EnsureAnimationReferences();

            if (playerTransform != null)
            {
                if (npcFacing != null)
                {
                    npcFacing.FaceTarget(playerTransform);
                }
                else if (spriteAnimator != null)
                {
                    Vector2 direction = playerTransform.position - transform.position;
                    if (direction.sqrMagnitude > Mathf.Epsilon)
                    {
                        var facing = Direction8Utility.FromVector(direction, allowDiagonals: true, fallback: Direction8.Down);
                        spriteAnimator.SetFacing(facing);
                    }
                }
            }

            NpcSpriteAnimator animator = ResolveAnimator();
            if (animator == null)
                return;

            Direction8 attackDirection = npcFacing != null
                ? npcFacing.FacingDirection
                : ResolveFacingFromPlayer(playerTransform);

            if (!animator.HasAttackAnimation(attackDirection))
                return;

            if (counterAttackRoutine != null)
                StopCoroutine(counterAttackRoutine);

            counterAttackRoutine = StartCoroutine(PlayCounterAttack(animator, attackDirection));
        }

        private void EnsureAnimationReferences()
        {
            if (npcFacing == null)
                npcFacing = GetComponent<NpcFacing>() ?? GetComponentInChildren<NpcFacing>();

            if (spriteAnimator == null)
                spriteAnimator = GetComponent<NpcSpriteAnimator>() ?? GetComponentInChildren<NpcSpriteAnimator>();

            if (npcFacing != null && npcFacing.Animator != null)
                spriteAnimator = npcFacing.Animator;
        }

        private NpcSpriteAnimator ResolveAnimator()
        {
            if (npcFacing != null && npcFacing.Animator != null)
                return npcFacing.Animator;

            return spriteAnimator;
        }

        private Direction8 ResolveFacingFromPlayer(Transform playerTransform)
        {
            if (playerTransform == null)
                return npcFacing != null ? npcFacing.FacingDirection : Direction8.Down;

            Vector2 direction = playerTransform.position - transform.position;
            return Direction8Utility.FromVector(direction, allowDiagonals: true, fallback: Direction8.Down);
        }

        private IEnumerator PlayCounterAttack(NpcSpriteAnimator animator, Direction8 direction)
        {
            if (animator == null)
            {
                counterAttackRoutine = null;
                yield break;
            }

            yield return animator.PlayAttackAnimation(direction);
            counterAttackRoutine = null;
        }
    }
}
