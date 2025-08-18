using UnityEngine;
using UnityEngine.UI;

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

        private const float ZoomStep = 5f;
        private const float MinZoom = 5f;
        private const float MaxZoom = 100f;


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
            const int btnSize = 24;
            const int btnSpacing = 4;

            // Load sprite from "Assets/Interfaces/Minimap.PlusButton.png"
            var plusGO = new GameObject("ZoomIn", typeof(Image), typeof(Button));
            plusGO.transform.SetParent(canvasGO.transform, false);
            var plusImg = plusGO.GetComponent<Image>();
            plusImg.sprite = Resources.Load<Sprite>("Interfaces/Minimap/PlusButton");
            plusImg.preserveAspect = true;
            var plusRect = plusImg.rectTransform;
            plusRect.anchorMin = new Vector2(1f, 1f);
            plusRect.anchorMax = new Vector2(1f, 1f);
            plusRect.pivot = new Vector2(1f, 1f);
            plusRect.sizeDelta = new Vector2(btnSize, btnSize);
            plusRect.anchoredPosition = new Vector2(-10f, -10f - (size + border * 2) - btnSpacing);
            plusGO.GetComponent<Button>().onClick.AddListener(ZoomIn);

            // Load sprite from "Assets/Interfaces/Minimap.MinusButton.png"
            var minusGO = new GameObject("ZoomOut", typeof(Image), typeof(Button));
            minusGO.transform.SetParent(canvasGO.transform, false);
            var minusImg = minusGO.GetComponent<Image>();
            minusImg.sprite = Resources.Load<Sprite>("Interfaces/Minimap/MinusButton");
            minusImg.preserveAspect = true;
            var minusRect = minusImg.rectTransform;
            minusRect.anchorMin = new Vector2(1f, 1f);
            minusRect.anchorMax = new Vector2(1f, 1f);
            minusRect.pivot = new Vector2(1f, 1f);
            minusRect.sizeDelta = new Vector2(btnSize, btnSize);
            minusRect.anchoredPosition = plusRect.anchoredPosition + new Vector2(0f, -btnSize - btnSpacing);
            minusGO.GetComponent<Button>().onClick.AddListener(ZoomOut);

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

        }

        private void ZoomIn()
        {
            if (mapCamera != null)
                mapCamera.orthographicSize = Mathf.Max(MinZoom, mapCamera.orthographicSize - ZoomStep);
        }

        private void ZoomOut()
        {
            if (mapCamera != null)
                mapCamera.orthographicSize = Mathf.Min(MaxZoom, mapCamera.orthographicSize + ZoomStep);
        }
    }
}
