// Assets/Scripts/Inventory/Inventory.cs
using System;
using UnityEngine;
using Core.Save;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using ShopSystem;
using Player;
using Skills;
using Pets;
using Quests;
using UI;
using Books;
using Object = UnityEngine.Object;

namespace Inventory
{
    public struct InventoryEntry
    {
        public ItemData item;
        public int count;
    }

    /// <summary>
    /// Indicates how a stack split action should be handled.
    /// </summary>
    public enum StackSplitType
    {
        Drop,
        Sell,
        Split
    }

    /// <summary>
    /// Runtime inventory UI generator (Screen Space Overlay) that toggles with the OLD
    /// Input Manager (Input.GetKeyDown). The UI is created at scene root, starts inactive,
    /// and shows always-visible slot squares. If a slotFrameSprite is provided, it is used
    /// as the slot frame (set to Sliced).
    /// </summary>
    [DisallowMultipleComponent]
    public class Inventory : MonoBehaviour, IUIWindow
    {
        [Header("Inventory")]
        [Tooltip("Maximum number of items the inventory can hold.")]
        public int size = 20;

        [Header("UI Layout")]
        [Tooltip("Slot size in UI pixels.")]
        public Vector2 slotSize = new Vector2(32, 32);
        [Tooltip("Spacing between slots in UI pixels.")]
        public Vector2 slotSpacing = new Vector2(4, 4);
        [Tooltip("Reference resolution for Canvas Scaler.")]
        public Vector2 referenceResolution = new Vector2(1024f, 768f);
        [Tooltip("Number of columns in the slot grid.")]
        public int columns = 2;
        [Tooltip("Reuse a shared UI root across multiple inventories.")]
        public bool useSharedUIRoot = true;

        [Header("Empty Slot Look")]
        [Tooltip("Optional: frame sprite (9-sliced) to draw for each slot.")]
        public Sprite slotFrameSprite;
        [Tooltip("Color/tint for empty slots if no frame sprite, or tint over the frame.")]
        public Color emptySlotColor = new Color(0f, 0f, 0f, 1f); // solid black

        [Header("Stack Count Colors")]
        [Tooltip("Color used for stack counts below 10,000.")]
        public Color stackColorDefault = Color.yellow;
        [Tooltip("Color used for stack counts of 10,000 or more.")]
        public Color stackColor10k = Color.white;
        [Tooltip("Color used for stack counts of 100,000 or more.")]
        public Color stackColor100k = Color.green;
        [Tooltip("Color used for stack counts of 10,000,000 or more.")]
        public Color stackColor10m = Color.cyan;
        [Tooltip("Color used for stack counts of 100,000,000 or more.")]
        public Color stackColor100m = Color.magenta;

        [Tooltip("Optional: custom font for stack count text. Uses LegacyRuntime if null.")]
        public Font stackCountFont;

        [Tooltip("Font size for stack count text.")]
        public int stackCountFontSize = 12;

        [Header("Window")]
        [Tooltip("Background color for the inventory window.")]
        public Color windowColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        [Tooltip("Padding around the slot grid inside the window.")]
        public Vector2 windowPadding = new Vector2(8f, 8f);
        [Tooltip("Fixed width and height for the inventory window background.")]
        public Vector2 windowSize = new Vector2(83f, 375f);
        [Tooltip("Show a close button in the top-right corner.")]
        public bool showCloseButton;

        [Tooltip("Center the inventory window on screen instead of anchoring to the top-left.")]
        public bool centerOnScreen;

        [Tooltip("Anchored position of the inventory window.")]
        public Vector2 windowPosition = new Vector2(480f, -170f);

        [Header("Tooltip")]
        [Tooltip("Optional: custom font for the tooltip item name. Uses LegacyRuntime if null.")]
        public Font tooltipNameFont;
        [Tooltip("Color for the tooltip item name text.")]
        public Color tooltipNameColor = Color.white;
        [Tooltip("Optional: custom font for the tooltip description. Uses LegacyRuntime if null.")]
        public Font tooltipDescriptionFont;
        [Tooltip("Color for the tooltip description text.")]
        public Color tooltipDescriptionColor = new Color(184/255f, 134/255f, 11/255f, 1f);

        [Header("Save")]
        [Tooltip("Save key used for persistence.")]
        public string saveKey = "InventoryData";

        [Header("Combination")]
        [Tooltip("Database of valid item combinations.")]
        public ItemCombinationDatabase combinationDatabase;

        private Image[] slotImages;
        private Text[] slotCountTexts;
        private InventoryEntry[] items;
        public int selectedIndex = -1;
        private Image[] slotHighlights;
        private Material highlightMaterial;

        private GameObject tooltip;
        private Text tooltipNameText;
        private Text tooltipDescriptionText;
        private InventoryDropMenu dropMenu;

        // Active shop context when interacting with a shop
        private Shop currentShop;

        private PlayerMover playerMover;
        private Equipment equipment;

        // UI
        private GameObject uiRoot; // Canvas root
        private static GameObject sharedUIRoot;

