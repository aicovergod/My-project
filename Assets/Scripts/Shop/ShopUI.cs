using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Inventory;

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

        [Header("Price Display")]
        public Font priceFont;
        public Color priceColor = Color.white;

        [Header("Inventory")]
        public Inventory.Inventory playerInventory;

        private GameObject uiRoot;
        private Image[] slotImages;
        private Text[] slotPriceTexts;
        private Shop currentShop;

        private void Awake()
        {
            CreateUI();
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        /// <summary>
        /// Opens the UI for the given shop.
        /// </summary>
        public void Open(Shop shop)
        {
            if (shop == null) return;
            currentShop = shop;
            Refresh();
            uiRoot.SetActive(true);
        }

        /// <summary>
        /// Closes the shop UI.
        /// </summary>
        public void Close()
        {
            uiRoot?.SetActive(false);
            currentShop = null;
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
            grid.constraint = GridLayoutGroup.Constraint.FixedColumns;
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
            float width = grid.constraintCount * slotSize.x + (grid.constraintCount - 1) * slotSpacing.x + windowPadding.x * 2f;
            float height = rows * slotSize.y + (rows - 1) * slotSpacing.y + windowPadding.y * 2f;
            windowRect.sizeDelta = new Vector2(width, height);
            rect.sizeDelta = new Vector2(width - windowPadding.x * 2f, height - windowPadding.y * 2f);
        }

        private void Refresh()
        {
            for (int i = 0; i < slotImages.Length; i++)
            {
                var img = slotImages[i];
                var price = slotPriceTexts[i];
                if (currentShop != null && i < currentShop.stock.Length)
                {
                    var entry = currentShop.stock[i];
                    if (entry.item != null)
                    {
                        img.sprite = entry.item.icon != null ? entry.item.icon : slotFrameSprite;
                        img.color = Color.white;
                        img.enabled = true;
                        price.text = entry.price.ToString();
                        continue;
                    }
                }
                img.sprite = slotFrameSprite;
                img.color = emptySlotColor;
                price.text = string.Empty;
            }
        }
    }
}
