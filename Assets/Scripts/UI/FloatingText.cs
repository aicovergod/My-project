using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Simple floating text utility for feedback messages that leverages an object pool.
    /// Instances are configured by <see cref="FloatingTextPool"/> and returned to it when their lifetime expires.
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        [SerializeField, Tooltip("How long the text remains on screen before it returns to the pool.")]
        private float lifetime = 1.5f;

        [SerializeField, Tooltip("World-space velocity applied each frame while the text is visible.")]
        private Vector3 floatSpeed = new Vector3(0f, 1f, 0f);

        [SerializeField, Tooltip("Baseline text size multiplier when no override is supplied (1 = 64px font).")]
        private float textSize = 1f;

        [SerializeField, Tooltip("World-space offset applied when positioning the floating text (helps align with character height).")]
        private Vector3 spawnOffset = new Vector3(0f, 0.75f, 0f);

        private Text uiText;
        private RectTransform rectTransform;
        private RectTransform textRect;
        private Image backgroundImage;
        private Vector3 worldPosition;
        private Camera mainCamera;
        private float remainingLifetime;
        private FloatingTextPool owningPool;

        /// <summary>
        /// Displays a floating text message using a pooled instance.
        /// </summary>
        /// <param name="message">Message to render.</param>
        /// <param name="position">World position the text should track.</param>
        /// <param name="color">Optional colour override.</param>
        /// <param name="size">Optional size override in OSRS units (1 = 64px font).</param>
        /// <param name="background">Optional background sprite.</param>
        public static void Show(string message, Vector3 position, Color? color = null, float? size = null, Sprite background = null)
        {
            var pool = FloatingTextPool.Instance;
            if (pool == null)
            {
                Debug.LogWarning("FloatingText.Show was called but no FloatingTextPool exists in the scene.");
                return;
            }

            if (!pool.TryGet(out FloatingText instance) || instance == null)
            {
                Debug.LogWarning("FloatingTextPool is exhausted and cannot provide a floating text instance.");
                return;
            }

            instance.Present(message, position, color, size, background);
        }

        /// <summary>
        /// Injects pooled UI component references into the instance.
        /// </summary>
        /// <param name="pool">The managing pool.</param>
        /// <param name="textComponent">Text component used for rendering.</param>
        /// <param name="canvasRect">RectTransform that will be positioned in screen-space.</param>
        /// <param name="background">Background image that can optionally be shown.</param>
        internal void ConfigureFromPool(FloatingTextPool pool, Text textComponent, RectTransform canvasRect, Image background)
        {
            owningPool = pool;
            uiText = textComponent;
            rectTransform = canvasRect != null ? canvasRect : GetComponent<RectTransform>();
            backgroundImage = background;
            textRect = textComponent.rectTransform;
            ResetForPool();
        }

        /// <summary>
        /// Applies the requested payload to the floating text and begins counting down its lifetime.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="position">World position the text should follow.</param>
        /// <param name="color">Optional colour override.</param>
        /// <param name="size">Optional size override in OSRS units.</param>
        /// <param name="background">Optional background sprite override.</param>
        internal void Present(string message, Vector3 position, Color? color, float? size, Sprite background)
        {
            if (uiText == null)
            {
                Debug.LogError("FloatingText instance has not been configured by the pool.");
                return;
            }

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            // Capture the desired world position with the configured spawn offset so text renders above the anchor point.
            worldPosition = position + spawnOffset;
            mainCamera = Camera.main;

            float finalSize = Mathf.Max(0.01f, size ?? textSize);
            uiText.fontSize = Mathf.RoundToInt(64f * finalSize);
            uiText.text = message;
            uiText.color = color ?? Color.white;

            if (textRect != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(textRect);
                textRect.sizeDelta = new Vector2(uiText.preferredWidth, uiText.preferredHeight);
            }

            if (background != null)
            {
                if (backgroundImage != null)
                {
                    backgroundImage.sprite = background;
                    backgroundImage.enabled = true;
                    backgroundImage.SetNativeSize();
                    backgroundImage.rectTransform.anchoredPosition = Vector2.zero;
                    backgroundImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);

                    if (textRect != null)
                        textRect.sizeDelta = backgroundImage.rectTransform.sizeDelta;
                }
            }
            else if (backgroundImage != null)
            {
                backgroundImage.sprite = null;
                backgroundImage.enabled = false;
                backgroundImage.rectTransform.sizeDelta = Vector2.zero;
            }

            UpdateScreenPosition();
            remainingLifetime = lifetime;
        }

        /// <summary>
        /// Updates the floating text every frame while active.
        /// </summary>
        private void Update()
        {
            if (remainingLifetime <= 0f)
                return;

            worldPosition += floatSpeed * Time.deltaTime;
            UpdateScreenPosition();

            remainingLifetime -= Time.deltaTime;
            if (remainingLifetime <= 0f && owningPool != null)
                owningPool.Release(this);
        }

        /// <summary>
        /// Forces the RectTransform to track the current world position in screen-space.
        /// </summary>
        private void UpdateScreenPosition()
        {
            if (rectTransform == null)
                return;

            if (mainCamera == null)
                mainCamera = Camera.main;

            if (mainCamera != null)
                rectTransform.position = mainCamera.WorldToScreenPoint(worldPosition);
        }

        /// <summary>
        /// Resets all runtime data so the instance is ready for reuse when returned to the pool.
        /// </summary>
        internal void ResetForPool()
        {
            remainingLifetime = 0f;
            worldPosition = Vector3.zero;
            mainCamera = null;

            if (uiText != null)
            {
                uiText.text = string.Empty;
                uiText.color = Color.white;
            }

            if (textRect != null)
                textRect.sizeDelta = Vector2.zero;

            if (backgroundImage != null)
            {
                backgroundImage.sprite = null;
                backgroundImage.enabled = false;
                backgroundImage.rectTransform.sizeDelta = Vector2.zero;
            }
        }
    }
}
