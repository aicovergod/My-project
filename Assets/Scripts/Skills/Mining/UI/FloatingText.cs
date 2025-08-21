using UnityEngine;
using UnityEngine.UI;

namespace Skills.Mining
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

        private static FloatingText activeInstance;

        public static void Show(string message, Vector3 position, Color? color = null, float? size = null)
        {
            if (activeInstance == null)
            {
                GameObject go = new GameObject("FloatingText", typeof(Canvas));
                activeInstance = go.AddComponent<FloatingText>();
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                go.AddComponent<GraphicRaycaster>();

                var textGO = new GameObject("Text", typeof(Text));
                textGO.transform.SetParent(go.transform, false);
                activeInstance.uiText = textGO.GetComponent<Text>();
                activeInstance.uiText.alignment = TextAnchor.MiddleCenter;
                activeInstance.uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
                activeInstance.uiText.verticalOverflow = VerticalWrapMode.Overflow;
                activeInstance.uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                activeInstance.rectTransform = textGO.GetComponent<RectTransform>();
                activeInstance.mainCamera = Camera.main;
            }

            activeInstance.worldPosition = position;
            if (activeInstance.mainCamera == null)
                activeInstance.mainCamera = Camera.main;
            activeInstance.rectTransform.position = activeInstance.mainCamera.WorldToScreenPoint(position);
            activeInstance.uiText.text = message;
            activeInstance.uiText.color = color ?? Color.white;
            float finalSize = size ?? activeInstance.textSize;
            activeInstance.uiText.fontSize = Mathf.RoundToInt(64 * finalSize);
            activeInstance.remainingLifetime = activeInstance.lifetime;
        }

        private void Awake()
        {
            remainingLifetime = lifetime;
        }

        private void Update()
        {
            worldPosition += floatSpeed * Time.deltaTime;
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (rectTransform != null && mainCamera != null)
                rectTransform.position = mainCamera.WorldToScreenPoint(worldPosition);

            remainingLifetime -= Time.deltaTime;
            if (remainingLifetime <= 0f)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (activeInstance == this)
                activeInstance = null;
        }
    }
}
