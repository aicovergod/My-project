using System;
using UnityEngine;
using UnityEngine.UI;
using Inventory;
using Core.Save;
using Skills;

namespace BankSystem
{
    /// <summary>
    /// Simple OSRS-style bank with 400 slots (8x50) generated at runtime.
    /// Allows depositing from the inventory and withdrawing back.
    /// </summary>
    public class BankUI : MonoBehaviour
    {
        public Vector2 slotSize = new Vector2(32f, 32f);
        public Vector2 slotSpacing = new Vector2(4f, 4f);
        public Vector2 referenceResolution = new Vector2(640f, 360f);
        public Color emptySlotColor = new Color(1f, 1f, 1f, 0f);

        public Color windowColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        public Vector2 windowPadding = new Vector2(8f, 8f);
        public float headerHeight = 24f;

        private const int Columns = 8;
        private const int Rows = 50;
        private const int Size = Columns * Rows;
        private const int BankStackLimit = 9999;

        private GameObject uiRoot;
        private Image[] slotImages;
        private Text[] slotCountTexts;
        private InventoryEntry[] items = new InventoryEntry[Size];
        private Inventory.Inventory playerInventory;
        private Font defaultFont;

        private static BankUI instance;
        public static BankUI Instance => instance;

        private const string SaveKey = "BankData";

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var go = new GameObject("Bank");
            DontDestroyOnLoad(go);
            go.AddComponent<BankUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            CreateUI();
            uiRoot.SetActive(false);
            Load();
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("BankUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            uiRoot.transform.SetParent(null, false);
            DontDestroyOnLoad(uiRoot);

            var canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;

            var scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 0f;

            GameObject window = new GameObject("Window", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(uiRoot.transform, false);
            var windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.anchoredPosition = Vector2.zero;
            float width = Columns * slotSize.x + (Columns - 1) * slotSpacing.x + windowPadding.x * 2f + 20f;
            float visibleRows = 8f;
            float height = visibleRows * slotSize.y + (visibleRows - 1f) * slotSpacing.y + windowPadding.y * 2f + headerHeight;
            windowRect.sizeDelta = new Vector2(width, height);
            var windowImg = window.GetComponent<Image>();
            windowImg.color = windowColor;

            GameObject titleGO = new GameObject("Title", typeof(Text));
            titleGO.transform.SetParent(window.transform, false);
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(0f, headerHeight);
            titleRect.anchoredPosition = new Vector2(0f, 0f);
            var titleText = titleGO.GetComponent<Text>();
            titleText.font = defaultFont;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.text = "Bank";

            GameObject closeGO = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGO.transform.SetParent(window.transform, false);
            var closeRect = closeGO.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-4f, -4f);
            closeRect.sizeDelta = new Vector2(16f, 16f);
            var closeImg = closeGO.GetComponent<Image>();
            closeImg.color = Color.red;
            var closeBtn = closeGO.GetComponent<Button>();
            closeBtn.onClick.AddListener(Close);
            GameObject closeTextGO = new GameObject("Text", typeof(Text));
            closeTextGO.transform.SetParent(closeGO.transform, false);
            var closeText = closeTextGO.GetComponent<Text>();
            closeText.font = defaultFont;
            closeText.alignment = TextAnchor.MiddleCenter;
            closeText.color = Color.white;
            closeText.text = "X";
            var closeTextRect = closeTextGO.GetComponent<RectTransform>();
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.offsetMin = Vector2.zero;
            closeTextRect.offsetMax = Vector2.zero;

            GameObject scrollGO = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollGO.transform.SetParent(window.transform, false);
            var scrollRect = scrollGO.GetComponent<RectTransform>();
            float scrollbarWidth = 12f;
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.pivot = new Vector2(0.5f, 0.5f);
            scrollRect.offsetMin = new Vector2(windowPadding.x, windowPadding.y);
            scrollRect.offsetMax = new Vector2(-windowPadding.x - scrollbarWidth, -windowPadding.y - headerHeight);

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scrollGO.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.pivot = new Vector2(0f, 1f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            var maskImg = viewport.GetComponent<Image>();
            maskImg.color = new Color(0f, 0f, 0f, 1f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(0f, 1f);
            contentRect.pivot = new Vector2(0f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(
                Columns * slotSize.x + (Columns - 1) * slotSpacing.x,
                Rows * slotSize.y + (Rows - 1) * slotSpacing.y);

            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = slotSize;
            grid.spacing = slotSpacing;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;

            GameObject scrollbarGO = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarGO.transform.SetParent(window.transform, false);
            var scrollbarRect = scrollbarGO.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-windowPadding.x - scrollbarWidth, windowPadding.y);
            scrollbarRect.offsetMax = new Vector2(-windowPadding.x, -windowPadding.y - headerHeight);
            var scrollbarImg = scrollbarGO.GetComponent<Image>();
            scrollbarImg.color = new Color(0f, 0f, 0f, 0.5f);
            GameObject handleGO = new GameObject("Handle", typeof(Image));
            handleGO.transform.SetParent(scrollbarGO.transform, false);
            var handleImg = handleGO.GetComponent<Image>();
            handleImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var handleRect = handleGO.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            var scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handleImg;
            scrollbar.handleRect = handleRect;

            var scroll = scrollGO.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            slotImages = new Image[Size];
            slotCountTexts = new Text[Size];
            for (int i = 0; i < Size; i++)
            {
                GameObject slot = new GameObject($"Slot{i}", typeof(Image), typeof(BankSlot));
                slot.transform.SetParent(content.transform, false);
                var img = slot.GetComponent<Image>();
                img.sprite = null;
                img.type = Image.Type.Simple;
                img.color = emptySlotColor;
                img.enabled = true;

                GameObject countGO = new GameObject("Count", typeof(Text));
                countGO.transform.SetParent(slot.transform, false);
                var countText = countGO.GetComponent<Text>();
                countText.font = defaultFont;
                countText.alignment = TextAnchor.LowerRight;
                countText.raycastTarget = false;
                countText.color = Color.white;
                var countRect = countGO.GetComponent<RectTransform>();
                countRect.anchorMin = Vector2.zero;
                countRect.anchorMax = Vector2.one;
                countRect.offsetMin = Vector2.zero;
                countRect.offsetMax = Vector2.zero;

                var slotComponent = slot.GetComponent<BankSlot>();
                slotComponent.bank = this;
                slotComponent.index = i;

                slotImages[i] = img;
                slotCountTexts[i] = countText;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }

        private void UpdateSlotVisual(int index)
        {
            var entry = items[index];
            bool hasItem = entry.item != null;

            // Ensure the slot image component is always enabled so changes to
            // the sprite or colour are visible after loading/saving.
            var image = slotImages[index];
            image.enabled = true;

            if (hasItem)
            {
                image.sprite = entry.item.icon;
                image.type = Image.Type.Simple;
                image.color = Color.white;

                slotCountTexts[index].text = entry.count > 1 ? entry.count.ToString() : string.Empty;
                slotCountTexts[index].enabled = entry.count > 1;
            }
            else
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = emptySlotColor;

                slotCountTexts[index].text = string.Empty;
                slotCountTexts[index].enabled = false;
            }
        }

        public void Open()
        {
            if (playerInventory == null)
                playerInventory = FindObjectOfType<Inventory.Inventory>();
            if (playerInventory != null)
            {
                playerInventory.OpenUI();
                playerInventory.BankOpen = true;
            }
            var skills = SkillsUI.Instance;
            if (skills != null && skills.IsOpen)
                skills.Close();
            // Ensure latest saved state is loaded whenever the bank opens
            Load();
            uiRoot.SetActive(true);
        }

        public void Close()
        {
            CompactItems();
            uiRoot.SetActive(false);
            if (playerInventory != null)
                playerInventory.BankOpen = false;
            Save();
        }

        public bool DepositFromInventory(int invIndex)
        {
            if (playerInventory == null)
                return false;
            var entry = playerInventory.GetSlot(invIndex);
            if (entry.item == null)
                return false;
            if (!AddItem(entry.item, entry.count))
                return false;
            playerInventory.ClearSlot(invIndex);
            playerInventory.Save();
            Save();
            return true;
        }

        public bool Withdraw(int bankIndex)
        {
            if (playerInventory == null)
                playerInventory = FindObjectOfType<Inventory.Inventory>();
            if (playerInventory == null)
                return false;
            var entry = items[bankIndex];
            if (entry.item == null)
                return false;
            if (!playerInventory.AddItem(entry.item, entry.count))
                return false;
            items[bankIndex].item = null;
            items[bankIndex].count = 0;
            UpdateSlotVisual(bankIndex);
            playerInventory.Save();
            Save();
            return true;
        }

        private void CompactItems()
        {
            int nextIndex = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].item != null)
                {
                    if (i != nextIndex)
                    {
                        items[nextIndex] = items[i];
                        items[i].item = null;
                        items[i].count = 0;
                        UpdateSlotVisual(nextIndex);
                        UpdateSlotVisual(i);
                    }
                    nextIndex++;
                }
            }
            for (int i = nextIndex; i < items.Length; i++)
                UpdateSlotVisual(i);
        }

