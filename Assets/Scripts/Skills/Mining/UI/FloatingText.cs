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
            instance.uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

    }
}
