// Assets/Scripts/Inventory/Inventory.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Inventory
{
    /// <summary>
    /// Runtime inventory UI generator (Screen Space Overlay) that toggles with the OLD
    /// Input Manager (Input.GetKeyDown). The UI is created at scene root, starts inactive,
    /// and shows always-visible slot squares. If a slotFrameSprite is provided, it is used
    /// as the slot frame (set to Sliced).
    /// </summary>
    [DisallowMultipleComponent]
    public class Inventory : MonoBehaviour
    {
        [Header("Inventory")]
        [Tooltip("Maximum number of items the inventory can hold.")]
        public int size = 20;

        [Header("UI Layout")]
        [Tooltip("Slot size in UI pixels.")]
        public Vector2 slotSize = new Vector2(32, 32);
        [Tooltip("Spacing between slots in UI pixels.")]
        public Vector2 slotSpacing = new Vector2(4, 4);
        [Tooltip("Reference resolution for Canvas Scaler (use even numbers).")]
        public Vector2 referenceResolution = new Vector2(640, 360);

        [Header("Empty Slot Look")]
        [Tooltip("Optional: frame sprite (9-sliced) to draw for each slot.")]
        public Sprite slotFrameSprite;
        [Tooltip("Color/tint for empty slots if no frame sprite, or tint over the frame.")]
        public Color emptySlotColor = new Color(1f, 1f, 1f, 0.25f); // light translucent

        private Image[] slotImages;
        private ItemData[] items;

        // UI
        private GameObject uiRoot; // Canvas root

        private void Awake()
        {
            items = new ItemData[size];
            EnsureLegacyEventSystem();
            CreateUI();

            // Start completely hidden (inactive object so it’s clear in Hierarchy)
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        /// <summary>
        /// Creates a Screen Space Overlay canvas and a simple grid of slots.
        /// The grid background has no Image component (no giant rectangle).
        /// Slots themselves have Image components so you can see the squares.
        /// </summary>
        private void CreateUI()
        {
            uiRoot = new GameObject("InventoryUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            // Put UI at the SCENE ROOT so it never behaves like a world object
            uiRoot.transform.SetParent(null, false);
            DontDestroyOnLoad(uiRoot);

            // Optional: assign UI layer if it exists (no error if it doesn't)
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) uiRoot.layer = uiLayer;

            var canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;

            var scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 0f; // match width (nice for 64px tile games)

            // Panel to hold slots (no Image component = no big background)
            GameObject panel = new GameObject("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
            panel.transform.SetParent(uiRoot.transform, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(10f, -10f);

            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = slotSize;
            grid.spacing = slotSpacing;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;

            // Generate visible slot images
            slotImages = new Image[size];
            for (int i = 0; i < size; i++)
            {
                GameObject slot = new GameObject($"Slot{i}", typeof(Image));
                slot.transform.SetParent(panel.transform, false);

                var img = slot.GetComponent<Image>();
                if (slotFrameSprite != null)
                {
                    img.sprite = slotFrameSprite;
                    img.type = Image.Type.Sliced; // use 9-slice if the sprite supports it
                    img.color = emptySlotColor;    // tint
                }
                else
                {
                    // No sprite? Show a simple tinted square.
                    img.sprite = null;
                    img.color = emptySlotColor;
                    // Optional: give it a subtle outline by adding a child Image here if you want.
                }

                // IMPORTANT: keep enabled so you can see the empty slot
                img.enabled = true;

                slotImages[i] = img;
            }
        }

        /// <summary>
        /// Adds an item to the first empty slot. Returns true if added.
        /// </summary>
        public bool AddItem(ItemData item)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null)
                {
                    items[i] = item;

                    if (slotImages != null && i < slotImages.Length && slotImages[i] != null)
                    {
                        // Put the item icon on top of the slot look by replacing/tinting
                        slotImages[i].sprite = item.icon ? item.icon : slotFrameSprite;
                        slotImages[i].type = (slotImages[i].sprite == slotFrameSprite && slotFrameSprite != null)
                            ? Image.Type.Sliced : Image.Type.Simple;
                        slotImages[i].color = Color.white; // show item icon as-is
                        slotImages[i].enabled = true;
                    }

                    return true;
                }
            }

            return false; // inventory full
        }

        /// <summary>
        /// Checks whether the inventory contains an item by ID.
        /// </summary>
        public bool HasItem(string id)
        {
            foreach (var item in items)
            {
                if (item != null && item.id == id)
                    return true;
            }
            return false;
        }

        private void Update()
        {
            // OLD INPUT MANAGER
            if (Input.GetKeyDown(KeyCode.I))
            {
                if (uiRoot != null)
                    uiRoot.SetActive(!uiRoot.activeSelf);
            }
        }

        /// <summary>
        /// Ensure a legacy EventSystem exists for uGUI with StandaloneInputModule.
        /// </summary>
        private static void EnsureLegacyEventSystem()
        {
            var existing = Object.FindObjectOfType<EventSystem>();
            if (existing != null)
            {
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(null, false);
            DontDestroyOnLoad(go);
        }
    }
}
