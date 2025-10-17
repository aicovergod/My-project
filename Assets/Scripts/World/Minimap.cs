using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Skills;
using BankSystem;
using ShopSystem;
using Player;
using UnityEngine.EventSystems;
using Pets;
using UI.Utilities;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace World
{
    /// <summary>
    /// Generates a simple minimap in the top-right corner showing a top-down view of the map.
    /// Everything is created via code so no prefabs or scene objects are required.
    /// </summary>
    public class Minimap : SceneGatedSingletonBehaviour<Minimap>
    {
        private Camera mapCamera;
        private RenderTexture mapTexture;
        private Transform target;
        private PlayerMover cachedPlayerMover;
        private GameObject expandedRoot;
        private GameObject smallRoot;
        private RectTransform smallMapRect;
        private RectTransform expandedMapRect;
        private Vector3 dragOffset;
        private RectTransform borderRect;
        private RectTransform smallRootRect;
        private Canvas minimapCanvas;

        private readonly List<MinimapMarker> markers = new List<MinimapMarker>();
        private readonly Dictionary<MinimapMarker.MarkerType, Sprite> iconCache = new Dictionary<MinimapMarker.MarkerType, Sprite>();

        public static Minimap Instance => SceneGatedSingletonBehaviour<Minimap>.Instance;
        private static bool debugTeleportOnClickEnabled;

        /// <summary>
        ///     Reused buffer for UI raycasts when diagnosing debug teleports so we avoid per-click allocations.
        /// </summary>
        private static readonly List<RaycastResult> teleportRaycastBuffer = new List<RaycastResult>();

        public RectTransform BorderRect => borderRect;
        public RectTransform SmallRootRect => smallRootRect;
        public Canvas MinimapCanvas => minimapCanvas;

        /// <summary>
        ///     Enables debug-only teleportation whenever the minimap is clicked. When false, the minimap behaves normally.
        /// </summary>
        public static bool DebugTeleportOnClickEnabled
        {
            get => debugTeleportOnClickEnabled;
            set => debugTeleportOnClickEnabled = value;
        }

        private const float ZoomStep = 5f;
        private const float MinZoom = 5f;
        private const float MaxZoom = 100f;
        private const int SmallMapZoomSteps = 3;
        private const float DefaultZoom = 25f;
        private float SmallMapZoom => DefaultZoom - ZoomStep * SmallMapZoomSteps;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static Minimap CreateInstance()
        {
            var go = new GameObject(nameof(Minimap));
            return go.AddComponent<Minimap>();
        }

        protected override void OnSingletonAwake()
        {
            CreateCamera();
            CreateUI();
            RegisterExistingMarkers();
            ResetSmallMapZoom();
        }

        private void OnEnable()
        {
            if (mapTexture != null && !mapTexture.IsCreated())
                mapTexture.Create();
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        protected override void OnSingletonDestroyed()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            TearDownResources();
        }

        private void CreateCamera()
        {
            if (mapCamera != null)
                return;

            if (mapTexture != null && mapTexture.IsCreated())
                mapTexture.Release();

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
            if (minimapCanvas != null)
                return;

            var overlay = OverlayCanvasFactory.CreateOverlayCanvas(
                "MinimapCanvas",
                new Vector2(1920f, 1080f),
                parent: transform,
                assignToUiLayer: true);
            var canvasGO = overlay.Root;
            minimapCanvas = overlay.Canvas;

            const int size = 128;
            const int border = 4;

            smallRoot = new GameObject("Small", typeof(RectTransform));
            smallRoot.transform.SetParent(canvasGO.transform, false);
            smallRootRect = smallRoot.GetComponent<RectTransform>();
            smallRootRect.anchorMin = new Vector2(1f, 1f);
            smallRootRect.anchorMax = new Vector2(1f, 1f);
            smallRootRect.pivot = new Vector2(1f, 1f);
            smallRootRect.anchoredPosition = Vector2.zero;

            var borderGO = new GameObject("Border", typeof(Image));
            borderGO.transform.SetParent(smallRoot.transform, false);
            var borderImg = borderGO.GetComponent<Image>();
            borderImg.color = new Color32(64, 64, 64, 255);
            var borderRect = borderImg.rectTransform;
            this.borderRect = borderRect;
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

            var hp = Object.FindObjectOfType<PlayerHitpoints>();
            if (hp != null)
                HealthHUD.CreateUnderMinimap(borderRect, hp);

        }

        private void RegisterExistingMarkers()
        {
            var existing = FindObjectsOfType<MinimapMarker>();
            foreach (var marker in existing)
            {
                if (!markers.Contains(marker))
                    markers.Add(marker);
                ValidateManualIcons(marker);
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mapTexture != null && !mapTexture.IsCreated())
                mapTexture.Create();

            if (mapCamera != null)
                mapCamera.targetTexture = mapTexture;

            if (smallMapRect != null)
            {
                var raw = smallMapRect.GetComponent<RawImage>();
                if (raw != null)
                    raw.texture = mapTexture;
            }

            if (expandedMapRect != null)
            {
                var raw = expandedMapRect.GetComponent<RawImage>();
                if (raw != null)
                    raw.texture = mapTexture;
            }

            RegisterExistingMarkers();
            ResetSmallMapZoom();
            dragOffset = Vector3.zero;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    target = player.transform;
                    cachedPlayerMover = player.GetComponent<PlayerMover>();
                }
            }
            else if (cachedPlayerMover == null)
            {
                cachedPlayerMover = target.GetComponent<PlayerMover>();
            }

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
#endif

            Vector3 pointerPosition = Vector3.zero;
            Vector2 pointerDelta = Vector2.zero;
            float scrollDelta = 0f;
            bool toggleRequested = false;
            bool dragButtonPressed = false;
            bool leftClickPressed = false;

#if ENABLE_LEGACY_INPUT_MANAGER
            pointerPosition = Input.mousePosition;
            pointerDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            scrollDelta = Input.mouseScrollDelta.y;
            dragButtonPressed = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            toggleRequested = Input.GetKeyDown(KeyCode.M);
            leftClickPressed = Input.GetMouseButtonDown(0);
#elif ENABLE_INPUT_SYSTEM
            if (mouse != null)
            {
                Vector2 mousePosition = mouse.position.ReadValue();
                pointerPosition = new Vector3(mousePosition.x, mousePosition.y, 0f);
                pointerDelta = mouse.delta.ReadValue();
                scrollDelta = mouse.scroll.ReadValue().y;
                dragButtonPressed = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                leftClickPressed = mouse.leftButton.wasPressedThisFrame;
            }

            if (keyboard != null)
                toggleRequested = keyboard.mKey.wasPressedThisFrame;
#endif

            if (mapCamera != null)
            {
                if (IsExpanded && expandedMapRect != null && dragButtonPressed)
                {
                    float worldPerPixel = (mapCamera.orthographicSize * 2f) / expandedMapRect.rect.height;
                    dragOffset += new Vector3(-pointerDelta.x, -pointerDelta.y, 0f) * worldPerPixel * 10f;
                }
                else if (!IsExpanded)
                {
                    dragOffset = Vector3.zero;
                }

                HandleExpandedScrollZoom(scrollDelta, pointerPosition);
            }

            if (target != null && mapCamera != null)
            {
                var pos = target.position + dragOffset;
                mapCamera.transform.position = new Vector3(pos.x, pos.y, -10f);
            }

            if (toggleRequested)
            {
                ToggleExpanded();
            }

            if (DebugTeleportOnClickEnabled)
                HandleDebugTeleportClick(leftClickPressed, pointerPosition);

            if (mapCamera != null)
            {
                foreach (var marker in markers)
                {
                    if (marker == null) continue;
                    UpdateIconPosition(marker.SmallIcon, marker.transform.position, smallMapRect);
                    UpdateIconPosition(marker.BigIcon, marker.transform.position, expandedMapRect);
                }
            }
        }

        /// <summary>
        ///     Handles scroll-wheel zooming while the expanded minimap is visible and the pointer is over the map.
        /// </summary>
        /// <param name="scrollDelta">The scroll value reported by the active input backend.</param>
        /// <param name="screenPosition">The pointer position in screen space.</param>
        private void HandleExpandedScrollZoom(float scrollDelta, Vector3 screenPosition)
        {
            if (mapCamera == null)
                return;

            if (Mathf.Approximately(scrollDelta, 0f))
                return;

            if (!ShouldBlockWorldCameraZoom(new Vector2(screenPosition.x, screenPosition.y)))
                return;

            float newSize = Mathf.Clamp(mapCamera.orthographicSize - scrollDelta * ZoomStep, MinZoom, MaxZoom);
            mapCamera.orthographicSize = newSize;
        }

        /// <summary>
        ///     Determines whether the expanded minimap should consume the current scroll event rather than
        ///     allowing the world camera to zoom.
        /// </summary>
        /// <param name="screenPosition">Pointer position in screen space.</param>
        public bool ShouldBlockWorldCameraZoom(Vector2 screenPosition)
        {
            if (!IsExpanded || expandedMapRect == null)
                return false;

            var referenceCamera = minimapCanvas != null ? minimapCanvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(expandedMapRect, screenPosition, referenceCamera))
                return false;

            if (PointerHitsBlockingControl(screenPosition))
                return false;

            return true;
        }

        /// <summary>
        ///     Processes left-clicks on the minimap while the debug toggle is enabled and teleports the player when appropriate.
        /// </summary>
        /// <param name="leftClickPressed">Whether the primary pointer button was pressed during the current frame.</param>
        /// <param name="screenPosition">The pointer position in screen space.</param>
        private void HandleDebugTeleportClick(bool leftClickPressed, Vector3 screenPosition)
        {
            if (mapCamera == null || !leftClickPressed)
                return;

            // Operate entirely in screen space so UI checks and rect conversions agree on coordinates.

            if (PointerHitsBlockingControl(screenPosition))
                return;

            // Resolve the minimap click into world space. Bail if the pointer is outside the active rect.
            if (!TryGetTeleportWorldPosition(screenPosition, out Vector3 worldPosition))
                return;

            TeleportPlayerTo(worldPosition);
        }

        /// <summary>
        ///     Checks if the pointer is currently over a UI control (such as zoom or expand buttons) that should block teleporting.
        /// </summary>
        private bool PointerHitsBlockingControl(Vector3 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            var pointerData = new PointerEventData(eventSystem)
            {
                position = screenPosition
            };

            // Reuse the cached buffer to avoid allocations every time QA clicks the minimap while testing.
            teleportRaycastBuffer.Clear();
            eventSystem.RaycastAll(pointerData, teleportRaycastBuffer);

            for (int i = 0; i < teleportRaycastBuffer.Count; i++)
            {
                var result = teleportRaycastBuffer[i];
                // Skip teleportation when the click hits interactive buttons like zoom or expand controls.
                if (result.gameObject != null && result.gameObject.TryGetComponent<Button>(out _))
                {
                    teleportRaycastBuffer.Clear();
                    return true;
                }
            }

            teleportRaycastBuffer.Clear();
            return false;
        }

        /// <summary>
        ///     Converts a minimap click into a world position so the player can be warped to the target tile.
        /// </summary>
        private bool TryGetTeleportWorldPosition(Vector3 screenPosition, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            if (mapCamera == null)
                return false;

            RectTransform activeRect = null;
            Camera eventCamera = minimapCanvas != null ? minimapCanvas.worldCamera : null;

            if (expandedRoot != null && expandedRoot.activeSelf && expandedMapRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(expandedMapRect, screenPosition, eventCamera))
            {
                // Prioritize the expanded rect whenever it is visible.
                activeRect = expandedMapRect;
            }
            else if (smallRoot != null && smallRoot.activeSelf && smallMapRect != null &&
                     RectTransformUtility.RectangleContainsScreenPoint(smallMapRect, screenPosition, eventCamera))
            {
                activeRect = smallMapRect;
            }

            if (activeRect == null)
                return false;

            // Convert the pointer into local minimap coordinates (centered around zero).
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(activeRect, screenPosition, eventCamera, out Vector2 localPoint))
                return false;

            Rect rect = activeRect.rect;
            if (rect.height <= 0f)
                return false;

            // Translate the local offset into world units, respecting the current zoom/orthographic size.
            float worldUnitsPerPixel = (mapCamera.orthographicSize * 2f) / rect.height;
            Vector3 cameraCenter = mapCamera.transform.position;
            Vector3 offset = new Vector3(localPoint.x * worldUnitsPerPixel, localPoint.y * worldUnitsPerPixel, 0f);
            worldPosition = new Vector3(cameraCenter.x + offset.x, cameraCenter.y + offset.y, 0f);
            return true;
        }

        /// <summary>
        ///     Moves the player and their active pet to the requested minimap location, ensuring saves remain accurate.
        /// </summary>
        private void TeleportPlayerTo(Vector3 worldPosition)
        {
            var mover = cachedPlayerMover;
            if (mover == null)
            {
                GameObject playerObj = target != null ? target.gameObject : GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    // Cache the mover so future teleports do not have to repeat the lookup.
                    mover = playerObj.GetComponent<PlayerMover>();
                    target = playerObj.transform;
                    cachedPlayerMover = mover;
                }
            }

            if (mover == null)
            {
                Debug.LogWarning("Minimap debug teleport requested but no PlayerMover could be found.");
                return;
            }

            // Halt any ongoing locomotion so we do not carry momentum into the new position.
            mover.StopMovement();

            Transform playerTransform = mover.transform;
            Vector3 currentPosition = playerTransform.position;
            // Preserve the existing Z value so sprite sorting layers remain correct.
            Vector3 newPosition = new Vector3(worldPosition.x, worldPosition.y, currentPosition.z);
            playerTransform.position = newPosition;

            GameObject pet = PetDropSystem.ActivePetObject;
            if (pet != null)
            {
                // Drop the pet alongside the player so it resumes following without a sudden snap.
                Vector3 petPosition = newPosition + Vector3.right * 0.5f;
                petPosition.z = pet.transform.position.z;
                pet.transform.position = petPosition;

                var follower = pet.GetComponent<PetFollower>();
                if (follower != null)
                    follower.SetPlayer(playerTransform);
            }

            // Persist the new location to keep autosaves and relogging in sync with the teleport.
            mover.SavePosition();
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
                var eq = Object.FindObjectOfType<Inventory.Equipment>();
                if (eq != null && eq.IsOpen)
                    eq.CloseUI();
            }

            expandedRoot.SetActive(opening);
            if (smallRoot != null)
                smallRoot.SetActive(!opening);

            if (!opening)
            {
                ResetSmallMapZoom();
                dragOffset = Vector3.zero;
            }

            var playerObj = target != null ? target.gameObject : GameObject.FindGameObjectWithTag("Player");
            var mover = playerObj != null ? playerObj.GetComponent<PlayerMover>() : null;
            if (mover != null)
            {
                if (opening)
                    mover.StopMovement();
                mover.enabled = !opening;
            }
        }

        public bool IsExpanded => expandedRoot != null && expandedRoot.activeSelf;

        public void CloseExpanded()
        {
            if (IsExpanded)
                ToggleExpanded();
        }

        public void Register(MinimapMarker marker)
        {
            if (marker == null)
                return;

            if (!markers.Contains(marker))
                markers.Add(marker);

            ValidateManualIcons(marker);
        }

        private void TearDownResources()
        {
            if (mapTexture != null)
            {
                mapTexture.Release();
                mapTexture = null;
            }

            mapCamera = null;
            minimapCanvas = null;
            expandedRoot = null;
            smallRoot = null;
            smallMapRect = null;
            expandedMapRect = null;
            borderRect = null;
            smallRootRect = null;
            target = null;
            markers.Clear();
            iconCache.Clear();
        }

        private void ValidateManualIcons(MinimapMarker marker)
        {
            if (marker == null)
                return;

            if (smallMapRect != null && marker.SmallIcon == null)
            {
                Debug.LogWarning(
                    $"Minimap marker '{marker.name}' is missing a small icon assignment and will not appear on the minimap. " +
                    "Assign a RectTransform reference to the marker so it can be positioned manually.");
            }

            if (expandedMapRect != null && marker.BigIcon == null)
            {
                Debug.LogWarning(
                    $"Minimap marker '{marker.name}' is missing an expanded icon assignment and will not appear on the expanded minimap. " +
                    "Assign a RectTransform reference to the marker so it can be positioned manually.");
            }

            AssignSpriteIfMissing(marker.SmallIcon, marker.type);
            AssignSpriteIfMissing(marker.BigIcon, marker.type);
        }

        private void AssignSpriteIfMissing(RectTransform icon, MinimapMarker.MarkerType type)
        {
            if (icon == null)
                return;

            var image = icon.GetComponent<Image>();
            if (image == null)
                return;

            if (image.sprite == null)
            {
                image.sprite = GetMarkerSprite(type);
                image.preserveAspect = true;
            }
        }

        public void Unregister(MinimapMarker marker)
        {
            if (marker == null)
                return;

            markers.Remove(marker);
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

            bool inside = viewport.x >= 0f && viewport.x <= 1f &&
                          viewport.y >= 0f && viewport.y <= 1f &&
                          viewport.z > 0f;

            if (!inside)
            {
                if (icon.gameObject.activeSelf)
                    icon.gameObject.SetActive(false);
                return;
            }

            if (!icon.gameObject.activeSelf)
                icon.gameObject.SetActive(true);

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
