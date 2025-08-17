using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Inventory
{
    /// <summary>
    /// Responsible purely for creating and updating the inventory UI.  All data
    /// manipulation lives in <see cref="InventoryModel"/> while interactions such
    /// as dragging or tooltips are handled by dedicated components.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventoryModel))]
    public class InventoryUI : MonoBehaviour
    {
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
        public Color emptySlotColor = new Color(1f, 1f, 1f, 0.25f);

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
        private GameObject tooltip;
        private Text tooltipNameText;
        private Text tooltipDescriptionText;

        private GameObject uiRoot; // Canvas root
        private static GameObject sharedUIRoot;
        private Font defaultFont;
        private InventoryModel model;

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;
        public GameObject UIRoot => uiRoot;
        public Vector2 SlotSize => slotSize;
        public Image[] SlotImages => slotImages;
        public Text[] SlotCountTexts => slotCountTexts;
        public GameObject TooltipObject => tooltip;
        public Text TooltipNameText => tooltipNameText;
        public Text TooltipDescriptionText => tooltipDescriptionText;

        private void Awake()
        {
            model = GetComponent<InventoryModel>();

            // Unity 2022+ renamed the builtin Arial font. Attempt to load the new
            // name first and fall back for older Unity versions to avoid runtime
            // exceptions that would prevent the UI from being created.
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

            if (uiRoot != null)
                uiRoot.SetActive(false);

            model.OnInventoryChanged += RefreshAll;
        }

        private void OnDestroy()
        {
            if (model != null)
                model.OnInventoryChanged -= RefreshAll;
        }

        public void OpenUI()
        {
            if (uiRoot != null)
                uiRoot.SetActive(true);
        }

        public void CloseUI()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("InventoryUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            uiRoot.transform.SetParent(null, false);
            DontDestroyOnLoad(uiRoot);

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) uiRoot.layer = uiLayer;

            var canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;

            var scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 0f; // match width

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

            slotImages = new Image[model.Count];
            slotCountTexts = new Text[model.Count];

            var drag = GetComponent<InventoryDragHandler>();
            var tooltipComp = GetComponent<InventoryTooltip>();

            for (int i = 0; i < model.Count; i++)
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

                var slotComponent = slot.AddComponent<InventorySlot>();
                slotComponent.model = model;
                slotComponent.index = i;
                slotComponent.drag = drag;
                slotComponent.tooltip = tooltipComp;
                slotComponent.ui = this;

                slotImages[i] = img;
                slotCountTexts[i] = countText;
            }

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
            tooltipNameText.font = tooltipNameFont != null ? tooltipNameFont : defaultFont;
            tooltipNameText.alignment = TextAnchor.UpperLeft;
            tooltipNameText.color = tooltipNameColor;
            tooltipNameText.raycastTarget = false;
            tooltipNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipNameText.verticalOverflow = VerticalWrapMode.Overflow;

            var descGO = new GameObject("Description", typeof(Text));
            descGO.transform.SetParent(tooltip.transform, false);
            tooltipDescriptionText = descGO.GetComponent<Text>();
            tooltipDescriptionText.font = tooltipDescriptionFont != null ? tooltipDescriptionFont : defaultFont;
            tooltipDescriptionText.alignment = TextAnchor.UpperLeft;
            tooltipDescriptionText.color = tooltipDescriptionColor;
            tooltipDescriptionText.raycastTarget = false;
            tooltipDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipDescriptionText.verticalOverflow = VerticalWrapMode.Overflow;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            tooltipRect.pivot = new Vector2(0f, 1f);

            tooltip.SetActive(false);
        }

        public void RefreshAll()
        {
            if (slotImages == null) return;
            for (int i = 0; i < slotImages.Length; i++)
                UpdateSlotVisual(i);
        }

        public void UpdateSlotVisual(int index)
        {
            if (slotImages == null || index < 0 || index >= slotImages.Length || slotImages[index] == null)
                return;

            var entry = model.GetEntry(index);
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

        public void PromptStackSplit(int slotIndex, StackSplitType type)
        {
            if (slotIndex < 0 || slotIndex >= model.Count) return;
            var entry = model.GetEntry(slotIndex);
            if (entry.item == null || entry.count <= 1) return;
            if (!entry.item.splittable) return;
            if (type == StackSplitType.Drop && !model.CanDropItems) return;

            StackSplitDialog.Show(uiRoot.transform, entry.count, amount =>
            {
                switch (type)
                {
                    case StackSplitType.Sell:
                        model.SellItem(slotIndex, amount);
                        break;
                    case StackSplitType.Drop:
                        model.DropItem(slotIndex, amount);
                        break;
                    case StackSplitType.Split:
                        model.SplitStack(slotIndex, amount);
                        break;
                }
            });
        }

        private void EnsureLegacyEventSystem()
        {
            if (EventSystem.current != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(go);
        }
    }
}