        private bool AddItem(ItemData item, int count)
        {
            if (item == null || count <= 0)
                return false;

            int remaining = count;
            int maxStack = BankStackLimit;

            for (int i = 0; i < items.Length && remaining > 0; i++)
            {
                if (items[i].item == item && items[i].count < maxStack)
                {
                    int add = Mathf.Min(maxStack - items[i].count, remaining);
                    items[i].count += add;
                    remaining -= add;
                    UpdateSlotVisual(i);
                }
            }

            for (int i = 0; i < items.Length && remaining > 0; i++)
            {
                if (items[i].item == null)
                {
                    int add = Mathf.Min(maxStack, remaining);
                    items[i].item = item;
                    items[i].count = add;
                    remaining -= add;
                    UpdateSlotVisual(i);
                }
            }
            return remaining <= 0;
        }

        [Serializable]
        private class BankSaveData
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
            var data = new BankSaveData { slots = new SlotData[Size] };
            for (int i = 0; i < Size; i++)
            {
                var entry = items[i];
                data.slots[i] = new SlotData
                {
                    id = entry.item != null ? entry.item.id : string.Empty,
                    count = entry.item != null ? entry.count : 0
                };
            }
            SaveManager.Save(SaveKey, data);
        }

        public void Load()
        {
            var data = SaveManager.Load<BankSaveData>(SaveKey);
            if (data?.slots == null)
                return;
            int len = Mathf.Min(Size, data.slots.Length);
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
        }

        private void OnApplicationQuit()
        {
            CompactItems();
            Save();
        }
    }
}