        // Drag & drop
        private int draggingIndex = -1;
        private GameObject draggingIcon;
        private static Inventory draggingInventory;

        // Cached default font to avoid repeated builtin lookups that may throw
        private Font defaultFont;

        public event Action OnInventoryChanged;

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        public bool BankOpen { get; set; }
        public bool InShop => currentShop != null;

        private bool CanDropItems => playerMover == null || playerMover.CanDrop;

        public void CloseUI()
        {
            if (BankOpen)
                return;
            if (uiRoot != null)
                uiRoot.SetActive(false);
            HideDropMenu();
            if (playerMover != null)
            {
                var pet = PetDropSystem.ActivePetObject;
                var storage = pet != null ? pet.GetComponent<PetStorage>() : null;
                storage?.Close();
            }
        }

        public void Close()
        {
            CloseUI();
        }

        public void OpenUI()
        {
            if (!BankOpen && !InShop && useSharedUIRoot)
                UIManager.Instance.OpenWindow(this);
            if (uiRoot != null)
                uiRoot.SetActive(true);
            if (playerMover != null)
            {
                var pet = PetDropSystem.ActivePetObject;
                var storage = pet != null ? pet.GetComponent<PetStorage>() : null;
                if (!BankOpen)
                {
                    if (PetDropSystem.PetInventoryVisible)
                        storage?.Open();
                    else
                        storage?.Close();
                }
                else
                    storage?.Close();
            }
        }

        public InventoryEntry GetSlot(int index)
        {
            return index >= 0 && index < items.Length ? items[index] : default;
        }

