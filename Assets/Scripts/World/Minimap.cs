using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Skills;
using BankSystem;
using ShopSystem;
using Player;

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
        private GameObject expandedRoot;
        private GameObject smallRoot;
        private RectTransform smallMapRect;
        private RectTransform expandedMapRect;

        private readonly List<MinimapMarker> markers = new List<MinimapMarker>();
        private readonly Dictionary<MinimapMarker.MarkerType, Sprite> iconCache = new Dictionary<MinimapMarker.MarkerType, Sprite>();

        private static Minimap instance;
        public static Minimap Instance => instance;

        private const float ZoomStep = 5f;
        private const float MinZoom = 5f;
        private const float MaxZoom = 100f;
        private const float MarkerScale = 0.25f;
        private const float SmallIconScaleMultiplier = 0.5f;
        private const int SmallMapZoomSteps = 3;
        private const float DefaultZoom = 25f;
        private float SmallMapZoom => DefaultZoom - ZoomStep * SmallMapZoomSteps;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateInstance()
        {
            var go = new GameObject("Minimap");
            DontDestroyOnLoad(go);
            go.AddComponent<Minimap>();
        }

        private void Awake()
        {
            instance = this;
            CreateCamera();
            CreateUI();
            RegisterExistingMarkers();
            ResetSmallMapZoom();
        }

        private void CreateCamera()
        {
            // Increase resolution so the expanded map remains sharp
            mapTexture = new RenderTexture(512, 512, 16)
            {
                name = "MinimapTexture"
            };
            // Ensure the render texture is ready to receive camera output
            mapTexture.Create();

            var camGO = new GameObject("MinimapCamera");
            camGO.transform.SetParent(transform, false);
            mapCamera = camGO.AddComponent<Camera>();
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = DefaultZoom;
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

            smallRoot = new GameObject("Small", typeof(RectTransform));
            smallRoot.transform.SetParent(canvasGO.transform, false);
            var smallRect = smallRoot.GetComponent<RectTransform>();
            smallRect.anchorMin = new Vector2(1f, 1f);
            smallRect.anchorMax = new Vector2(1f, 1f);
            smallRect.pivot = new Vector2(1f, 1f);
            smallRect.anchoredPosition = Vector2.zero;

            var borderGO = new GameObject("Border", typeof(Image));
            borderGO.transform.SetParent(smallRoot.transform, false);
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
            smallMapRect = rawRect;
            const int btnSize = 24;
            const int btnSpacing = 4;

            // Expanded minimap window (center of screen)
            const int expandedWidth = 512;
            const int expandedHeight = 384;
            const int expandedBorder = 8;
            expandedRoot = new GameObject("Expanded", typeof(Image));
            expandedRoot.transform.SetParent(canvasGO.transform, false);
            var expandedImg = expandedRoot.GetComponent<Image>();
            expandedImg.color = new Color32(64, 64, 64, 255);
            var expandedRect = expandedImg.rectTransform;
            expandedRect.anchorMin = new Vector2(0.5f, 0.5f);
            expandedRect.anchorMax = new Vector2(0.5f, 0.5f);
            expandedRect.pivot = new Vector2(0.5f, 0.5f);
            expandedRect.sizeDelta = new Vector2(expandedWidth + expandedBorder * 2, expandedHeight + expandedBorder * 2);
            expandedRect.anchoredPosition = Vector2.zero;

            var expandedRawGO = new GameObject("Image", typeof(RawImage));
            expandedRawGO.transform.SetParent(expandedRoot.transform, false);
            var expandedRawImg = expandedRawGO.GetComponent<RawImage>();
            expandedRawImg.texture = mapTexture;
            var expandedRawRect = expandedRawImg.rectTransform;
            expandedRawRect.anchorMin = Vector2.zero;
            expandedRawRect.anchorMax = Vector2.one;
            expandedRawRect.offsetMin = new Vector2(expandedBorder, expandedBorder);
            expandedRawRect.offsetMax = new Vector2(-expandedBorder, -expandedBorder);
            expandedMapRect = expandedRawRect;

            // Buttons for expanded map
            var closeGO = new GameObject("Close", typeof(Image), typeof(Button));
            closeGO.transform.SetParent(expandedRoot.transform, false);
            var closeImg = closeGO.GetComponent<Image>();
            closeImg.sprite = Resources.Load<Sprite>("Interfaces/Minimap/ExpandButton");
            closeImg.preserveAspect = true;
            var closeRect = closeImg.rectTransform;
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(btnSize, btnSize);
            closeRect.anchoredPosition = new Vector2(-btnSpacing, -btnSpacing);
            closeGO.GetComponent<Button>().onClick.AddListener(ToggleExpanded);

            var bigPlusGO = new GameObject("ZoomIn", typeof(Image), typeof(Button));
            bigPlusGO.transform.SetParent(expandedRoot.transform, false);
            var bigPlusImg = bigPlusGO.GetComponent<Image>();
            bigPlusImg.sprite = Resources.Load<Sprite>("Interfaces/Minimap/PlusButton");
            bigPlusImg.preserveAspect = true;
            var bigPlusRect = bigPlusImg.rectTransform;
            bigPlusRect.anchorMin = new Vector2(1f, 1f);
            bigPlusRect.anchorMax = new Vector2(1f, 1f);
            bigPlusRect.pivot = new Vector2(1f, 1f);
            bigPlusRect.sizeDelta = new Vector2(btnSize, btnSize);
            bigPlusRect.anchoredPosition = closeRect.anchoredPosition + new Vector2(0f, -btnSize - btnSpacing);
            bigPlusGO.GetComponent<Button>().onClick.AddListener(ZoomIn);

            var bigMinusGO = new GameObject("ZoomOut", typeof(Image), typeof(Button));
            bigMinusGO.transform.SetParent(expandedRoot.transform, false);
            var bigMinusImg = bigMinusGO.GetComponent<Image>();
            bigMinusImg.sprite = Resources.Load<Sprite>("Interfaces/Minimap/MinusButton");
            bigMinusImg.preserveAspect = true;
            var bigMinusRect = bigMinusImg.rectTransform;
            bigMinusRect.anchorMin = new Vector2(1f, 1f);
            bigMinusRect.anchorMax = new Vector2(1f, 1f);
            bigMinusRect.pivot = new Vector2(1f, 1f);
            bigMinusRect.sizeDelta = new Vector2(btnSize, btnSize);
            bigMinusRect.anchoredPosition = bigPlusRect.anchoredPosition + new Vector2(0f, -btnSize - btnSpacing);
            bigMinusGO.GetComponent<Button>().onClick.AddListener(ZoomOut);

            expandedRoot.SetActive(false);

            // Load sprite from "Assets/Interfaces/Minimap/ExpandButton/ExpandButton.png"
            var expandGO = new GameObject("Expand", typeof(Image), typeof(Button));
            expandGO.transform.SetParent(borderGO.transform, false);
            var expandImg = expandGO.GetComponent<Image>();
            expandImg.sprite = Resources.Load<Sprite>("Interfaces/Minimap/ExpandButton");
            expandImg.preserveAspect = true;
            var expandRect = expandImg.rectTransform;
            expandRect.anchorMin = new Vector2(1f, 1f);
            expandRect.anchorMax = new Vector2(1f, 1f);
            expandRect.pivot = new Vector2(1f, 1f);
            expandRect.sizeDelta = new Vector2(btnSize, btnSize);
            expandRect.anchoredPosition = new Vector2(-btnSpacing, -btnSpacing);
            expandGO.GetComponent<Button>().onClick.AddListener(ToggleExpanded);

        }

        private void RegisterExistingMarkers()
        {
            var existing = FindObjectsOfType<MinimapMarker>();
            foreach (var marker in existing)
            {
                if (!markers.Contains(marker))
                    markers.Add(marker);
                CreateIcons(marker);
            }
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

            if (Input.GetKeyDown(KeyCode.M))
            {
                ToggleExpanded();
            }

            if (mapCamera != null)
            {
                foreach (var marker in markers)
                {
                    if (marker == null) continue;
                    UpdateIconPosition(marker.smallIcon, marker.transform.position, smallMapRect);
                    UpdateIconPosition(marker.bigIcon, marker.transform.position, expandedMapRect);
                }
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

        private void ResetSmallMapZoom()
        {
            if (mapCamera != null)
                mapCamera.orthographicSize = SmallMapZoom;
        }

        private void ToggleExpanded()
        {
            if (expandedRoot == null)
                return;

            bool opening = !expandedRoot.activeSelf;
            if (opening)
            {
                var bank = BankUI.Instance;
                if (bank != null && bank.IsOpen)
                    return;
                var shop = ShopUI.Instance;
                if (shop != null && shop.IsOpen)
                    return;

                var skills = SkillsUI.Instance;
                if (skills != null && skills.IsOpen)
                    skills.Close();
                var inv = Object.FindObjectOfType<Inventory.Inventory>();
                if (inv != null && inv.IsOpen)
                    inv.CloseUI();
            }

            expandedRoot.SetActive(opening);
            if (smallRoot != null)
                smallRoot.SetActive(!opening);

            if (!opening)
                ResetSmallMapZoom();

            var playerObj = target != null ? target.gameObject : GameObject.FindGameObjectWithTag("Player");
            var mover = playerObj != null ? playerObj.GetComponent<PlayerMover>() : null;
            if (mover != null)
                mover.enabled = !opening;
        }

        public void Register(MinimapMarker marker)
        {
            if (marker == null)
                return;

            if (!markers.Contains(marker))
                markers.Add(marker);

            CreateIcons(marker);
        }

        private void CreateIcons(MinimapMarker marker)
        {
            if (marker == null)
                return;

            if (smallMapRect != null && marker.smallIcon == null)
            {
                var smallGO = new GameObject("Marker", typeof(Image));
                smallGO.transform.SetParent(smallMapRect, false);
                var img = smallGO.GetComponent<Image>();
                img.sprite = GetMarkerSprite(marker.type);
                img.preserveAspect = true;
                marker.smallIcon = img.rectTransform;
                marker.smallIcon.localScale = Vector3.one * MarkerScale * SmallIconScaleMultiplier;
            }

            if (expandedMapRect != null && marker.bigIcon == null)
            {
                var bigGO = new GameObject("Marker", typeof(Image));
                bigGO.transform.SetParent(expandedMapRect, false);
                var img = bigGO.GetComponent<Image>();
                img.sprite = GetMarkerSprite(marker.type);
                img.preserveAspect = true;
                marker.bigIcon = img.rectTransform;
                marker.bigIcon.localScale = Vector3.one * MarkerScale;
            }
        }

        public void Unregister(MinimapMarker marker)
        {
            if (marker == null)
                return;

            markers.Remove(marker);
            if (marker.smallIcon != null)
                Destroy(marker.smallIcon.gameObject);
            if (marker.bigIcon != null)
                Destroy(marker.bigIcon.gameObject);
        }

        private Sprite GetMarkerSprite(MinimapMarker.MarkerType type)
        {
            if (!iconCache.TryGetValue(type, out var sprite) || sprite == null)
            {
                string path = type switch
                {
                    MinimapMarker.MarkerType.Bank => "Interfaces/Minimap/Bank",
                    MinimapMarker.MarkerType.Shop => "Interfaces/Minimap/Shop",
                    MinimapMarker.MarkerType.Ore => "Interfaces/Minimap/Ore",
                    MinimapMarker.MarkerType.Tree => "Interfaces/Minimap/Tree",
                    _ => null
                };
                if (!string.IsNullOrEmpty(path))
                    sprite = Resources.Load<Sprite>(path);
                iconCache[type] = sprite;
            }
            return sprite;
        }

        private void UpdateIconPosition(RectTransform icon, Vector3 worldPos, RectTransform container)
        {
            if (icon == null || container == null || mapCamera == null)
                return;

            Vector3 viewport = mapCamera.WorldToViewportPoint(worldPos);
            Vector2 size = container.rect.size;
            Vector2 pos = new Vector2((viewport.x - 0.5f) * size.x, (viewport.y - 0.5f) * size.y);

            // Clamp the icon to the bounds of the minimap so it never renders outside
            Vector2 halfSize = size * 0.5f;
            Vector2 iconHalf = Vector2.Scale(icon.rect.size, icon.localScale) * 0.5f;
            pos.x = Mathf.Clamp(pos.x, -halfSize.x + iconHalf.x, halfSize.x - iconHalf.x);
            pos.y = Mathf.Clamp(pos.y, -halfSize.y + iconHalf.y, halfSize.y - iconHalf.y);

            icon.anchoredPosition = pos;
        }
    }
}
