using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Inventory;
using NPC;
using UI;
using UI.Utilities;
using World;

namespace ShopSystem
{
    /// <summary>
    /// Runtime generated shop UI used to display items for sale.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScenePersistentObject))]
    public class ShopUI : ManagedUiWindow
    {
        [Header("Layout")]
        public Vector2 slotSize = new Vector2(32, 32);
        public Vector2 slotSpacing = new Vector2(4, 4);
        public Vector2 referenceResolution = new Vector2(1024f, 768f);
        public Sprite slotFrameSprite;
        public Color emptySlotColor = new Color(1f, 1f, 1f, 0.25f);

        [Header("Window")]
        public Color windowColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        public Vector2 windowPadding = new Vector2(8f, 8f);
        [Tooltip("Additional height applied to the window so the close button isn't overlapped.")]
        public float extraWindowHeight = 33f;

        [Header("Price Display")]
        public Font priceFont;
        public Color priceColor = Color.white;

        [Header("Inventory")]
        public Inventory.Inventory playerInventory;

        private GameObject uiRoot;
        private static GameObject sharedUIRoot;
        private Image[] slotImages;
        private Text[] slotPriceTexts;
        private Text tooltipText;
        private Text shopNameText;
        private Shop currentShop;
        private NpcWanderer npcMover;
        private bool hasLoggedMissingInventory;
        // Tracks the inventory visibility so we can restore the state when leaving the shop.
        private bool inventoryWasOpenBeforeShop;
        private bool inventoryStateCaptured;
        private readonly PlayerMovementModalLock playerMovementLock = new PlayerMovementModalLock();

        private static ShopUI instance;
        public static ShopUI Instance => instance;

        // Global modal flag used by UIManager to block other windows while trading.
        private static bool shopModalActive;
        public static bool IsShopModalActive => shopModalActive;

        private Shop pendingShop;
        private NpcWanderer pendingNpc;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Attempt to resolve the player's inventory immediately so the shop can hook into it when opened.
            ResolvePlayerInventory(false);

            if (sharedUIRoot == null)
                sharedUIRoot = GameObject.Find("ShopUI");

            if (sharedUIRoot != null && sharedUIRoot.GetComponent<Canvas>() == null)
                sharedUIRoot = null;

            if (sharedUIRoot != null)
            {
                uiRoot = sharedUIRoot;
            }
            else
            {
                CreateUI();
                sharedUIRoot = uiRoot;
            }

