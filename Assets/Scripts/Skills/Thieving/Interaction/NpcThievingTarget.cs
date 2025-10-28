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

        [Header("State")]
        [SerializeField, Tooltip("Runtime counter tracking consecutive failures so the lockout trigger can be enforced.")]
        private int consecutiveFailures;

        [SerializeField, Tooltip("Timestamp (Time.time) when the NPC becomes available again after a lockout.")]
        private float lockoutEndTime;

        [SerializeField, Tooltip("True while the player is mid pickpocket attempt.")]
        private bool isBusy;

        private Coroutine lockoutCoroutine;
        private NpcInteractionOptions interactionOptions;

        /// <summary>
        ///     True when the NPC can be pickpocketed right now. Considers lockouts, busy state and definition availability.
        /// </summary>
        public bool CanPickpocket => Definition != null && !isBusy && Time.time >= lockoutEndTime;

        /// <summary>
        ///     Definition resolved for this NPC (override falls back to the base definition).
        /// </summary>
        public ThievingNpcDefinition Definition => definitionOverride != null ? definitionOverride : definition;

        private void Awake()
        {
            if (interactionOptions == null)
                interactionOptions = GetComponent<NpcInteractionOptions>();
        }

        private void OnValidate()
        {
            if (interactionOptions == null)
                interactionOptions = GetComponent<NpcInteractionOptions>();

            if (interactionOptions != null && !interactionOptions.IsPickpocketEnabled)
                interactionOptions.SetPickpocketEnabled(true);

            if (Definition == null)
                Debug.LogWarning($"{name} is configured with {nameof(NpcThievingTarget)} but no definition is assigned.", this);
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
    }
}
