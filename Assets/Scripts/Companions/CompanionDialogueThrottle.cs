using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Provides a reusable throttle for companion dialogue so flavour lines share
    /// a consistent cooldown window regardless of which system triggered them.
    /// </summary>
    public static class CompanionDialogueThrottle
    {
        /// <summary>Default delay applied between repeated chatter entries.</summary>
        public const float DefaultDelaySeconds = 3.5f;

        /// <summary>Maps throttle keys to the next unscaled time a line may be emitted.</summary>
        private static readonly Dictionary<string, float> NextAllowedTimes =
            new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>
        /// Attempts to consume the supplied throttle key. Returns <c>true</c> when the
        /// caller is allowed to proceed and records the next time the key may succeed.
        /// </summary>
        /// <param name="key">Unique key describing the chatter event.</param>
        /// <param name="delaySeconds">Cooldown applied between permitted emissions.</param>
        public static bool TryConsume(string key, float delaySeconds)
        {
            if (string.IsNullOrWhiteSpace(key))
                return true;

            float now = Time.unscaledTime;
            if (NextAllowedTimes.TryGetValue(key, out float nextAllowed) && now < nextAllowed)
                return false;

            float clampedDelay = Mathf.Max(0f, delaySeconds);
            NextAllowedTimes[key] = clampedDelay <= 0f ? now : now + clampedDelay;
            return true;
        }
    }
}
