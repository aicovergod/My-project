using System.Collections;
using System.Collections.Generic;
using UI;
using UnityEngine;
using Util;
using World;

namespace Skills.Common
{
    /// <summary>
    ///     Persistent helper that centralises floating text behaviour for gathering skills.
    ///     Designers can tweak the interaction radius, XP popup delay, and the shared
    ///     minimum popup interval from a single location while gameplay code reuses the
    ///     shared range validation helpers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GatheringFloatingTextService : MonoBehaviour
    {
        private const float DefaultRadius = 3f;
        private const float DefaultXpDelayTicks = 5f;
        private const float DefaultMinimumPopupIntervalTicks = 1f;

        /// <summary>
        ///     Provides direct access to the active singleton instance when available.
        /// </summary>
        public static GatheringFloatingTextService Instance =>
            PersistentSceneSingleton<GatheringFloatingTextService>.Instance;

        [Header("Feedback Settings")]
        [SerializeField]
        [Tooltip("Maximum distance between the player and the resource before floating text is suppressed.")]
        private float feedbackRadius = DefaultRadius;

        [SerializeField]
        [Tooltip("Fallback XP popup delay (in ticks) used when a caller does not supply an override.")]
        private float defaultXpPopupDelayTicks = DefaultXpDelayTicks;

        [Header("Popup Cooldown")]
        [SerializeField]
        [Tooltip("Minimum interval (in ticks) enforced between floating text popups for the same anchor."
                 + " OnValidate keeps the seconds field aligned so designers can author values in ticks if preferred.")]
        private float minimumPopupIntervalTicks = DefaultMinimumPopupIntervalTicks;

        [SerializeField]
        [Tooltip("Minimum interval (in seconds) enforced between floating text popups for the same anchor."
                 + " Adjusting this value also updates the tick configuration so teams can tune whichever unit is more intuitive.")]
        private float minimumPopupIntervalSeconds = DefaultMinimumPopupIntervalTicks * Ticker.TickDuration;

        [SerializeField]
        [Tooltip("Runtime tracker storing the last display time for each anchor so cooldowns remain consistent across skills.")]
        private AnchorCooldownDictionary anchorCooldowns = new AnchorCooldownDictionary();

        /// <summary>
        ///     Ensures the singleton is spawned before gameplay scenes begin loading.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            PersistentSceneSingleton<GatheringFloatingTextService>.Bootstrap(CreateSingleton);
        }

        private static GatheringFloatingTextService CreateSingleton()
        {
            var go = new GameObject(nameof(GatheringFloatingTextService));
            return go.AddComponent<GatheringFloatingTextService>();
        }

        private void Awake()
        {
            if (!PersistentSceneSingleton<GatheringFloatingTextService>.HandleAwake(this))
                return;
        }

        private void OnDestroy()
        {
            if (!PersistentSceneSingleton<GatheringFloatingTextService>.HandleOnDestroy(this))
                return;

            StopAllCoroutines();
            anchorCooldowns?.Clear();
        }

        private void OnValidate()
        {
            feedbackRadius = Mathf.Max(0f, feedbackRadius);
            defaultXpPopupDelayTicks = Mathf.Max(0f, defaultXpPopupDelayTicks);
            minimumPopupIntervalTicks = Mathf.Max(0f, minimumPopupIntervalTicks);
            minimumPopupIntervalSeconds = Mathf.Max(0f, minimumPopupIntervalSeconds);

            float tickSeconds = minimumPopupIntervalTicks * Ticker.TickDuration;
            if (minimumPopupIntervalSeconds < tickSeconds)
            {
                minimumPopupIntervalSeconds = tickSeconds;
            }
            else if (Ticker.TickDuration > Mathf.Epsilon)
            {
                minimumPopupIntervalTicks = minimumPopupIntervalSeconds / Ticker.TickDuration;
            }

            if (anchorCooldowns == null)
                anchorCooldowns = new AnchorCooldownDictionary();

            anchorCooldowns.PruneNullEntries();
        }

