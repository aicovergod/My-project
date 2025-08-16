// Assets/Scripts/Inventory/Inventory.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using ShopSystem;
using Player;

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

        [Header("Window")]
        [Tooltip("Background color for the inventory window.")]
        public Color windowColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        [Tooltip("Padding around the slot grid inside the window.")]
        public Vector2 windowPadding = new Vector2(8f, 8f);
        [Tooltip("Fixed width and height for the inventory window background.")]
        public Vector2 windowSize = new Vector2(83f, 375f);

        [Header("Tooltip")]
        [Tooltip("Optional: custom font for the tooltip item name. Uses Arial if null.")]
        public Font tooltipNameFont;
        [Tooltip("Color for the tooltip item name text.")]
        public Color tooltipNameColor = Color.white;
        [Tooltip("Optional: custom font for the tooltip description. Uses Arial if null.")]
        public Font tooltipDescriptionFont;
        [Tooltip("Color for the tooltip description text.")]
        public Color tooltipDescriptionColor = Color.white;
        
        private Image[] slotImages;
        private Text[] slotCountTexts;
        private InventoryEntry[] items;

        private GameObject tooltip;
        private Text tooltipNameText;
        private Text tooltipDescriptionText;

        // Active shop context when interacting with a shop
        private Shop currentShop;

        private PlayerMover playerMover;

        // UI
        private GameObject uiRoot; // Canvas root
        private static GameObject sharedUIRoot;

        // Drag & drop
        private int draggingIndex = -1;
        private GameObject draggingIcon;

        // Cached default font to avoid repeated builtin lookups that may throw
        private Font defaultFont;

        public event Action OnInventoryChanged;

        private bool CanDropItems => playerMover == null || playerMover.CanDrop;

        private void Awake()
        {
            // Ensure at least one slot and cache the builtin font once
            size = Mathf.Max(1, size);

            // Unity 2022+ renamed the builtin Arial font. Attempt to load the new
            // name first and fall back for older Unity versions to avoid
            // runtime exceptions that would prevent the UI from being created.
            try
            {
                defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.ArgumentException)
            {
                // ignored: will fall back below
            }

            if (defaultFont == null)
            {
                try
                {
                    defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch (System.ArgumentException)
                {
                    defaultFont = null;
                }
            }

            items = new InventoryEntry[size];
            EnsureLegacyEventSystem();

            if (sharedUIRoot != null)
            {
                uiRoot = sharedUIRoot;
            }
            else
            {
                CreateUI();
                sharedUIRoot = uiRoot;
            }

            playerMover = GetComponent<PlayerMover>();

            // Start completely hidden (inactive object so it’s clear in Hierarchy)
            if (uiRoot != null)
                uiRoot.SetActive(false);

            // Restore any previously saved inventory state
            Load();
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
            windowRect.anchorMin = new Vector2(0f, 1f);
            windowRect.anchorMax = new Vector2(0f, 1f);
            windowRect.pivot = new Vector2(0f, 1f);
            windowRect.anchoredPosition = new Vector2(10f - windowPadding.x, -10f + windowPadding.y);
            windowRect.sizeDelta = windowSize;

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
            grid.constraintCount = 2;

            // Generate visible slot images
            slotImages = new Image[size];
            slotCountTexts = new Text[size];

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

                    // Add quantity text
                    GameObject countGO = new GameObject("Count", typeof(Text));
                    countGO.transform.SetParent(slot.transform, false);
                    var countText = countGO.GetComponent<Text>();
                    if (defaultFont != null)
                        countText.font = defaultFont;
                    countText.alignment = TextAnchor.LowerRight;
                    countText.raycastTarget = false;
                    countText.color = Color.white;
                    countText.text = string.Empty;
                    var countRect = countGO.GetComponent<RectTransform>();
                    countRect.anchorMin = Vector2.zero;
                    countRect.anchorMax = Vector2.one;
                    countRect.offsetMin = Vector2.zero;
                    countRect.offsetMax = Vector2.zero;

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

            // Force a layout rebuild so slots are positioned before the UI is hidden
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            windowRect.sizeDelta = windowSize;

            // Tooltip setup
            tooltip = new GameObject("Tooltip", typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            tooltip.transform.SetParent(uiRoot.transform, false);
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
        }

        private void UpdateSlotVisual(int index)
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
                    slotCountTexts[index].text = entry.count > 1 ? entry.count.ToString() : string.Empty;
                    slotCountTexts[index].enabled = true;
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
        }

        /// <summary>
        /// Adds an item, stacking when possible. Returns true if the entire
        /// quantity could be added.
        /// </summary>
        public bool AddItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0)
                return false;

            if (!CanAddItem(item, quantity))
                return false;

            int remaining = quantity;

            if (item.stackable)
            {
                for (int i = 0; i < items.Length && remaining > 0; i++)
                {
                    if (items[i].item == item && items[i].count < item.maxStack)
                    {
                        int add = Mathf.Min(item.maxStack - items[i].count, remaining);
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
                    items[i].count = item.stackable ? Mathf.Min(item.maxStack, remaining) : 1;
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

            int space = 0;

            if (item.stackable)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].item == item)
                        space += item.maxStack - items[i].count;
                    else if (items[i].item == null)
                        space += item.maxStack;

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

        /// <summary>
        /// Drops a quantity of the item from the specified slot.
        /// </summary>
        public void DropItem(int slotIndex, int quantity = 1)
        {
            if (!CanDropItems) return;
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
            if (slotIndex < 0 || slotIndex >= items.Length) return;
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
                tooltip.transform.position = slotRect.position + new Vector3(slotSize.x, 0f, 0f);
                tooltip.SetActive(true);
                return;
            }

            string name = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            tooltipNameText.text = name;
            tooltipDescriptionText.text = item.description;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

            tooltip.transform.position = slotRect.position + new Vector3(slotSize.x, 0f, 0f);
            tooltip.SetActive(true);
        }

        public void HideTooltip()
        {
            if (tooltip != null)
                tooltip.SetActive(false);
        }

        /// <summary>
        /// Attempts to sell a quantity of the item at the given slot index to
        /// the current shop.
        /// </summary>
        public void SellItem(int slotIndex, int quantity = 1)
        {
            if (currentShop == null)
                return;
            if (slotIndex < 0 || slotIndex >= items.Length)
                return;
            var item = items[slotIndex].item;
            if (item == null)
                return;

            int sold = 0;
            for (int i = 0; i < quantity; i++)
            {
                if (currentShop.Sell(item, this))
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
            if (slotIndex < 0 || slotIndex >= items.Length) return;
            var entry = items[slotIndex];
            var item = entry.item;
            if (item == null) return;

            HideTooltip();
            draggingIndex = slotIndex;

            draggingIcon = new GameObject("DraggingIcon", typeof(Image));
            draggingIcon.transform.SetParent(uiRoot.transform, false);
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
            if (draggingIcon != null)
                draggingIcon.transform.position = eventData.position;
        }

        public void Drop(int slotIndex)
        {
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
            if (draggingIndex != -1)
                UpdateSlotVisual(draggingIndex);

            if (draggingIcon != null)
                Destroy(draggingIcon);

            draggingIcon = null;
            draggingIndex = -1;
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

        private const string SaveKey = "InventoryData";

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

            string json = JsonUtility.ToJson(data);
            PlayerPrefs.SetString(SaveKey, json);
            PlayerPrefs.Save();
        }

        public void Load()
        {
            if (!PlayerPrefs.HasKey(SaveKey))
                return;

            string json = PlayerPrefs.GetString(SaveKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            var data = JsonUtility.FromJson<InventorySaveData>(json);
            if (data?.slots == null)
                return;

            int len = Mathf.Min(size, data.slots.Length);
            for (int i = 0; i < len; i++)
            {
                var slot = data.slots[i];
                if (!string.IsNullOrEmpty(slot.id))
                {
                    var item = Resources.Load<ItemData>("Item/" + slot.id);
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
#if ENABLE_INPUT_SYSTEM
            bool toggle = Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame;
            toggle |= Input.GetKeyDown(KeyCode.I);
#else
            bool toggle = Input.GetKeyDown(KeyCode.I);
#endif
            if (currentShop != null)
            {
                if (uiRoot != null && !uiRoot.activeSelf)
                    uiRoot.SetActive(true);
                return;
            }
            if (toggle && uiRoot != null)
                uiRoot.SetActive(!uiRoot.activeSelf);
        }

        /// <summary>
        /// Ensure a legacy EventSystem exists for uGUI with StandaloneInputModule.
        /// </summary>
        private static void EnsureLegacyEventSystem()
        {
            var existing = UnityEngine.Object.FindObjectOfType<EventSystem>();
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
