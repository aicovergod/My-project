using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace World
{
    /// <summary>
    /// Generates a simple minimap in the top-right corner showing a top-down view of the map.
    /// Everything is created via code so no prefabs or scene objects are required.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        private Camera mapCamera;
        private RenderTexture mapTexture;
        private Transform target;

        private const float MinZoom = 5f;
        private const float MaxZoom = 50f;
        private const float ZoomSpeed = 20f;
        private const float ZoomStep = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            var go = new GameObject("Minimap");
            DontDestroyOnLoad(go);
            go.AddComponent<Minimap>();
        }

        private void Awake()
        {
            CreateCamera();
            CreateUI();
        }

        private void CreateCamera()
        {
            mapTexture = new RenderTexture(256, 256, 16)
            {
                name = "MinimapTexture"
            };
            // Ensure the render texture is ready to receive camera output
            mapTexture.Create();

            var camGO = new GameObject("MinimapCamera");
            camGO.transform.SetParent(transform, false);
            mapCamera = camGO.AddComponent<Camera>();
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = 25f;
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = Color.black;
            // Render everything except the UI layer
            mapCamera.cullingMask = ~LayerMask.GetMask("UI");
            mapCamera.targetTexture = mapTexture;
        }

        private void CreateUI()
        {
            var canvasGO = new GameObject("MinimapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            const int size = 128;
            const int border = 4;

            var borderGO = new GameObject("Border", typeof(Image));
            borderGO.transform.SetParent(canvasGO.transform, false);
            var borderImg = borderGO.GetComponent<Image>();
            borderImg.color = new Color32(64, 64, 64, 255);
            var borderRect = borderImg.rectTransform;
            borderRect.anchorMin = new Vector2(1f, 1f);
            borderRect.anchorMax = new Vector2(1f, 1f);
            borderRect.pivot = new Vector2(1f, 1f);
            borderRect.sizeDelta = new Vector2(size + border * 2, size + border * 2);
            borderRect.anchoredPosition = new Vector2(-10f, -10f);

            var rawGO = new GameObject("Image", typeof(RawImage));
            rawGO.transform.SetParent(borderGO.transform, false);
            var rawImg = rawGO.GetComponent<RawImage>();
            rawImg.texture = mapTexture;
            var rawRect = rawImg.rectTransform;
            rawRect.anchorMin = Vector2.zero;
            rawRect.anchorMax = Vector2.one;
            rawRect.offsetMin = new Vector2(border, border);
            rawRect.offsetMax = new Vector2(-border, -border);

            const int buttonSize = 24;
            // Place buttons below the minimap with a small margin
            float buttonY = -(size + border * 2 + buttonSize + 10f);
            CreateZoomButton(canvasGO.transform, "ZoomInButton", "+", new Vector2(-10f, buttonY), ZoomIn);
            CreateZoomButton(canvasGO.transform, "ZoomOutButton", "-", new Vector2(-10f - (buttonSize + 5), buttonY), ZoomOut);
        }

        private void CreateZoomButton(Transform parent, string name, string label, Vector2 anchoredPos, UnityAction onClick)
        {
            var buttonGO = new GameObject(name, typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(parent, false);
            var image = buttonGO.GetComponent<Image>();
            image.color = Color.white;
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(24f, 24f);
            rect.anchoredPosition = anchoredPos;

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(buttonGO.transform, false);
            var txt = textGO.GetComponent<Text>();
            txt.text = label;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.black;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var textRect = txt.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var button = buttonGO.GetComponent<Button>();
            button.onClick.AddListener(onClick);
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    target = player.transform;
            }

            if (target != null && mapCamera != null)
            {
                var pos = target.position;
                mapCamera.transform.position = new Vector3(pos.x, pos.y, -10f);
            }

            if (mapCamera != null)
            {
                var scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.001f)
                    AdjustZoom(-scroll * ZoomSpeed);
            }
        }

        private void ZoomIn() => AdjustZoom(-ZoomStep);

        private void ZoomOut() => AdjustZoom(ZoomStep);

        private void AdjustZoom(float delta)
        {
            var size = mapCamera.orthographicSize + delta;
            mapCamera.orthographicSize = Mathf.Clamp(size, MinZoom, MaxZoom);
        }
    }
}