        public void ClearSlot(int index)
        {
            if (index < 0 || index >= items.Length)
                return;
            items[index].item = null;
            items[index].count = 0;
            UpdateSlotVisual(index);
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Equip the item in the given inventory slot if possible.
        /// </summary>
        public bool EquipItem(int index)
        {
            if (equipment == null)
                return false;
            if (index < 0 || index >= items.Length)
                return false;
            var entry = items[index];
            if (entry.item == null || entry.item.equipmentSlot == EquipmentSlot.None)
                return false;

            // Temporarily free the slot before attempting to equip.
            items[index] = default;
            UpdateSlotVisual(index);

            // Try to equip the item.
            if (equipment.Equip(entry))
            {
                OnInventoryChanged?.Invoke();
                return true;
            }

            // Equipping failed. Restore the original item.
            items[index] = entry;
            UpdateSlotVisual(index);
            OnInventoryChanged?.Invoke();
            return false;
        }

        /// <summary>
        /// Use the item in the given slot if it supports usage.
        /// Opens books or consumes food items.
        /// </summary>
        public bool UseItem(int index)
        {
            if (index < 0 || index >= items.Length)
                return false;

            var entry = items[index];
            var item = entry.item;

            if (item is BookItemData bookItem && bookItem.book != null)
            {
                BookUI.Instance.Open(bookItem.book);
                return true;
            }

            var eater = GetComponent<PlayerEat>();
            if (eater != null && item != null && item.healAmount > 0)
            {
                if (eater.Eat(item))
                {
                    if (!string.IsNullOrEmpty(item.replacementItemId))
                    {
                        var next = ItemDatabase.GetItem(item.replacementItemId);
                        items[index].item = next;
                        items[index].count = next != null ? 1 : 0;
                    }
                    else
                    {
                        items[index].count--;
                        if (items[index].count <= 0)
                            items[index].item = null;
                    }
                    UpdateSlotVisual(index);
                    OnInventoryChanged?.Invoke();
                    ItemUseResolver.NotifyItemUsed(gameObject, item, ItemUseType.Consumed);
                    return true;
                }
            }

            return false;
        }

        private void Start()
        {
            // Ensure at least one slot and cache the builtin font once
            size = Mathf.Max(1, size);

            try
            {
                defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.ArgumentException)
            {
                defaultFont = null;
            }

            stackCountFont = Resources.Load<Font>("ThaleahFat_TTF") ??
                             Resources.Load<Font>("ThaleahFAT_TTF") ??
                             stackCountFont ?? defaultFont;

            items = new InventoryEntry[size];

            if (EventSystem.current == null)
                EnsureEventSystem();

            if (useSharedUIRoot && sharedUIRoot != null)
            {
                uiRoot = sharedUIRoot;
            }
            else
            {
                CreateUI();
                if (useSharedUIRoot)
                    sharedUIRoot = uiRoot;
            }

            playerMover = GetComponent<PlayerMover>();
            equipment = GetComponent<Equipment>();

            if (uiRoot != null)
                uiRoot.SetActive(false);

            Load();
            UIManager.Instance.RegisterWindow(this);
        }

        /// <summary>
        /// Creates a Screen Space Overlay canvas and a simple grid of slots.
        /// Slots sit inside a window with a colored background similar to the shop UI.
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

            GameObject window = new GameObject("Window", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(uiRoot.transform, false);

            var windowRect = window.GetComponent<RectTransform>();
            if (centerOnScreen)
            {
                windowRect.anchorMin = new Vector2(0.5f, 0.5f);
                windowRect.anchorMax = new Vector2(0.5f, 0.5f);
                windowRect.pivot = new Vector2(0.5f, 0.5f);
                windowRect.anchoredPosition = Vector2.zero;
            }
            else
            {
                windowRect.anchorMin = new Vector2(0f, 1f);
                windowRect.anchorMax = new Vector2(0f, 1f);
                windowRect.pivot = new Vector2(0f, 1f);
                windowRect.anchoredPosition = windowPosition;
            }

            var windowImg = window.GetComponent<Image>();
            windowImg.color = windowColor;

            GameObject panel = new GameObject("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
            panel.transform.SetParent(window.transform, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(windowPadding.x, -windowPadding.y);

            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = slotSize;
            grid.spacing = slotSpacing;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, columns);

            // Generate visible slot images
            slotImages = new Image[size];
            slotCountTexts = new Text[size];
            slotHighlights = new Image[size];

            try
            {
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

                    // Add highlight image
                    GameObject highlightGO = new GameObject("Highlight", typeof(Image));
                    highlightGO.transform.SetParent(slot.transform, false);
                    var highlightImg = highlightGO.GetComponent<Image>();
                    highlightImg.sprite = null;
                    // Use full alpha so the outline shader can render properly
                    highlightImg.color = new Color(1f, 1f, 1f, 1f);
                    highlightImg.type = Image.Type.Simple;
                    highlightImg.raycastTarget = false;
                    if (highlightMaterial == null)
                    {
                        var shader = Shader.Find("Custom/SpriteOutline");
                        if (shader != null)
                        {
                            highlightMaterial = new Material(shader);
                            highlightMaterial.SetColor("_OutlineColor", Color.yellow);
                        }
                    }
                    highlightImg.material = highlightMaterial;
                    var hlRect = highlightGO.GetComponent<RectTransform>();
                    hlRect.anchorMin = Vector2.zero;
                    hlRect.anchorMax = Vector2.one;
                    hlRect.offsetMin = Vector2.zero;
                    hlRect.offsetMax = Vector2.zero;
                    highlightImg.enabled = false;
                    slotHighlights[i] = highlightImg;

                    // Add quantity text
                    GameObject countGO = new GameObject("Count", typeof(Text));
                    countGO.transform.SetParent(slot.transform, false);
                    var countText = countGO.GetComponent<Text>();
                    var outline = countGO.AddComponent<Outline>();
                    outline.effectColor = Color.black;
                    outline.effectDistance = new Vector2(1f, -1f);
                    outline.useGraphicAlpha = false;
                    countText.font = stackCountFont ?? defaultFont;
                    countText.fontSize = stackCountFontSize;
                    countText.alignment = TextAnchor.UpperLeft;
                    countText.raycastTarget = false;
                    countText.color = Color.white;
                    countText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    countText.text = string.Empty;
                    var countRect = countGO.GetComponent<RectTransform>();
                    countRect.anchorMin = new Vector2(0f, 1f);
                    countRect.anchorMax = new Vector2(0f, 1f);
                    countRect.pivot = new Vector2(0f, 1f);
                    countRect.offsetMin = new Vector2(2f, -16f);
                    countRect.offsetMax = new Vector2(16f, -2f);

                    // Add hover handler
                    var slotComponent = slot.AddComponent<InventorySlot>();
                    if (slotComponent != null)
                    {
                        slotComponent.inventory = this;
                        slotComponent.index = i;
                    }

                    slotImages[i] = img;
                    slotCountTexts[i] = countText;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Inventory UI generation failed: {ex}");
            }

            int rows = Mathf.CeilToInt((float)size / Mathf.Max(1, columns));
            windowSize = new Vector2(columns * slotSize.x + (columns - 1) * slotSpacing.x + windowPadding.x * 2f,
                rows * slotSize.y + (rows - 1) * slotSpacing.y + windowPadding.y * 2f);
            rect.sizeDelta = new Vector2(windowSize.x - windowPadding.x * 2f, windowSize.y - windowPadding.y * 2f);

            // Force a layout rebuild so slots are positioned before the UI is hidden
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            windowRect.sizeDelta = windowSize;

            if (showCloseButton)
            {
                GameObject closeBtn = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
                closeBtn.transform.SetParent(window.transform, false);
                var cbRect = closeBtn.GetComponent<RectTransform>();
                cbRect.anchorMin = cbRect.anchorMax = new Vector2(1f, 1f);
                cbRect.pivot = new Vector2(1f, 1f);
                cbRect.anchoredPosition = new Vector2(-4f, -4f);
                cbRect.sizeDelta = new Vector2(16f, 16f);
                var txtGO = new GameObject("X", typeof(Text));
                txtGO.transform.SetParent(closeBtn.transform, false);
                var txt = txtGO.GetComponent<Text>();
                if (defaultFont != null) txt.font = defaultFont;
                txt.text = "X";
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.white;
                txt.raycastTarget = false;
                var txtRect = txtGO.GetComponent<RectTransform>();
                txtRect.anchorMin = Vector2.zero;
                txtRect.anchorMax = Vector2.one;
                txtRect.offsetMin = Vector2.zero;
                txtRect.offsetMax = Vector2.zero;
                closeBtn.GetComponent<Button>().onClick.AddListener(CloseUI);
            }

            // Tooltip setup
            tooltip = new GameObject("Tooltip", typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            tooltip.transform.SetParent(uiRoot.transform, false);

            // Ensure the tooltip is rendered above other interfaces like the bank
            var tooltipCanvas = tooltip.AddComponent<Canvas>();
            tooltipCanvas.overrideSorting = true;
            tooltipCanvas.sortingOrder = 1000;
            tooltip.AddComponent<GraphicRaycaster>();

            var bg = tooltip.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.75f);
            bg.raycastTarget = false;

            var layout = tooltip.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 2f;

            var fitter = tooltip.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var nameGO = new GameObject("Name", typeof(Text));
            nameGO.transform.SetParent(tooltip.transform, false);
            tooltipNameText = nameGO.GetComponent<Text>();
            tooltipNameText.font = tooltipNameFont != null
                ? tooltipNameFont
                : defaultFont;
            tooltipNameText.alignment = TextAnchor.UpperLeft;
            tooltipNameText.color = tooltipNameColor;
            tooltipNameText.raycastTarget = false;
            tooltipNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipNameText.verticalOverflow = VerticalWrapMode.Overflow;

            var descGO = new GameObject("Description", typeof(Text));
            descGO.transform.SetParent(tooltip.transform, false);
            tooltipDescriptionText = descGO.GetComponent<Text>();
            tooltipDescriptionText.font = tooltipDescriptionFont != null
                ? tooltipDescriptionFont
                : defaultFont;
            tooltipDescriptionText.alignment = TextAnchor.UpperLeft;
            tooltipDescriptionText.color = tooltipDescriptionColor;
            tooltipDescriptionText.raycastTarget = false;
            tooltipDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipDescriptionText.verticalOverflow = VerticalWrapMode.Overflow;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            tooltipRect.pivot = new Vector2(0f, 1f);

            tooltip.SetActive(false);

            dropMenu = InventoryDropMenu.Create(uiRoot.transform, stackCountFont);
        }

        private string FormatStackCount(int count, out Color color)
        {
            if (count < 10000)
            {
                color = stackColorDefault;
                return count.ToString();
            }

            if (count >= 1000000000)
            {
                color = stackColor100m;
                return (count / 1000000000) + "b";
            }

            if (count >= 100000000)
            {
                color = stackColor100m;
                return (count / 1000000) + "m";
            }

            if (count >= 10000000)
            {
                color = stackColor10m;
                return (count / 1000000) + "m";
            }

            if (count >= 1000000)
            {
                color = stackColor100k;
                return (count / 1000000) + "m";
            }

            if (count >= 100000)
            {
                color = stackColor100k;
                return (count / 1000) + "k";
            }

            // count between 10,000 and 99,999
            color = stackColor10k;
            return (count / 1000) + "k";
        }

        public void UpdateSlotVisual(int index)
        {
            if (slotImages == null || index < 0 || index >= slotImages.Length || slotImages[index] == null)
                return;

            var entry = items[index];
            var item = entry.item;
            if (item != null)
            {
                slotImages[index].sprite = item.icon ? item.icon : slotFrameSprite;
                slotImages[index].type = (slotImages[index].sprite == slotFrameSprite && slotFrameSprite != null)
                    ? Image.Type.Sliced : Image.Type.Simple;
                slotImages[index].color = Color.white;
                slotImages[index].enabled = true;
                if (slotCountTexts != null && slotCountTexts.Length > index && slotCountTexts[index] != null)
                {
                    if (entry.count > 1)
                    {
                        Color color;
                        slotCountTexts[index].text = FormatStackCount(entry.count, out color);
                        slotCountTexts[index].color = color;
                        slotCountTexts[index].enabled = true;
                    }
                    else
                    {
                        slotCountTexts[index].text = string.Empty;
                        slotCountTexts[index].enabled = false;
                    }
                }
            }
            else
            {
                slotImages[index].sprite = slotFrameSprite;
                slotImages[index].type = (slotFrameSprite != null) ? Image.Type.Sliced : Image.Type.Simple;
                slotImages[index].color = emptySlotColor;
                slotImages[index].enabled = true;
                if (slotCountTexts != null && slotCountTexts.Length > index && slotCountTexts[index] != null)
                {
                    slotCountTexts[index].text = string.Empty;
                    slotCountTexts[index].enabled = false;
                }
            }

            if (slotHighlights != null && slotHighlights.Length > index && slotHighlights[index] != null)
            {
                var highlight = slotHighlights[index];
                highlight.sprite = slotImages[index].sprite;
                highlight.type = Image.Type.Simple;
                // Ensure the outline image has an opaque color; the outline shader
                // will handle hiding the fill while keeping the border visible
                highlight.color = new Color(1f, 1f, 1f, 1f);
                if (highlightMaterial != null)
                {
                    highlight.material = highlightMaterial;
                    highlightMaterial.SetColor("_OutlineColor", Color.yellow);
                }
                highlight.enabled = (selectedIndex == index);
            }
        }

        /// <summary>
        /// Adds an item, stacking when possible. Returns true if the entire
        /// quantity could be added.
        /// </summary>
        public bool AddItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0)
                return false;

            var petStorage = GetComponent<PetStorage>();
            if (petStorage != null && !petStorage.CanStore(item))
                return false;

            if (!CanAddItem(item, quantity))
                return false;

            int remaining = quantity;

            if (item.stackable)
            {
                for (int i = 0; i < items.Length && remaining > 0; i++)
                {
                    if (items[i].item == item && items[i].count < item.MaxStack)
                    {
                        int add = Mathf.Min(item.MaxStack - items[i].count, remaining);
                        items[i].count += add;
                        remaining -= add;
                        UpdateSlotVisual(i);
                    }
                }
            }

            for (int i = 0; i < items.Length && remaining > 0; i++)
            {
                if (items[i].item == null)
                {
                    items[i].item = item;
                    items[i].count = item.stackable ? Mathf.Min(item.MaxStack, remaining) : 1;
                    remaining -= items[i].count;
                    UpdateSlotVisual(i);
                }
            }

            bool success = remaining <= 0;
            if (success)
                OnInventoryChanged?.Invoke();
            return success;
        }