            if (uiRoot != null)
            {
                CacheSlotReferences();
                var canvas = uiRoot.GetComponent<Canvas>();
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                {
                    canvas.worldCamera = Camera.main;
                }
                SetWindowRoot(uiRoot);
            }
            RegisterWindow();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
                UnregisterWindow();
            }
        }

        /// <summary>
        /// Opens the UI for the given shop and optionally pauses an NPC's movement.
        /// </summary>
        public void Open(Shop shop, NpcWanderer npcMovement = null)
        {
            if (shop == null)
                return;

            pendingShop = shop;
            pendingNpc = npcMovement;
            shopModalActive = true;
            ForceNextOpenHooks();
            base.Open();

            if (!IsOpen)
            {
                pendingShop = null;
                pendingNpc = null;
                shopModalActive = false;
            }
        }

        /// <summary>
        /// Closes the shop UI.
        /// </summary>
        public override void Close()
        {
            base.Close();
        }

        /// <summary>
        /// Attempt to buy item at slot index.
        /// </summary>
        public void Buy(int index)
        {
            if (currentShop == null || playerInventory == null) return;
            currentShop.Buy(index, playerInventory);
        }

        /// <summary>
        /// Resolves and caches the player's inventory component so the shop can manipulate it.
        /// </summary>
        /// <param name="logWarningOnFailure">When true, a warning is logged if the inventory cannot be found.</param>
        private void ResolvePlayerInventory(bool logWarningOnFailure)
        {
            if (playerInventory != null)
                return;

            playerInventory = FindObjectOfType<Inventory.Inventory>(true);

            if (playerInventory != null)
            {
                hasLoggedMissingInventory = false;
                return;
            }

            if (logWarningOnFailure && !hasLoggedMissingInventory)
            {
                Debug.LogWarning("[ShopUI] Unable to locate the player's Inventory component. Shop purchases will be unavailable until it spawns.", this);
                hasLoggedMissingInventory = true;
            }
        }

        private void HandleInventoryChanged()
        {
            Refresh();
        }

        protected override bool CanOpen()
        {
            return pendingShop != null && WindowRoot != null;
        }

        protected override void OnBeforeOpen()
        {
            currentShop = pendingShop;
            npcMover = pendingNpc;

            if (playerInventory == null)
                ResolvePlayerInventory(false);

            inventoryStateCaptured = false;
            if (playerInventory != null)
            {
                inventoryWasOpenBeforeShop = playerInventory.IsOpen;
                inventoryStateCaptured = true;
                playerInventory.SetShopContext(currentShop);
                playerInventory.OnInventoryChanged += HandleInventoryChanged;
            }

            playerMovementLock.Acquire();

            if (npcMover != null)
                npcMover.enabled = false;
        }

        protected override void OnAfterOpen()
        {
            Refresh();

            if (playerInventory == null)
                ResolvePlayerInventory(true);

            if (playerInventory != null)
            {
                if (!inventoryStateCaptured)
                {
                    inventoryWasOpenBeforeShop = playerInventory.IsOpen;
                    inventoryStateCaptured = true;
                    playerInventory.SetShopContext(currentShop);
                    playerInventory.OnInventoryChanged += HandleInventoryChanged;
                }

                if (!playerInventory.IsOpen)
                    playerInventory.OpenUI();
            }

            pendingShop = null;
            pendingNpc = null;
        }

        protected override void OnBeforeClose()
        {
            if (playerInventory != null)
            {
                playerInventory.OnInventoryChanged -= HandleInventoryChanged;
                playerInventory.SetShopContext(null);

                if (inventoryStateCaptured)
                {
                    if (!inventoryWasOpenBeforeShop && playerInventory.IsOpen)
                        playerInventory.CloseUI();
                    else if (inventoryWasOpenBeforeShop && !playerInventory.IsOpen)
                        playerInventory.OpenUI();
                }
            }

            playerMovementLock.Release();

            if (npcMover != null)
            {
                npcMover.enabled = true;
                npcMover = null;
            }

            inventoryStateCaptured = false;
            inventoryWasOpenBeforeShop = false;
            shopModalActive = false;
        }

        protected override void OnAfterClose()
        {
            currentShop = null;
            if (shopNameText != null)
                shopNameText.text = string.Empty;
        }

        private void CreateUI()
        {
            var overlay = OverlayCanvasFactory.CreateOverlayCanvas(
                "ShopUI",
                referenceResolution,
                dontDestroyOnLoad: true,
                matchWidthOrHeight: 0f);

            uiRoot = overlay.Root;

            Font runtimeFont = priceFont != null ? priceFont : LegacyFontProvider.GetLegacyFont();

            GameObject window = new GameObject("Window", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(uiRoot.transform, false);

            var windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.anchoredPosition = Vector2.zero;

            var windowImg = window.GetComponent<Image>();
            windowImg.color = windowColor;

            var closeButton = CloseButtonBuilder.Build(
                window.transform,
                Close,
                new CloseButtonBuilder.Options
                {
                    Font = runtimeFont,
                    AnchoredPosition = new Vector2(-4f, -4f),
                    Size = new Vector2(16f, 16f)
                });

            // Cache the close button rect so we can reserve horizontal space for it
            // when laying out the shop name label below. Without this, the label would
            // overlap the close button when the window resizes for larger inventories.
            var closeRect = closeButton.GetComponent<RectTransform>();

            GameObject panel = new GameObject("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
            panel.transform.SetParent(window.transform, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = slotSize;
            grid.spacing = slotSpacing;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;

            slotImages = new Image[Shop.MaxSlots];
            slotPriceTexts = new Text[Shop.MaxSlots];

            for (int i = 0; i < Shop.MaxSlots; i++)
            {
                GameObject slot = new GameObject($"Slot{i}", typeof(Image));
                slot.transform.SetParent(panel.transform, false);

                var img = slot.GetComponent<Image>();
                if (slotFrameSprite != null)
                {
                    img.sprite = slotFrameSprite;
                    img.type = Image.Type.Sliced;
                    img.color = emptySlotColor;
                }
                else
                {
                    img.sprite = null;
                    img.color = emptySlotColor;
                }
                img.enabled = true;
                slotImages[i] = img;

                GameObject priceGO = new GameObject("Price", typeof(Text));
                priceGO.transform.SetParent(slot.transform, false);
                var priceText = priceGO.GetComponent<Text>();
                priceText.font = runtimeFont;
                priceText.alignment = TextAnchor.LowerLeft;
                priceText.color = priceColor;
                priceText.raycastTarget = false;
                var priceRect = priceGO.GetComponent<RectTransform>();
                priceRect.anchorMin = Vector2.zero;
                priceRect.anchorMax = Vector2.one;
                priceRect.offsetMin = Vector2.zero;
                priceRect.offsetMax = Vector2.zero;
                slotPriceTexts[i] = priceText;

                var slotComponent = slot.AddComponent<ShopSlot>();
                slotComponent.shopUI = this;
                slotComponent.index = i;
            }

            int rows = Mathf.CeilToInt((float)Shop.MaxSlots / grid.constraintCount);
            float panelHeight = rows * slotSize.y + (rows - 1) * slotSpacing.y;
            float width = grid.constraintCount * slotSize.x + (grid.constraintCount - 1) * slotSpacing.x + windowPadding.x * 2f;
            float tooltipHeight = 20f;
            float windowHeight = panelHeight + windowPadding.y * 2f + extraWindowHeight + tooltipHeight;
            windowRect.sizeDelta = new Vector2(width, windowHeight);
            rect.sizeDelta = new Vector2(width - windowPadding.x * 2f, panelHeight);
            float nameHeight = 20f;
            GameObject nameGO = new GameObject("Name", typeof(Text));
            nameGO.transform.SetParent(window.transform, false);
            shopNameText = nameGO.GetComponent<Text>();
            shopNameText.font = runtimeFont;
            shopNameText.color = priceColor;
            shopNameText.alignment = TextAnchor.MiddleLeft;
            shopNameText.text = string.Empty;
            shopNameText.raycastTarget = false;
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.offsetMin = new Vector2(windowPadding.x, -windowPadding.y - nameHeight);
            nameRect.offsetMax = new Vector2(-windowPadding.x - closeRect.sizeDelta.x - 4f, -windowPadding.y);

            GameObject tooltipGO = new GameObject("Tooltip", typeof(Text));
            tooltipGO.transform.SetParent(window.transform, false);
            tooltipText = tooltipGO.GetComponent<Text>();
            tooltipText.font = runtimeFont;
            tooltipText.color = priceColor;
            tooltipText.alignment = TextAnchor.MiddleLeft;
            tooltipText.text = string.Empty;
            tooltipText.raycastTarget = false;
            var tooltipRect = tooltipGO.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(0f, 0f);
            tooltipRect.anchorMax = new Vector2(1f, 0f);
            tooltipRect.pivot = new Vector2(0.5f, 0f);
            tooltipRect.offsetMin = new Vector2(windowPadding.x, windowPadding.y);
            tooltipRect.offsetMax = new Vector2(-windowPadding.x, windowPadding.y + tooltipHeight);
        }

        private void CacheSlotReferences()
        {
            if (uiRoot == null)
                return;

            var slots = uiRoot.GetComponentsInChildren<ShopSlot>(true);
            slotImages = new Image[slots.Length];
            slotPriceTexts = new Text[slots.Length];

            for (int i = 0; i < slots.Length; i++)
            {
                slotImages[i] = slots[i].GetComponent<Image>();
                slotPriceTexts[i] = slots[i].GetComponentInChildren<Text>();
                slots[i].shopUI = this;
                slots[i].index = i;
            }

            var texts = uiRoot.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t.gameObject.name == "Tooltip")
                    tooltipText = t;
                else if (t.gameObject.name == "Name")
                    shopNameText = t;
            }
        }

        public void Refresh()
        {
            HideTooltip();
            if (slotImages == null || slotPriceTexts == null)
                CacheSlotReferences();
            if (shopNameText != null)
                shopNameText.text = currentShop != null ? currentShop.shopName : string.Empty;
            for (int i = 0; i < slotImages.Length; i++)
            {
                var img = slotImages[i];
                var price = slotPriceTexts[i];
                if (currentShop != null && i < currentShop.stock.Length)
                {
                    var entry = currentShop.stock[i];
                    if (entry.item != null && entry.quantity > 0)
                    {
                        Sprite sprite = entry.item.GetIconForCount(entry.quantity);
                        if (sprite == null)
                            sprite = entry.item.icon != null ? entry.item.icon : slotFrameSprite;
                        if (sprite == null)
                            sprite = slotFrameSprite;
                        img.sprite = sprite;
                        img.color = Color.white;
                        img.enabled = true;
                        // Display only the quantity in the slot, not the price
                        price.text = $"({entry.quantity})";
                        continue;
                    }
                }
                img.sprite = slotFrameSprite;
                img.color = emptySlotColor;
                price.text = string.Empty;
            }
        }

        public void ShowTooltip(int index)
        {
            if (tooltipText == null || currentShop == null) return;
            if (index < 0 || index >= currentShop.stock.Length) return;

            var entry = currentShop.stock[index];
            if (entry.item == null) return;

            string currencyName = currentShop.currency != null ? currentShop.currency.itemName : "Coins";
            tooltipText.text = $"{entry.item.itemName} costs {entry.price} {currencyName}";
            var tooltipRect = tooltipText.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);
        }

        public void HideTooltip()
        {
            if (tooltipText != null)
            {
                tooltipText.text = string.Empty;
            }
        }
    }
}
