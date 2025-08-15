using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Inventory;
using Player;
using NPC;

namespace ShopSystem
{
    /// <summary>
    /// Runtime generated shop UI used to display items for sale.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopUI : MonoBehaviour
    {
        [Header("Layout")]
        public Vector2 slotSize = new Vector2(32, 32);
        public Vector2 slotSpacing = new Vector2(4, 4);
        public Vector2 referenceResolution = new Vector2(640, 360);
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
        private Image[] slotImages;
        private Text[] slotPriceTexts;
        private Text tooltipText;
        private Text shopNameText;
        private Shop currentShop;
        private PlayerMover playerMover;
        private NpcRandomMovement npcMover;

        private void Awake()
        {
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        /// <summary>
        /// Opens the UI for the given shop and optionally pauses an NPC's movement.
        /// </summary>
        public void Open(Shop shop, NpcRandomMovement npcMovement = null)
        {
            if (shop == null) return;
            currentShop = shop;
            Refresh();
            uiRoot.SetActive(true);
            if (playerMover == null)
                playerMover = FindObjectOfType<PlayerMover>();
            if (playerMover != null)
                playerMover.enabled = false;
            npcMover = npcMovement;
            if (npcMover != null)
                npcMover.enabled = false;
        }

        /// <summary>
        /// Closes the shop UI.
        /// </summary>
        public void Close()
        {
            uiRoot?.SetActive(false);
            currentShop = null;
            if (shopNameText != null)
                shopNameText.text = string.Empty;
            if (playerMover != null)
                playerMover.enabled = true;
            if (npcMover != null)
            {
                npcMover.enabled = true;
                npcMover = null;
            }
        }

        /// <summary>
        /// Attempt to buy item at slot index.
        /// </summary>
        public void Buy(int index)
        {
            if (currentShop == null || playerInventory == null) return;
            if (currentShop.Buy(index, playerInventory))
            {
                Refresh();
            }
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("ShopUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

            var windowImg = window.GetComponent<Image>();
            windowImg.color = windowColor;

            GameObject closeButtonGO = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            closeButtonGO.transform.SetParent(window.transform, false);
            var closeRect = closeButtonGO.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-4f, -4f);
            closeRect.sizeDelta = new Vector2(16f, 16f);
            var closeImg = closeButtonGO.GetComponent<Image>();
            closeImg.color = Color.red;
            var closeBtn = closeButtonGO.GetComponent<Button>();
            closeBtn.onClick.AddListener(Close);

            GameObject closeTextGO = new GameObject("Text", typeof(Text));
            closeTextGO.transform.SetParent(closeButtonGO.transform, false);
            var closeText = closeTextGO.GetComponent<Text>();
            closeText.font = priceFont != null ? priceFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
            closeText.text = "X";
            closeText.alignment = TextAnchor.MiddleCenter;
            closeText.color = Color.white;
            var closeTextRect = closeTextGO.GetComponent<RectTransform>();
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.offsetMin = Vector2.zero;
            closeTextRect.offsetMax = Vector2.zero;

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
                priceText.font = priceFont != null ? priceFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
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
            shopNameText.font = priceFont != null ? priceFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
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
            tooltipText.font = priceFont != null ? priceFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
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

        private void Refresh()
        {
            HideTooltip();
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
                        img.sprite = entry.item.icon != null ? entry.item.icon : slotFrameSprite;
                        img.color = Color.white;
                        img.enabled = true;
                        price.text = $"{entry.price} ({entry.quantity})";
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
            tooltipText.text = $"\"{entry.item.itemName}\" costs {entry.price} {currencyName}";
        }

        public void HideTooltip()
        {
            if (tooltipText != null)
                tooltipText.text = string.Empty;
        }
    }
}