        /// <summary>
        /// Returns the total number of a given item currently in the inventory.
        /// </summary>
        public int GetItemCount(ItemData item)
        {
            int count = 0;
            if (item == null) return count;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].item == item)
                    count += items[i].count;
            }
            return count;
        }

        /// <summary>
        /// Returns true if there is room to add the specified quantity of the
        /// given item.
        /// </summary>
        public bool CanAddItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0)
                return false;

            var petStorage = GetComponent<PetStorage>();
            if (petStorage != null && !petStorage.CanStore(item))
                return false;

            int space = 0;

            if (item.stackable)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].item == item)
                        space += item.MaxStack - items[i].count;
                    else if (items[i].item == null)
                        space += item.MaxStack;

                    if (space >= quantity)
                        return true;
                }
            }
            else
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].item == null)
                        space++;
                    if (space >= quantity)
                        return true;
                }
            }

            return space >= quantity;
        }

        /// <summary>
        /// Removes up to 'count' of the specified item from the inventory.
        /// Returns true if the requested amount was removed.
        /// </summary>
        public bool RemoveItem(ItemData item, int count)
        {
            if (item == null || count <= 0)
                return false;

            for (int i = 0; i < items.Length && count > 0; i++)
            {
                if (items[i].item == item)
                {
                    int remove = Mathf.Min(count, items[i].count);
                    items[i].count -= remove;
                    count -= remove;
                    if (items[i].count <= 0)
                        items[i].item = null;
                    UpdateSlotVisual(i);
                }
            }

            bool success = count <= 0;
            if (success)
                OnInventoryChanged?.Invoke();
            return success;
        }

        /// <summary>
        /// Checks whether the inventory contains an item by ID.
        /// </summary>
        public bool HasItem(string id)
        {
            foreach (var entry in items)
            {
                if (entry.item != null && entry.item.id == id)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the first occurrence of an item with the given ID.
        /// Returns true if an item was removed.
        /// </summary>
        public bool RemoveItem(string id)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].item != null && items[i].item.id == id)
                {
                    items[i].count--;
                    if (items[i].count <= 0)
                        items[i].item = null;
                    UpdateSlotVisual(i);

                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }

            return false;
        }

        public bool CombineItems(int srcIndex, int dstIndex)
        {
            if (combinationDatabase == null)
                return false;
            if (srcIndex < 0 || dstIndex < 0 || srcIndex >= items.Length || dstIndex >= items.Length)
                return false;

            var srcItem = items[srcIndex].item;
            var dstItem = items[dstIndex].item;
            if (srcItem == null || dstItem == null)
                return false;

            var result = combinationDatabase.GetResult(srcItem, dstItem);
            if (result == null)
                return false;

            items[srcIndex].count--;
            if (items[srcIndex].count <= 0)
                items[srcIndex].item = null;
            UpdateSlotVisual(srcIndex);

            items[dstIndex].count--;
            if (items[dstIndex].count <= 0)
                items[dstIndex].item = null;
            UpdateSlotVisual(dstIndex);

            AddItem(result, 1);
            OnInventoryChanged?.Invoke();
            return true;
        }

        public void ClearSelection()
        {
            selectedIndex = -1;
            if (slotHighlights != null)
            {
                for (int i = 0; i < slotHighlights.Length; i++)
                {
                    if (slotHighlights[i] != null)
                        slotHighlights[i].enabled = false;
                }
            }
        }

        /// <summary>
        /// Replaces the item at <paramref name="slotIndex"/> with
        /// <paramref name="newItem"/> and sets its count. The slot must
        /// currently contain <paramref name="oldItem"/>. Returns true on
        /// success.
        /// </summary>
        public bool ReplaceItem(int slotIndex, ItemData oldItem, ItemData newItem, int newCount)
        {
            if (slotIndex < 0 || slotIndex >= items.Length)
                return false;

            var entry = items[slotIndex];
            if (entry.item != oldItem)
                return false;

            entry.item = newItem;
            entry.count = newCount;
            items[slotIndex] = entry;
            UpdateSlotVisual(slotIndex);
            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Drops a quantity of the item from the specified slot.
        /// </summary>
        public void DropItem(int slotIndex, int quantity = 1)
        {
            if (BankOpen || !CanDropItems) return;
            if (slotIndex < 0 || slotIndex >= items.Length) return;
            var entry = items[slotIndex];
            if (entry.item == null) return;
            if (entry.item.isUndroppable) return;

            // Cache the item before modifying the slot so we can check for pets.
            var droppedItem = entry.item;

            // Prevent dropping or swapping pets while merged with a pet.
            var pet = PetDropSystem.FindPetByItem(droppedItem);
            if (pet != null && Beastmaster.PetMergeController.Instance != null &&
                Beastmaster.PetMergeController.Instance.IsMerged)
                return;

            int remove = Mathf.Clamp(quantity, 1, entry.count);
            entry.count -= remove;
            if (entry.count <= 0)
                entry.item = null;
            items[slotIndex] = entry;
            UpdateSlotVisual(slotIndex);
            HideTooltip();
            OnInventoryChanged?.Invoke();

            // Attempt to spawn a pet for this item if one exists.
            if (pet != null)
            {
                // If a different pet is already active, return its pickup item to the inventory
                // before spawning the new one so players don't lose their previous pet item.
                var currentPet = PetDropSystem.ActivePet;
                if (currentPet != null && currentPet != pet && currentPet.pickupItem != null)
                    AddItem(currentPet.pickupItem);

                var player = GameObject.FindGameObjectWithTag("Player");
                Vector3 pos = player != null ? player.transform.position : Vector3.zero;
                Debug.Log($"Dropping pet item '{droppedItem.name}', spawning pet '{pet.displayName}'.");
                PetDropSystem.SpawnPet(pet, pos);
            }
            else
            {
                Debug.Log($"Dropped item '{droppedItem.name}' with no associated pet.");
            }
        }

        /// <summary>
        /// Removes a quantity from the specified slot without dropping it in the world.
        /// </summary>
        public void RemoveFromSlot(int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= items.Length) return;
            var entry = items[slotIndex];
            if (entry.item == null) return;

            int remove = Mathf.Clamp(quantity, 1, entry.count);
            entry.count -= remove;
            if (entry.count <= 0)
                entry.item = null;
            items[slotIndex] = entry;
            UpdateSlotVisual(slotIndex);
            HideTooltip();
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Splits a stack within the inventory, moving <paramref name="quantity"/>
        /// items to a new slot if space is available.
        /// </summary>
        public void SplitStack(int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= items.Length) return;
            var entry = items[slotIndex];
            if (entry.item == null || !entry.item.splittable) return;

            int amount = Mathf.Clamp(quantity, 1, entry.count - 1);
            if (amount <= 0) return;

            int target = -1;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].item == null)
                {
                    target = i;
                    break;
                }
            }

            if (target == -1) return; // no space

            entry.count -= amount;
            items[slotIndex] = entry;
            items[target].item = entry.item;
            items[target].count = amount;

            UpdateSlotVisual(slotIndex);
            UpdateSlotVisual(target);
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Opens the stack split dialog for the specified slot and performs the
        /// desired action with the chosen amount.
        /// </summary>
        public void PromptStackSplit(int slotIndex, StackSplitType type)
        {
            if (BankOpen || slotIndex < 0 || slotIndex >= items.Length) return;
            var entry = items[slotIndex];
            if (entry.item == null || entry.count <= 1) return;
            if (!entry.item.splittable) return;
            if (type == StackSplitType.Drop && !CanDropItems) return;

            StackSplitDialog.Show(uiRoot.transform, entry.count, amount =>
            {
                switch (type)
                {
                    case StackSplitType.Sell:
                        if (currentShop != null)
                            SellItem(slotIndex, amount);
                        else
                            SplitStack(slotIndex, amount);
                        break;
                    case StackSplitType.Drop:
                        DropItem(slotIndex, amount);
                        break;
                    case StackSplitType.Split:
                        SplitStack(slotIndex, amount);
                        break;
                }
            });
        }

        public void ShowTooltip(int slotIndex, RectTransform slotRect)
        {
            if (slotIndex < 0 || slotIndex >= items.Length) return;
            var item = items[slotIndex].item;
            if (item == null || tooltip == null || tooltipNameText == null || tooltipDescriptionText == null) return;

            if (currentShop != null && currentShop.TryGetSellPrice(item, out int sellPrice))
            {
                string currencyName = currentShop.currency != null ? currentShop.currency.itemName : "Coins";
                tooltipNameText.text = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
                tooltipDescriptionText.text = $"Sell for {sellPrice} {currencyName}";

                var tooltipRectSell = tooltip.GetComponent<RectTransform>();
                LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRectSell);

                Vector3 sellPos = slotRect.position + new Vector3(slotSize.x, 0f, 0f);
                Vector3[] sellCorners = new Vector3[4];
                tooltipRectSell.GetWorldCorners(sellCorners);
                float sellWidth = sellCorners[2].x - sellCorners[0].x;
                float sellHeight = sellCorners[2].y - sellCorners[0].y;
                sellPos.x = Mathf.Min(sellPos.x, Screen.width - sellWidth);
                sellPos.y = Mathf.Max(sellPos.y, sellHeight);
                tooltipRectSell.position = sellPos;

                tooltip.SetActive(true);
                return;
            }

            string name = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            tooltipNameText.text = name;
            tooltipDescriptionText.text = item.description;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

            Vector3 pos = slotRect.position + new Vector3(slotSize.x, 0f, 0f);
            Vector3[] corners = new Vector3[4];
            tooltipRect.GetWorldCorners(corners);
            float width = corners[2].x - corners[0].x;
            float height = corners[2].y - corners[0].y;
            pos.x = Mathf.Min(pos.x, Screen.width - width);
            pos.y = Mathf.Max(pos.y, height);
            tooltipRect.position = pos;

            tooltip.SetActive(true);
        }

        public void ShowTooltip(ItemData item, RectTransform slotRect)
        {
            if (item == null || tooltip == null || tooltipNameText == null || tooltipDescriptionText == null) return;

            string name = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            tooltipNameText.text = name;
            tooltipDescriptionText.text = item.description;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

            Vector3 pos = slotRect.position + new Vector3(slotSize.x, 0f, 0f);
            Vector3[] corners = new Vector3[4];
            tooltipRect.GetWorldCorners(corners);
            float width = corners[2].x - corners[0].x;
            float height = corners[2].y - corners[0].y;
            pos.x = Mathf.Min(pos.x, Screen.width - width);
            pos.y = Mathf.Max(pos.y, height);
            tooltipRect.position = pos;

            tooltip.SetActive(true);
        }

        public void HideTooltip()
        {
            if (tooltip != null)
                tooltip.SetActive(false);
        }

        public void ShowDropMenu(int slotIndex, Vector2 position)
        {
            HideTooltip();
            var entry = GetSlot(slotIndex);
            if (entry.item != null && entry.item.isUndroppable)
                return;
            dropMenu?.Show(this, slotIndex, position);
        }

        public void HideDropMenu()
        {
            dropMenu?.Hide();
        }

        /// <summary>
        /// Attempts to sell a quantity of the item at the given slot index to
        /// the current shop.
        /// </summary>
        public void SellItem(int slotIndex, int quantity = 1)
        {
            if (BankOpen || currentShop == null)
                return;
            if (slotIndex < 0 || slotIndex >= items.Length)
                return;
            int sold = 0;
            for (int i = 0; i < quantity; i++)
            {
                var item = items[slotIndex].item;
                if (item == null)
                    break;

                if (currentShop.Sell(item, this, slotIndex))
                    sold++;
                else
                    break;
            }

            if (sold > 0)
            {
                HideTooltip();
                OnInventoryChanged?.Invoke();
            }
        }

        /// <summary>
        /// Sets the active shop context used for selling and tooltip information.
        /// </summary>
        public void SetShopContext(Shop shop)
        {
            currentShop = shop;
            if (shop != null && uiRoot != null)
                uiRoot.SetActive(true);
        }

        public void BeginDrag(int slotIndex)
        {
            if (BankOpen || slotIndex < 0 || slotIndex >= items.Length) return;
            var entry = items[slotIndex];
            var item = entry.item;
            if (item == null) return;

            HideTooltip();
            draggingIndex = slotIndex;
            draggingInventory = this;

            draggingIcon = new GameObject("DraggingIcon", typeof(Image), typeof(Canvas));
            var dragCanvas = draggingIcon.GetComponent<Canvas>();
            dragCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            dragCanvas.overrideSorting = true;
            dragCanvas.sortingOrder = short.MaxValue;
            // Ensure the dragged icon renders above all inventory UIs by
            // reparenting it to the shared root and placing it last.
            Transform parent = sharedUIRoot != null ? sharedUIRoot.transform : uiRoot.transform;
            draggingIcon.transform.SetParent(parent, false);
            draggingIcon.transform.SetAsLastSibling();
            var img = draggingIcon.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = item.icon ? item.icon : slotFrameSprite;
            img.color = Color.white;
            var rect = draggingIcon.GetComponent<RectTransform>();
            rect.sizeDelta = slotSize;

            if (slotImages != null && slotIndex < slotImages.Length && slotImages[slotIndex] != null)
                slotImages[slotIndex].enabled = false;
            if (slotCountTexts != null && slotIndex < slotCountTexts.Length && slotCountTexts[slotIndex] != null)
                slotCountTexts[slotIndex].enabled = false;
        }

        public void Drag(PointerEventData eventData)
        {
            if (BankOpen) return;
            if (draggingIcon != null)
                draggingIcon.transform.position = eventData.position;
        }

        public void Drop(int slotIndex)
        {
            if (BankOpen)
            {
                EndDrag();
                return;
            }
            if (draggingInventory != null && draggingInventory != this && draggingInventory.draggingIndex != -1)
            {
                var source = draggingInventory;
                int sourceIndex = source.draggingIndex;
                if (slotIndex >= 0 && slotIndex < items.Length)
                {
                    var petStorage = GetComponent<PetStorage>();
                    if (petStorage != null &&
                        (petStorage.definition?.id == "Heron" ||
                         petStorage.definition?.id == "Beaver" ||
                         petStorage.definition?.id == "Rock Golem"))
                    {
                        // Skilling pets only accept auto-collected resources from their skill and cannot receive manual drops.
                        var entry = source.items[sourceIndex];
                        if (!petStorage.StoreItem(entry.item, entry.count))
                        {
                            source.EndDrag();
                            return;
                        }
                        source.ClearSlot(sourceIndex);
                        source.EndDrag();
                        return;
                    }

                    var temp = items[slotIndex];
                    items[slotIndex] = source.items[sourceIndex];
                    source.items[sourceIndex] = temp;
                    UpdateSlotVisual(slotIndex);
                    source.UpdateSlotVisual(sourceIndex);
                    source.EndDrag();
                    OnInventoryChanged?.Invoke();
                    source.OnInventoryChanged?.Invoke();
                }
                else
                {
                    source.EndDrag();
                }
                return;
            }

            if (draggingIndex == -1)
            {
                EndDrag();
                return;
            }

            if (slotIndex >= 0 && slotIndex < items.Length)
            {
                if (slotIndex != draggingIndex)
                {
                    var temp = items[slotIndex];
                    items[slotIndex] = items[draggingIndex];
                    items[draggingIndex] = temp;
                    UpdateSlotVisual(slotIndex);
                }

                UpdateSlotVisual(draggingIndex);
            }

            EndDrag();
            OnInventoryChanged?.Invoke();
        }

        public void EndDrag()
        {
            if (BankOpen)
            {
                if (draggingIcon != null)
                    Destroy(draggingIcon);
                draggingIcon = null;
                draggingIndex = -1;
                if (draggingInventory == this)
                    draggingInventory = null;
                return;
            }
            if (draggingIndex != -1)
                UpdateSlotVisual(draggingIndex);

            if (draggingIcon != null)
                Destroy(draggingIcon);

            draggingIcon = null;
            draggingIndex = -1;
            if (draggingInventory == this)
                draggingInventory = null;
        }

        [Serializable]
        private class InventorySaveData
        {
            public SlotData[] slots;
        }

        [Serializable]
        private class SlotData
        {
            public string id;
            public int count;
        }

        public void Save()
        {
            var data = new InventorySaveData
            {
                slots = new SlotData[size]
            };

            for (int i = 0; i < size; i++)
            {
                var entry = items[i];
                data.slots[i] = new SlotData
                {
                    id = entry.item != null ? entry.item.id : string.Empty,
                    count = entry.item != null ? entry.count : 0
                };
            }

            SaveManager.Save(saveKey, data);
        }

        public void Load()
        {
            var data = SaveManager.Load<InventorySaveData>(saveKey);
            if (data?.slots == null)
                return;

            int len = Mathf.Min(size, data.slots.Length);
            for (int i = 0; i < len; i++)
            {
                var slot = data.slots[i];
                if (!string.IsNullOrEmpty(slot.id))
                {
                    var item = ItemDatabase.GetItem(slot.id);
                    items[i].item = item;
                    items[i].count = slot.count;
                }
                else
                {
                    items[i].item = null;
                    items[i].count = 0;
                }
                UpdateSlotVisual(i);
            }

            OnInventoryChanged?.Invoke();
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private void Update()
        {
            bool toggle = false;
            if (playerMover == null)
                return;

            var quest = Object.FindObjectOfType<QuestUI>();
            if (quest != null && quest.IsOpen)
            {
                if (uiRoot != null && uiRoot.activeSelf)
                    CloseUI();
                return;
            }
            if (currentShop != null)
            {
                if (uiRoot != null && !uiRoot.activeSelf)
                    OpenUI();
                return;
            }
            if (BankOpen)
            {
                if (uiRoot != null && !uiRoot.activeSelf)
                    OpenUI();
                return;
            }
            if (toggle && uiRoot != null)
            {
                if (!uiRoot.activeSelf)
                {
                    var skills = SkillsUI.Instance;
                    if (skills != null && skills.IsOpen)
                        skills.Close();
                    OpenUI();
                }
                else
                {
                    CloseUI();
                }
            }
        }

        /// <summary>
        /// Ensure an EventSystem exists for uGUI with the Input System module.
        /// </summary>
        private static void EnsureEventSystem()
        {
            var existing = Object.FindObjectOfType<EventSystem>();
            if (existing != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(null, false);
        }
    }
}