        /// <summary>
        ///     Attempts to display floating text immediately, enforcing the configured range check.
        /// </summary>
        /// <param name="message">Text to display.</param>
        /// <param name="playerAnchor">Transform used for placement and distance evaluation.</param>
        /// <param name="sourcePosition">World position of the resource that triggered the message.</param>
        /// <returns><c>true</c> when the popup was shown; otherwise <c>false</c>.</returns>
        public static bool TryShowNow(string message, Transform playerAnchor, Vector3 sourcePosition)
        {
            if (string.IsNullOrWhiteSpace(message) || playerAnchor == null)
                return false;

            var instance = RequireInstance();
            if (instance == null)
                return false;

            Vector3 anchorPosition = playerAnchor.position;
            if (!instance.IsWithinRange(anchorPosition, sourcePosition))
                return false;

            return instance.TryShowAtAnchorInternal(message, playerAnchor);
        }

        /// <summary>
        ///     Attempts to display floating text using the supplied anchor without performing a range validation.
        ///     Useful for helpers that only know about the player anchor but still want cooldown enforcement.
        /// </summary>
        /// <param name="message">Text to display.</param>
        /// <param name="anchor">Transform used for placement and cooldown tracking.</param>
        /// <returns><c>true</c> when the popup was shown; otherwise <c>false</c>.</returns>
        public static bool TryShowAtAnchor(string message, Transform anchor)
        {
            if (string.IsNullOrWhiteSpace(message) || anchor == null)
                return false;

            var instance = RequireInstance();
            if (instance == null)
                return false;

            return instance.TryShowAtAnchorInternal(message, anchor);
        }

        /// <summary>
        ///     Queues a delayed XP popup, using ticks to keep cadence aligned with the global ticker.
        /// </summary>
        /// <param name="xp">XP amount to show.</param>
        /// <param name="anchor">Transform used for popup placement.</param>
        /// <param name="sourcePosition">World position of the resource awarding XP.</param>
        /// <param name="delayTicks">Optional override for the popup delay (in ticks).</param>
        /// <returns><c>true</c> when the popup was scheduled; otherwise <c>false</c>.</returns>
        public static bool QueueDelayedXpPopup(int xp, Transform anchor, Vector3 sourcePosition, float delayTicks)
        {
            if (xp <= 0 || anchor == null)
                return false;

            var instance = RequireInstance();
            if (instance == null)
                return false;

            Vector3 anchorPosition = anchor.position;
            if (!instance.IsWithinRange(anchorPosition, sourcePosition))
                return false;

            float ticks = instance.ResolveDelayTicks(delayTicks);
            float delaySeconds = Mathf.Max(0f, ticks * Ticker.TickDuration);
            if (instance.ShouldThrottleAnchor(anchor, delaySeconds))
                return false;

            instance.StartCoroutine(instance.ShowXpPopupAfterDelayRoutine(xp, anchor, sourcePosition, ticks));
            return true;
        }

        private static GatheringFloatingTextService RequireInstance()
        {
            var instance = Instance;
            if (instance != null)
                return instance;

            PersistentSceneSingleton<GatheringFloatingTextService>.Bootstrap(CreateSingleton);
            return Instance;
        }

        private bool IsWithinRange(Vector3 anchorPosition, Vector3 sourcePosition)
        {
            return Vector3.Distance(anchorPosition, sourcePosition) <= GetRadius();
        }

        private float GetRadius()
        {
            return Mathf.Max(0f, feedbackRadius);
        }

        private float ResolveDelayTicks(float overrideTicks)
        {
            return overrideTicks > 0f ? overrideTicks : Mathf.Max(0f, defaultXpPopupDelayTicks);
        }

        private IEnumerator ShowXpPopupAfterDelayRoutine(int xp, Transform anchor, Vector3 sourcePosition, float delayTicks)
        {
            float clampedTicks = Mathf.Max(0f, delayTicks);
            float delaySeconds = clampedTicks * Ticker.TickDuration;
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);

            if (anchor == null)
                yield break;

            Vector3 anchorPosition = anchor.position;
            if (!IsWithinRange(anchorPosition, sourcePosition))
                yield break;

