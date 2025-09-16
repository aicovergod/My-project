using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Object pool responsible for creating, warming and reusing <see cref="FloatingText"/> instances.
    /// The pool avoids costly GameObject instantiation and allows tuning through inspector-exposed settings.
    /// </summary>
    [DisallowMultipleComponent]
    public class FloatingTextPool : MonoBehaviour
    {
        /// <summary>
        /// Global accessor so <see cref="FloatingText.Show(string, Vector3, Color?, float?, Sprite)"/> can locate the pool.
        /// </summary>
        public static FloatingTextPool Instance { get; private set; }

        [Header("Pooling Settings")]
        [SerializeField, Tooltip("Number of floating text instances created on Awake for immediate reuse.")]
        private int initialPoolSize = 10;

        [SerializeField, Tooltip("Absolute cap on the total number of pooled floating text objects.")]
        private int maxPoolSize = 50;

        [SerializeField, Tooltip("How many new instances to create whenever the pool needs to expand.")]
        private int growthStep = 5;

        [SerializeField, Tooltip("Allow the pool to grow beyond the prewarmed count when demand exceeds supply.")]
        private bool allowGrowth = true;

        private readonly Queue<FloatingText> availableTexts = new Queue<FloatingText>();
        private readonly List<FloatingText> allTexts = new List<FloatingText>();
        private readonly HashSet<FloatingText> pooledLookup = new HashSet<FloatingText>();

        /// <summary>
        /// Ensures a single pool instance exists and prewarms the requested number of entries.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate FloatingTextPool detected. Destroying the newest instance to preserve the original pool.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Prewarm(initialPoolSize);
        }

        /// <summary>
        /// Clears the singleton reference if this pool is destroyed (i.e. during scene transitions).
        /// </summary>
        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Validates inspector data to ensure sensible pooling bounds when edited at runtime.
        /// </summary>
        private void OnValidate()
        {
            maxPoolSize = Mathf.Max(1, maxPoolSize);
            initialPoolSize = Mathf.Clamp(initialPoolSize, 0, maxPoolSize);
            growthStep = Mathf.Max(1, growthStep);
        }

        /// <summary>
        /// Requests a floating text instance from the pool, growing the pool if necessary and permitted.
        /// </summary>
        /// <param name="instance">Returned pooled instance ready for configuration.</param>
        /// <returns>True if an instance was supplied, false if the pool is exhausted.</returns>
        public bool TryGet(out FloatingText instance)
        {
            if (availableTexts.Count == 0)
                TryExpandPool();

            if (availableTexts.Count == 0)
            {
                instance = null;
                return false;
            }

            instance = availableTexts.Dequeue();
            pooledLookup.Remove(instance);

            if (!instance.gameObject.activeSelf)
                instance.gameObject.SetActive(true);

            instance.transform.SetAsLastSibling();
            instance.transform.localScale = Vector3.one;
            return true;
        }

        /// <summary>
        /// Returns a floating text instance back to the pool after its lifetime expires.
        /// </summary>
        /// <param name="floatingText">Instance that should be recycled.</param>
        internal void Release(FloatingText floatingText)
        {
            if (floatingText == null)
                return;

            floatingText.ResetForPool();
            floatingText.gameObject.SetActive(false);
            floatingText.transform.SetParent(transform, false);

            if (pooledLookup.Add(floatingText))
                availableTexts.Enqueue(floatingText);
        }

        /// <summary>
        /// Attempts to grow the pool by the configured step if growth is allowed and the cap has not been reached.
        /// </summary>
        private void TryExpandPool()
        {
            if (!allowGrowth || allTexts.Count >= maxPoolSize)
                return;

            int availableSpace = Mathf.Max(0, maxPoolSize - allTexts.Count);
            if (availableSpace <= 0)
                return;

            int toCreate = Mathf.Min(growthStep, availableSpace);
            Prewarm(toCreate);
        }

        /// <summary>
        /// Creates and enqueues the requested number of floating text instances.
        /// </summary>
        /// <param name="amount">Number of instances to generate.</param>
        private void Prewarm(int amount)
        {
            int availableSpace = Mathf.Max(0, maxPoolSize - allTexts.Count);
            int createCount = Mathf.Min(Mathf.Max(0, amount), availableSpace);
            for (int i = 0; i < createCount; i++)
            {
                FloatingText textInstance = CreateFloatingTextInstance();
                textInstance.gameObject.SetActive(false);
                availableTexts.Enqueue(textInstance);
                pooledLookup.Add(textInstance);
            }
        }

        /// <summary>
        /// Generates a new floating text object with the correct UI hierarchy and wiring.
        /// </summary>
        private FloatingText CreateFloatingTextInstance()
        {
            GameObject root = new GameObject($"FloatingText_{allTexts.Count}", typeof(RectTransform));
            root.transform.SetParent(transform, false);

            var rectTransform = root.GetComponent<RectTransform>();
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.localScale = Vector3.one;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            root.AddComponent<GraphicRaycaster>();

            GameObject backgroundGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundGO.transform.SetParent(root.transform, false);
            var backgroundRect = backgroundGO.GetComponent<RectTransform>();
            backgroundRect.anchorMin = backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            var backgroundImage = backgroundGO.GetComponent<Image>();
            backgroundImage.enabled = false;
            backgroundImage.raycastTarget = false;

            GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(root.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;

            var textComponent = textGO.GetComponent<Text>();
            textComponent.alignment = TextAnchor.MiddleCenter;
            textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
            textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComponent.raycastTarget = false;

            // Ensure the background renders beneath the text.
            backgroundGO.transform.SetSiblingIndex(0);
            textGO.transform.SetSiblingIndex(1);

            var floatingText = root.AddComponent<FloatingText>();
            floatingText.ConfigureFromPool(this, textComponent, rectTransform, backgroundImage);

            allTexts.Add(floatingText);
            return floatingText;
        }
    }
}
