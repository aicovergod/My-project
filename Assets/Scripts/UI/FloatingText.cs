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

        private Text uiText;
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
            GameObject go = new GameObject("FloatingText", typeof(Canvas));
            var instance = go.AddComponent<FloatingText>();
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.AddComponent<GraphicRaycaster>();

            GameObject parentGO = go;

            if (background != null)
            {
                var imageGO = new GameObject("Background", typeof(Image));
                imageGO.transform.SetParent(go.transform, false);
                var image = imageGO.GetComponent<Image>();
                image.sprite = background;
                image.SetNativeSize();
                parentGO = imageGO;
            }

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(parentGO.transform, false);
            instance.uiText = textGO.GetComponent<Text>();
            instance.uiText.alignment = TextAnchor.MiddleCenter;
            instance.uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            instance.uiText.verticalOverflow = VerticalWrapMode.Overflow;
            LegacyFontProvider.ApplyTo(instance.uiText);
            instance.rectTransform = background != null ? parentGO.GetComponent<RectTransform>() : textGO.GetComponent<RectTransform>();
            instance.mainCamera = Camera.main;

            instance.worldPosition = position;
            if (instance.mainCamera == null)
                instance.mainCamera = Camera.main;
            instance.rectTransform.position = instance.mainCamera.WorldToScreenPoint(position);
            instance.uiText.text = message;
            instance.uiText.color = color ?? Color.white;
            float finalSize = size ?? instance.textSize;
            instance.uiText.fontSize = Mathf.RoundToInt(64 * finalSize);
            instance.remainingLifetime = instance.lifetime;
            instance.needsInitialSnap = true;

            if (debugLogMessages)
            {
                // Mirror the popup in the console so QA can diagnose why the message appeared.
                Debug.Log($"[FloatingText] {message}");
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

    }
}
