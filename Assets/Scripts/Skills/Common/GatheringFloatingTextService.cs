using System.Collections;
using UI;
using UnityEngine;
using Util;
using World;

namespace Skills.Common
{
    /// <summary>
    ///     Persistent helper that centralises floating text behaviour for gathering skills.
    ///     Designers can tweak the interaction radius and XP popup delay from a single
    ///     location while gameplay code reuses the shared range validation helpers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GatheringFloatingTextService : MonoBehaviour
    {
        private const float DefaultRadius = 3f;
        private const float DefaultXpDelayTicks = 5f;

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
        }

        private void OnValidate()
        {
            feedbackRadius = Mathf.Max(0f, feedbackRadius);
            defaultXpPopupDelayTicks = Mathf.Max(0f, defaultXpPopupDelayTicks);
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

            FloatingText.Show(message, anchorPosition);
            return true;
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

            FloatingText.Show($"+{xp} XP", anchorPosition);
        }
    }
}