            if (!TryShowAtAnchorInternal($"+{xp} XP", anchor))
                yield break;
        }

        private bool TryShowAtAnchorInternal(string message, Transform anchor)
        {
            if (string.IsNullOrWhiteSpace(message) || anchor == null)
                return false;

            if (!TryConsumeAnchorCooldown(anchor))
                return false;

            FloatingText.Show(message, anchor.position);
            return true;
        }

        private bool TryConsumeAnchorCooldown(Transform anchor)
        {
            if (anchor == null)
                return false;

            if (anchorCooldowns == null)
                anchorCooldowns = new AnchorCooldownDictionary();

            anchorCooldowns.PruneNullEntries();

            float cooldownSeconds = GetMinimumPopupIntervalSeconds();
            float now = Time.unscaledTime;
            if (cooldownSeconds > 0f &&
                anchorCooldowns.TryGetLastShown(anchor, out float lastShown) &&
                now - lastShown < cooldownSeconds)
            {
                return false;
            }

            anchorCooldowns.SetLastShown(anchor, now);
            return true;
        }

        private bool ShouldThrottleAnchor(Transform anchor, float plannedDelaySeconds)
        {
            if (anchor == null)
                return true;

            if (anchorCooldowns == null)
                anchorCooldowns = new AnchorCooldownDictionary();

            anchorCooldowns.PruneNullEntries();

            float cooldownSeconds = GetMinimumPopupIntervalSeconds();
            if (cooldownSeconds <= 0f)
                return false;

            if (!anchorCooldowns.TryGetLastShown(anchor, out float lastShown))
                return false;

            float now = Time.unscaledTime;
            float checkTime = now + Mathf.Max(0f, plannedDelaySeconds);
            return checkTime - lastShown < cooldownSeconds;
        }

        private float GetMinimumPopupIntervalSeconds()
        {
            float fromTicks = Mathf.Max(0f, minimumPopupIntervalTicks) * Ticker.TickDuration;
            return Mathf.Max(Mathf.Max(0f, minimumPopupIntervalSeconds), fromTicks);
        }

        [System.Serializable]
        private sealed class AnchorCooldownDictionary : ISerializationCallbackReceiver
        {
            [SerializeField]
            private List<Transform> anchors = new List<Transform>();

            [SerializeField]
            private List<float> timestamps = new List<float>();

            private readonly Dictionary<Transform, float> runtimeMap = new Dictionary<Transform, float>();
            private readonly List<Transform> pruneBuffer = new List<Transform>();

            public bool TryGetLastShown(Transform anchor, out float timestamp)
            {
                if (anchor == null)
                {
                    timestamp = 0f;
                    return false;
                }

                return runtimeMap.TryGetValue(anchor, out timestamp);
            }

            public void SetLastShown(Transform anchor, float timestamp)
            {
                if (anchor == null)
                    return;

                runtimeMap[anchor] = timestamp;
            }

            public void PruneNullEntries()
            {
                pruneBuffer.Clear();
                foreach (var pair in runtimeMap)
                {
                    if (pair.Key == null)
                        pruneBuffer.Add(pair.Key);
                }

                for (int i = 0; i < pruneBuffer.Count; i++)
                {
                    runtimeMap.Remove(pruneBuffer[i]);
                }

                pruneBuffer.Clear();
            }

            public void Clear()
            {
                runtimeMap.Clear();
                anchors.Clear();
                timestamps.Clear();
            }

            public void OnBeforeSerialize()
            {
                anchors.Clear();
                timestamps.Clear();

                foreach (var pair in runtimeMap)
                {
                    anchors.Add(pair.Key);
                    timestamps.Add(pair.Value);
                }
            }

            public void OnAfterDeserialize()
            {
                runtimeMap.Clear();

                int count = Mathf.Min(anchors.Count, timestamps.Count);
                for (int i = 0; i < count; i++)
                {
                    Transform anchor = anchors[i];
                    if (anchor == null)
                        continue;

                    runtimeMap[anchor] = timestamps[i];
                }
            }
        }
    }
}
