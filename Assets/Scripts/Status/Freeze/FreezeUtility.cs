using UnityEngine;
using Util;

namespace Status.Freeze
{
    /// <summary>
    /// Helper utilities for creating and applying the frozen status effect. The utility ensures
    /// consistent buff configuration and centralises the logic used by combat, debug tools and
    /// scripted sequences when forcing a freeze.
    /// </summary>
    public static class FreezeUtility
    {
        /// <summary>Display name presented on the buff HUD for frozen timers.</summary>
        public const string DefaultBuffDisplayName = "Frozen";

        /// <summary>Identifier loaded from Resources/ui/frozen for the buff icon.</summary>
        public const string DefaultBuffIconId = "frozen";

        /// <summary>
        /// Applies a freeze for the supplied duration measured in OSRS ticks (0.6 seconds each).
        /// </summary>
        public static void ApplyFreezeTicks(GameObject target, int durationTicks, Status.BuffSourceType sourceType, string sourceId = null, bool resetTimer = true)
        {
            if (target == null || durationTicks <= 0)
                return;

            float durationSeconds = Mathf.Max(1, durationTicks) * Ticker.TickDuration;
            ApplyFreezeSeconds(target, durationSeconds, sourceType, sourceId, resetTimer);
        }

        /// <summary>
        /// Applies a freeze for the supplied duration in seconds, constructing the buff definition
        /// and raising the appropriate timer event.
        /// </summary>
        public static void ApplyFreezeSeconds(GameObject target, float durationSeconds, Status.BuffSourceType sourceType, string sourceId = null, bool resetTimer = true)
        {
            if (target == null || durationSeconds <= 0f)
                return;

            var controller = target.GetComponent<FrozenStatusController>();
            if (controller == null)
            {
                Debug.LogWarning($"FreezeUtility could not find a FrozenStatusController on '{target.name}'. The freeze buff will still be applied so it persists for saving, but movement will not pause until the component is added.", target);
            }

            var definition = BuildFreezeBuffDefinition(durationSeconds);
            var context = new Status.BuffEventContext
            {
                target = target,
                definition = definition,
                sourceType = sourceType,
                sourceId = string.IsNullOrEmpty(sourceId) ? target.name : sourceId
            };

            if (resetTimer)
                Status.BuffEvents.RaiseBuffApplied(context);
            else
                Status.BuffEvents.RaiseBuffRefreshed(context);
        }

        /// <summary>
        /// Builds a freeze buff timer definition using the shared naming/icon conventions.
        /// </summary>
        public static Status.BuffTimerDefinition BuildFreezeBuffDefinition(float durationSeconds)
        {
            float clampedDuration = Mathf.Max(durationSeconds, Ticker.TickDuration);
            return new Status.BuffTimerDefinition
            {
                type = Status.BuffType.Freeze,
                displayName = DefaultBuffDisplayName,
                iconId = DefaultBuffIconId,
                durationSeconds = clampedDuration,
                recurringIntervalSeconds = 0f,
                isRecurring = false,
                showExpiryWarning = false,
                expiryWarningTicks = 0
            };
        }
    }
}
