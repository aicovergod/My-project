using System.Collections.Generic;
using System.Text;
using UI.Chat;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Simple floating text utility for feedback messages.
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        [SerializeField] private float lifetime = 1.5f;
        [SerializeField] private Vector3 floatSpeed = new Vector3(0f, 1f, 0f);
        [SerializeField] private float textSize = 0.2f;

        private EmojiTokenLayout tokenLayout;
        private RectTransform rectTransform;
        private Vector3 worldPosition;
        private Camera mainCamera;
        private float remainingLifetime;
        private bool needsInitialSnap = true;

        /// <summary>
        ///     Backing field for <see cref="DebugLogMessages"/> so the toggle persists between spawn calls.
        /// </summary>
        private static bool debugLogMessages;

        /// <summary>
        ///     When enabled via the admin debug menu, every floating text message is echoed to the console.
        /// </summary>
        public static bool DebugLogMessages
        {
            get => debugLogMessages;
            set => debugLogMessages = value;
        }

        public static void Show(string message, Vector3 position, Color? color = null, float? size = null, Sprite background = null)
        {
            var tokens = EmojiMarkupParser.Parse(message ?? string.Empty);
            Show(tokens, position, color, size, background);
        }

        /// <summary>
        /// Displays floating text by rendering a pre-parsed token list.
        /// </summary>
        public static void Show(IReadOnlyList<EmojiMarkupToken> tokens, Vector3 position, Color? color = null, float? size = null, Sprite background = null)
        {
            GameObject go = new GameObject("FloatingText", typeof(Canvas));
            var instance = go.AddComponent<FloatingText>();
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.AddComponent<GraphicRaycaster>();

            GameObject parentGO = go;
            RectTransform parentRect = go.GetComponent<RectTransform>();

            if (background != null)
            {
                var imageGO = new GameObject("Background", typeof(Image));
                imageGO.transform.SetParent(go.transform, false);
                var image = imageGO.GetComponent<Image>();
                image.sprite = background;
                image.SetNativeSize();
                parentGO = imageGO;
                parentRect = imageGO.GetComponent<RectTransform>();
            }

            var contentGO = new GameObject("Content", typeof(RectTransform), typeof(EmojiTokenLayout));
            var contentRect = contentGO.GetComponent<RectTransform>();
            contentRect.SetParent(parentGO.transform, false);
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;

            instance.tokenLayout = contentGO.GetComponent<EmojiTokenLayout>();
            var targetRect = background != null ? parentRect : contentRect;
            instance.rectTransform = targetRect;
            instance.mainCamera = Camera.main;

            instance.worldPosition = position;
            if (instance.mainCamera == null)
                instance.mainCamera = Camera.main;
            instance.rectTransform.position = instance.mainCamera.WorldToScreenPoint(position);
            float finalSize = size ?? instance.textSize;
            int fontSize = Mathf.RoundToInt(64 * finalSize);
            Color resolvedColor = color ?? Color.white;
            instance.RenderTokens(tokens, resolvedColor, fontSize);
            instance.remainingLifetime = instance.lifetime;
            instance.needsInitialSnap = true;

            if (debugLogMessages)
            {
                Debug.Log($"[FloatingText] {BuildDebugMessage(tokens)}");
            }
        }

        private void Awake()
        {
            remainingLifetime = lifetime;
        }

        private void LateUpdate()
        {
            // Ensure we always have the latest camera reference in case the active camera changed mid-session.
            if (mainCamera == null)
                mainCamera = Camera.main;

            if (rectTransform == null || mainCamera == null)
                return;

            // Reapply the first projection in LateUpdate so the spawn frame respects any camera movement that
            // occurred after the popup was created earlier in the frame.
            if (needsInitialSnap)
            {
                rectTransform.position = mainCamera.WorldToScreenPoint(worldPosition);
                needsInitialSnap = false;
            }

            worldPosition += floatSpeed * Time.deltaTime;
            rectTransform.position = mainCamera.WorldToScreenPoint(worldPosition);

            remainingLifetime -= Time.deltaTime;
            if (remainingLifetime <= 0f)
                Destroy(gameObject);
        }

        private void RenderTokens(IReadOnlyList<EmojiMarkupToken> tokens, Color color, int fontSize)
        {
            if (tokenLayout == null)
                return;

            var payload = tokens ?? EmojiMarkupParser.Parse(string.Empty);
            tokenLayout.RenderTokens(payload, color, fontSize, TextAnchor.MiddleCenter);
        }

        private static string BuildDebugMessage(IReadOnlyList<EmojiMarkupToken> tokens)
        {
            if (tokens == null || tokens.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.IsEmoji)
                    builder.Append($"<emoji={token.Emoji.Key}>");
                else
                    builder.Append(token.Text);
            }

            return builder.ToString();
        }
    }
}
