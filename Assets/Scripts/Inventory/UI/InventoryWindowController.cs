// Assets/Scripts/Inventory/UI/InventoryWindowController.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UI.Utilities;
using Inventory.Core;
using InventoryComponent = global::Inventory.Inventory;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Inventory.UI
{
    /// <summary>
    /// Defines the pointer callbacks that inventory slot presenters forward to the
    /// window controller. Slots no longer talk directly to <see cref="InventoryComponent"/>
    /// so presentation and gameplay logic remain isolated.
    /// </summary>
    public interface IInventoryUIActions
    {
        /// <summary>
        /// True when the owning inventory window is connected to the bank UI. Drag
        /// interactions are disabled in that state.
        /// </summary>
        bool IsBankOpen { get; }

        /// <summary>
        /// Called when the pointer enters a slot so the controller can show tooltips.
        /// </summary>
        void HandlePointerEnter(int slotIndex, RectTransform slotRect);

        /// <summary>
        /// Called when the pointer leaves a slot to hide the tooltip.
        /// </summary>
        void HandlePointerExit(int slotIndex);

        /// <summary>
        /// Begins a drag interaction for the slot at <paramref name="slotIndex"/>.
        /// </summary>
        void HandleBeginDrag(int slotIndex);

        /// <summary>
        /// Updates the drag cursor to the supplied pointer position.
        /// </summary>
        void HandleDrag(PointerEventData eventData);

        /// <summary>
        /// Completes a drag operation when the pointer is released over a slot.
        /// </summary>
        void HandleDrop(int slotIndex);

        /// <summary>
        /// Cancels the drag interaction when the pointer is released away from slots.
        /// </summary>
        void HandleEndDrag(int slotIndex);

        /// <summary>
        /// Dispatches the click information for a slot so higher level systems can
        /// decide which gameplay action to perform.
        /// </summary>
        void HandlePointerClick(int slotIndex, PointerEventData eventData);
    }

    /// <summary>
    /// Manages the runtime inventory window hierarchy. The controller is responsible
    /// for generating the canvas, binding slots, and exposing input intent events to
    /// the owning <see cref="InventoryComponent"/> instance.
    /// </summary>
    public sealed class InventoryWindowController : IInventoryUIActions
    {
        /// <summary>
        /// Configuration payload describing how the inventory window should be built.
        /// Values are copied from the <see cref="InventoryComponent"/> MonoBehaviour
        /// so UI generation stays completely data driven.
        /// </summary>
        public readonly struct WindowConfig : IEquatable<WindowConfig>
        {
            public WindowConfig(
                Vector2 slotSize,
                Vector2 slotSpacing,
                Vector2 windowPadding,
                Vector2 windowSize,
                Vector2 referenceResolution,
                Vector2 windowPosition,
                Color windowColor,
                Color emptySlotColor,
                Color stackColorDefault,
                Color stackColor10k,
                Color stackColor100k,
                Color stackColor10m,
                Color stackColor100m,
                Color tooltipNameColor,
                Color tooltipDescriptionColor,
                Font defaultFont,
                Font stackCountFont,
                Font tooltipNameFont,
                Font tooltipDescriptionFont,
                Sprite slotFrameSprite,
                bool showCloseButton,
                bool centerOnScreen,
                bool useSharedRoot,
                int columns,
                int stackCountFontSize)
            {
                SlotSize = slotSize;
                SlotSpacing = slotSpacing;
                WindowPadding = windowPadding;
                WindowSize = windowSize;
                ReferenceResolution = referenceResolution;
                WindowPosition = windowPosition;
                WindowColor = windowColor;
                EmptySlotColor = emptySlotColor;
                StackColorDefault = stackColorDefault;
                StackColor10k = stackColor10k;
                StackColor100k = stackColor100k;
                StackColor10m = stackColor10m;
                StackColor100m = stackColor100m;
                TooltipNameColor = tooltipNameColor;
                TooltipDescriptionColor = tooltipDescriptionColor;
                DefaultFont = defaultFont;
                StackCountFont = stackCountFont;
                TooltipNameFont = tooltipNameFont;
                TooltipDescriptionFont = tooltipDescriptionFont;
                SlotFrameSprite = slotFrameSprite;
                ShowCloseButton = showCloseButton;
                CenterOnScreen = centerOnScreen;
                UseSharedRoot = useSharedRoot;
                Columns = Mathf.Max(1, columns);
                StackCountFontSize = stackCountFontSize;
            }

            public Vector2 SlotSize { get; }
            public Vector2 SlotSpacing { get; }
            public Vector2 WindowPadding { get; }
            public Vector2 WindowSize { get; }
            public Vector2 ReferenceResolution { get; }
            public Vector2 WindowPosition { get; }
            public Color WindowColor { get; }
            public Color EmptySlotColor { get; }
            public Color StackColorDefault { get; }
            public Color StackColor10k { get; }
            public Color StackColor100k { get; }
            public Color StackColor10m { get; }
            public Color StackColor100m { get; }
            public Color TooltipNameColor { get; }
            public Color TooltipDescriptionColor { get; }
            public Font DefaultFont { get; }
            public Font StackCountFont { get; }
            public Font TooltipNameFont { get; }
            public Font TooltipDescriptionFont { get; }
            public Sprite SlotFrameSprite { get; }
            public bool ShowCloseButton { get; }
            public bool CenterOnScreen { get; }
            public bool UseSharedRoot { get; }
            public int Columns { get; }
            public int StackCountFontSize { get; }

            /// <summary>
            /// Determines whether two configuration payloads are equivalent.
            /// </summary>
            public bool Equals(WindowConfig other)
            {
                return SlotSize == other.SlotSize &&
                       SlotSpacing == other.SlotSpacing &&
                       WindowPadding == other.WindowPadding &&
                       WindowSize == other.WindowSize &&
                       ReferenceResolution == other.ReferenceResolution &&
                       WindowPosition == other.WindowPosition &&
                       WindowColor.Equals(other.WindowColor) &&
                       EmptySlotColor.Equals(other.EmptySlotColor) &&
                       StackColorDefault.Equals(other.StackColorDefault) &&
                       StackColor10k.Equals(other.StackColor10k) &&
                       StackColor100k.Equals(other.StackColor100k) &&
                       StackColor10m.Equals(other.StackColor10m) &&
                       StackColor100m.Equals(other.StackColor100m) &&
                       TooltipNameColor.Equals(other.TooltipNameColor) &&
                       TooltipDescriptionColor.Equals(other.TooltipDescriptionColor) &&
                       DefaultFont == other.DefaultFont &&
                       StackCountFont == other.StackCountFont &&
                       TooltipNameFont == other.TooltipNameFont &&
                       TooltipDescriptionFont == other.TooltipDescriptionFont &&
                       SlotFrameSprite == other.SlotFrameSprite &&
                       ShowCloseButton == other.ShowCloseButton &&
                       CenterOnScreen == other.CenterOnScreen &&
                       UseSharedRoot == other.UseSharedRoot &&
                       Columns == other.Columns &&
                       StackCountFontSize == other.StackCountFontSize;
            }

            /// <summary>
            /// Determines whether the supplied object matches this configuration.
            /// </summary>
            public override bool Equals(object obj)
            {
                return obj is WindowConfig other && Equals(other);
            }

            /// <summary>
            /// Generates a hash code so payloads can be cached or compared in sets.
            /// </summary>
            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add(SlotSize);
                hash.Add(SlotSpacing);
                hash.Add(WindowPadding);
                hash.Add(WindowSize);
                hash.Add(ReferenceResolution);
                hash.Add(WindowPosition);
                hash.Add(WindowColor);
                hash.Add(EmptySlotColor);
                hash.Add(StackColorDefault);
                hash.Add(StackColor10k);
                hash.Add(StackColor100k);
                hash.Add(StackColor10m);
                hash.Add(StackColor100m);
                hash.Add(TooltipNameColor);
                hash.Add(TooltipDescriptionColor);
                hash.Add(DefaultFont);
                hash.Add(StackCountFont);
                hash.Add(TooltipNameFont);
                hash.Add(TooltipDescriptionFont);
                hash.Add(SlotFrameSprite);
                hash.Add(ShowCloseButton);
                hash.Add(CenterOnScreen);
                hash.Add(UseSharedRoot);
                hash.Add(Columns);
                hash.Add(StackCountFontSize);
                return hash.ToHashCode();
            }

            public static bool operator ==(WindowConfig left, WindowConfig right) => left.Equals(right);

            public static bool operator !=(WindowConfig left, WindowConfig right) => !left.Equals(right);
        }

        /// <summary>
        /// Describes a slot click. Consumers inspect the pointer button and modifier
        /// state to resolve the correct gameplay action (equip, use, deposit, etc.).
        /// </summary>
        public readonly struct SlotClickEvent
        {
            public SlotClickEvent(InventoryWindowController controller, int slotIndex, PointerEventData.InputButton button, bool shiftHeld, Vector2 pointerPosition)
            {
                Controller = controller;
                SlotIndex = slotIndex;
                Button = button;
                ShiftHeld = shiftHeld;
                PointerPosition = pointerPosition;
            }

            public InventoryWindowController Controller { get; }
            public int SlotIndex { get; }
            public PointerEventData.InputButton Button { get; }
            public bool ShiftHeld { get; }
            public Vector2 PointerPosition { get; }
        }

        /// <summary>
        /// Raised when the player selects a drop quantity using the legacy drop menu.
        /// </summary>
        public readonly struct DropRequestEvent
        {
            public DropRequestEvent(InventoryWindowController controller, int slotIndex, int quantity)
            {
                Controller = controller;
                SlotIndex = slotIndex;
                Quantity = Mathf.Max(1, quantity);
            }

            public InventoryWindowController Controller { get; }
            public int SlotIndex { get; }
            public int Quantity { get; }
        }

        /// <summary>
        /// Raised when the player drops an item onto another slot during a drag.
        /// </summary>
        public readonly struct DragDropEvent
        {
            public DragDropEvent(InventoryWindowController source, int sourceIndex, InventoryWindowController target, int targetIndex)
            {
                Source = source;
                SourceIndex = sourceIndex;
                Target = target;
                TargetIndex = targetIndex;
            }

            public InventoryWindowController Source { get; }
            public int SourceIndex { get; }
            public InventoryWindowController Target { get; }
            public int TargetIndex { get; }
        }

        /// <summary>
        /// Raised whenever an item context menu option is selected so gameplay systems can react.
        /// </summary>
        public readonly struct ItemContextActionEvent
        {
            public ItemContextActionEvent(
                InventoryWindowController controller,
                int slotIndex,
                InventoryItemContextAction action,
                Vector2 pointerPosition)
            {
                Controller = controller;
                SlotIndex = slotIndex;
                Action = action;
                PointerPosition = pointerPosition;
            }

            public InventoryWindowController Controller { get; }
            public int SlotIndex { get; }
            public InventoryItemContextAction Action { get; }
            public Vector2 PointerPosition { get; }
        }

        public event Action<InventoryWindowController, SlotClickEvent> SlotClicked;
        public event Action<InventoryWindowController, DropRequestEvent> DropRequested;
        public event Action<InventoryWindowController, DragDropEvent> DragDropRequested;
        public event Action<InventoryWindowController> DragCancelled;
        public event Action<InventoryWindowController> CloseRequested;
        public event Action<InventoryWindowController, ItemContextActionEvent> ContextActionSelected;

        private readonly InventoryModel model;
        private WindowConfig config;

        private GameObject uiRoot;
        private Image[] slotImages;
        private Text[] slotCountTexts;
        private Image[] slotHighlights;
        private Material highlightMaterial;

        private GameObject tooltip;
        private Text tooltipNameText;
        private Text tooltipDescriptionText;
        private InventoryItemContextMenu itemContextMenu;
        private Vector2 lastContextMenuPointerPosition;

        private static InventoryWindowController activeDragController;
        private static int activeDragIndex = -1;
        private static GameObject activeDragIcon;

        private int selectedIndex = -1;

        /// <summary>
        /// Canvas root shared by any inventories that opt into the shared window
        /// configuration. Used for drag icons so they render above all windows.
        /// </summary>
        public static Transform SharedCanvasRoot { get; private set; }

        public bool IsBankOpen { get; set; }
        public bool InShop { get; set; }
        public bool CanDropItems { get; set; } = true;
        public ShopSystem.Shop CurrentShop { get; set; }

        /// <summary>
        /// Owning inventory component. Set by <see cref="InventoryComponent"/> after
        /// construction so drag events can be resolved across windows.
        /// </summary>
        public InventoryComponent Owner { get; internal set; }

        public GameObject UiRoot => uiRoot;

        /// <summary>Last pointer position used to display a context menu.</summary>
        public Vector2 LastContextMenuPointerPosition => lastContextMenuPointerPosition;

        public Vector2 SlotSize => config.SlotSize;

        public InventoryWindowController(InventoryModel model, WindowConfig config)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.config = config;

            BuildUserInterface();
            RefreshAllSlots();
        }

        /// <summary>
        /// Replaces the active configuration and rebuilds the UI hierarchy so the
        /// slot grid reflects the latest runtime sizing and styling rules.
        /// </summary>
        /// <param name="newConfig">Payload describing the desired UI layout.</param>
        public void ApplyConfig(WindowConfig newConfig)
        {
            config = newConfig;

            DestroyUi();
            BuildUserInterface();
            RefreshAllSlots();
        }

        /// <summary>
        /// Shows the inventory window.
        /// </summary>
        public void Show()
        {
            if (uiRoot != null)
            {
                uiRoot.SetActive(true);

                // Ensure the shared canvas root always references the active canvas
                // so drag icons parent themselves under a live hierarchy.
                if (config.UseSharedRoot)
                    SharedCanvasRoot = uiRoot.transform;
            }
        }

        /// <summary>
        /// Hides the inventory window and any auxiliary UI elements.
        /// </summary>
        public void Hide()
        {
            if (uiRoot != null)
            {
                uiRoot.SetActive(false);

                // Clear the shared canvas root when this window hides to prevent
                // future drag icons from binding to an inactive hierarchy.
                if (config.UseSharedRoot && SharedCanvasRoot == uiRoot.transform)
                    SharedCanvasRoot = null;
            }

            HideTooltip();
            DismissContextMenus();
        }

        /// <summary>
        /// Rebuilds the UI on a dedicated canvas, ensuring highlight and tooltip
        /// references are regenerated when the shared canvas option is disabled.
        /// </summary>
        public void ForceDedicatedCanvas()
        {
            if (config.UseSharedRoot)
                return;

            DestroyUi();
            BuildUserInterface();
            RefreshAllSlots();
        }

        /// <summary>
        /// Ensures that all slot visuals match the underlying inventory model.
        /// </summary>
        public void RefreshAllSlots()
        {
            if (slotImages == null)
                return;

            for (int i = 0; i < slotImages.Length; i++)
                RefreshSlot(i);
        }

        /// <summary>
        /// Updates the UI for an individual slot.
        /// </summary>
        public void RefreshSlot(int index)
        {
            if (slotImages == null || index < 0 || index >= slotImages.Length)
                return;

            var image = slotImages[index];
            if (image == null)
                return;

            var entry = model.GetEntry(index);
            var item = entry.item;

            if (item != null)
            {
                Sprite sprite = item.GetIconForCount(entry.count);
                if (sprite == null)
                    sprite = item.icon != null ? item.icon : config.SlotFrameSprite;
                if (sprite == null)
                    sprite = config.SlotFrameSprite;

                image.sprite = sprite;
                image.type = (image.sprite == config.SlotFrameSprite && config.SlotFrameSprite != null)
                    ? Image.Type.Sliced
                    : Image.Type.Simple;
                image.color = Color.white;
                image.enabled = true;

                if (slotCountTexts != null && slotCountTexts.Length > index && slotCountTexts[index] != null)
                {
                    var text = slotCountTexts[index];
                    if (entry.count > 1)
                    {
                        Color color;
                        text.text = FormatStackCount(entry.count, out color);
                        text.color = color;
                        text.enabled = true;
                    }
                    else
                    {
                        text.text = string.Empty;
                        text.enabled = false;
                    }
                }
            }
            else
            {
                image.sprite = config.SlotFrameSprite;
                image.type = (config.SlotFrameSprite != null) ? Image.Type.Sliced : Image.Type.Simple;
                image.color = config.EmptySlotColor;
                image.enabled = true;

                if (slotCountTexts != null && slotCountTexts.Length > index && slotCountTexts[index] != null)
                {
                    var text = slotCountTexts[index];
                    text.text = string.Empty;
                    text.enabled = false;
                }
            }

            if (slotHighlights != null && slotHighlights.Length > index && slotHighlights[index] != null)
            {
                var highlight = slotHighlights[index];
                highlight.sprite = slotImages[index].sprite;
                highlight.type = Image.Type.Simple;
                highlight.color = new Color(1f, 1f, 1f, 1f);
                highlight.enabled = (selectedIndex == index);
            }
        }

        /// <summary>
        /// Records the active slot selection so highlight visuals stay in sync with
        /// gameplay selection state.
        /// </summary>
        public void SetSelectedIndex(int index)
        {
            if (selectedIndex == index)
                return;

            int previous = selectedIndex;
            selectedIndex = index;

            if (previous >= 0)
                RefreshSlot(previous);
            if (selectedIndex >= 0)
                RefreshSlot(selectedIndex);
        }

        /// <summary>
        /// Clears all slot highlights without altering the gameplay selection.
        /// </summary>
        public void ClearHighlight()
        {
            selectedIndex = -1;
            if (slotHighlights == null)
                return;

            for (int i = 0; i < slotHighlights.Length; i++)
            {
                if (slotHighlights[i] != null)
                    slotHighlights[i].enabled = false;
            }
        }

        public void HandlePointerEnter(int slotIndex, RectTransform slotRect)
        {
            ShowTooltip(slotIndex, slotRect);
        }

        public void HandlePointerExit(int slotIndex)
        {
            HideTooltip();
        }

        public void HandleBeginDrag(int slotIndex)
        {
            if (IsBankOpen)
                return;

            if (slotIndex < 0 || slotIndex >= model.Size)
                return;

            var entry = model.GetEntry(slotIndex);
            if (entry.item == null)
                return;

            HideTooltip();

            activeDragController = this;
            activeDragIndex = slotIndex;

            if (slotImages != null && slotIndex < slotImages.Length && slotImages[slotIndex] != null)
                slotImages[slotIndex].enabled = false;
            if (slotCountTexts != null && slotIndex < slotCountTexts.Length && slotCountTexts[slotIndex] != null)
                slotCountTexts[slotIndex].enabled = false;

            CreateDragIcon(entry);
        }

        public void HandleDrag(PointerEventData eventData)
        {
            if (IsBankOpen)
                return;

            if (activeDragIcon != null)
                activeDragIcon.transform.position = eventData.position;
        }

        public void HandleDrop(int slotIndex)
        {
            if (IsBankOpen)
            {
                EndDragInternal();
                return;
            }

            if (activeDragController == null || activeDragIndex < 0)
            {
                EndDragInternal();
                return;
            }

            if (slotIndex < 0 || slotIndex >= model.Size)
            {
                EndDragInternal();
                return;
            }

            var evt = new DragDropEvent(activeDragController, activeDragIndex, this, slotIndex);
            DragDropRequested?.Invoke(this, evt);

            EndDragInternal();
        }

        public void HandleEndDrag(int slotIndex)
        {
            EndDragInternal();
        }

        public void HandlePointerClick(int slotIndex, PointerEventData eventData)
        {
            bool shiftHeld = IsShiftHeld();
            SlotClicked?.Invoke(this, new SlotClickEvent(this, slotIndex, eventData.button, shiftHeld, eventData.position));
        }

        internal void HandleDropMenuSelection(int slotIndex, DropMenuSelection selection)
        {
            switch (selection)
            {
                case DropMenuSelection.DropOne:
                    DropRequested?.Invoke(this, new DropRequestEvent(this, slotIndex, 1));
                    break;
                case DropMenuSelection.DropAll:
                    var entry = model.GetEntry(slotIndex);
                    DropRequested?.Invoke(this, new DropRequestEvent(this, slotIndex, entry.count));
                    break;
            }
        }

        internal void HandleItemContextMenuSelection(
            InventoryWindowController controller,
            int slotIndex,
            InventoryItemContextAction action,
            Vector2 pointerPosition)
        {
            if (controller != this)
                return;

            HideItemContextMenu();
            lastContextMenuPointerPosition = pointerPosition;
            ContextActionSelected?.Invoke(this, new ItemContextActionEvent(this, slotIndex, action, pointerPosition));
        }

        internal void HideItemContextMenu()
        {
            if (itemContextMenu != null)
                itemContextMenu.Hide();
        }

        public void DismissContextMenus()
        {
            HideItemContextMenu();
            lastContextMenuPointerPosition = Vector2.zero;
        }

        private void ShowTooltip(int slotIndex, RectTransform slotRect)
        {
            if (slotIndex < 0 || slotIndex >= model.Size)
                return;

            var entry = model.GetEntry(slotIndex);
            if (entry.item == null)
                return;

            ShowTooltipForItem(entry.item, slotRect, CurrentShop);
        }

        /// <summary>
        /// Displays the inventory tooltip for an arbitrary item, allowing external systems
        /// such as the bank UI to reuse the unified hover presentation.
        /// </summary>
        /// <param name="item">Item definition that should populate the tooltip.</param>
        /// <param name="slotRect">World-space rectangle used to anchor the tooltip.</param>
        /// <param name="shopOverride">
        /// Optional shop context that forces sell price information even when this window
        /// is not currently bound to an active shop.
        /// </param>
        public void ShowTooltipForItem(global::Inventory.ItemData item, RectTransform slotRect, ShopSystem.Shop shopOverride = null)
        {
            if (tooltip == null || tooltipNameText == null || tooltipDescriptionText == null)
                return;
            if (item == null || slotRect == null)
                return;

            var activeShop = shopOverride ?? CurrentShop;
            if (activeShop != null && activeShop.TryGetSellPrice(item, out int sellPrice))
            {
                string currencyName;
                if (activeShop.currency != null)
                    currencyName = !string.IsNullOrEmpty(activeShop.currency.itemName) ? activeShop.currency.itemName : activeShop.currency.name;
                else
                    currencyName = "Coins";

                tooltipNameText.text = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
                tooltipDescriptionText.text = $"Sell for {sellPrice} {currencyName}";

                PositionTooltip(slotRect);
                tooltip.SetActive(true);
                return;
            }

            string name = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            tooltipNameText.text = name;
            tooltipDescriptionText.text = BuildTooltipDescription(item);

            PositionTooltip(slotRect);
            tooltip.SetActive(true);
        }

        private void PositionTooltip(RectTransform slotRect)
        {
            var tooltipRect = tooltip.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

            Vector3 pos = slotRect.position + new Vector3(config.SlotSize.x, 0f, 0f);
            Vector3[] corners = new Vector3[4];
            tooltipRect.GetWorldCorners(corners);
            float width = corners[2].x - corners[0].x;
            float height = corners[2].y - corners[0].y;
            pos.x = Mathf.Min(pos.x, Screen.width - width);
            pos.y = Mathf.Max(pos.y, height);
            tooltipRect.position = pos;
        }

        private void HideTooltip()
        {
            if (tooltip != null)
                tooltip.SetActive(false);
        }

        public void DismissTooltip()
        {
            HideTooltip();
        }

        internal void ShowItemContextMenu(
            int slotIndex,
            Vector2 pointerPosition,
            IReadOnlyList<InventoryItemContextMenu.Option> options)
        {
            if (itemContextMenu == null)
                return;

            HideTooltip();
            HideDropMenu();
            HideItemContextMenu();

            if (options == null || options.Count == 0)
                return;

            lastContextMenuPointerPosition = pointerPosition;
            itemContextMenu.Show(this, slotIndex, pointerPosition, options);
        }

        private void BuildUserInterface()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            var overlay = OverlayCanvasFactory.CreateOverlayCanvas(
                "InventoryUI",
                config.ReferenceResolution,
                dontDestroyOnLoad: true,
                matchWidthOrHeight: 0f,
                explicitLayer: uiLayer >= 0 ? uiLayer : (int?)null,
                assignToUiLayer: uiLayer < 0);

            uiRoot = overlay.Root;

            var window = new GameObject("Window", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(uiRoot.transform, false);

            var windowRect = window.GetComponent<RectTransform>();
            if (config.CenterOnScreen)
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
                windowRect.anchoredPosition = config.WindowPosition;
            }

            var windowImg = window.GetComponent<Image>();
            windowImg.color = config.WindowColor;

            GameObject panel = new GameObject("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
            panel.transform.SetParent(window.transform, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(config.WindowPadding.x, -config.WindowPadding.y);

            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = config.SlotSize;
            grid.spacing = config.SlotSpacing;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = config.Columns;

            slotImages = new Image[model.Size];
            slotCountTexts = new Text[model.Size];
            slotHighlights = new Image[model.Size];

            try
            {
                for (int i = 0; i < model.Size; i++)
                {
                    GameObject slot = new GameObject($"Slot{i}", typeof(Image));
                    slot.transform.SetParent(panel.transform, false);

                    var img = slot.GetComponent<Image>();
                    if (config.SlotFrameSprite != null)
                    {
                        img.sprite = config.SlotFrameSprite;
                        img.type = Image.Type.Sliced;
                        img.color = config.EmptySlotColor;
                    }
                    else
                    {
                        img.sprite = null;
                        img.color = config.EmptySlotColor;
                    }

                    img.enabled = true;

                    GameObject highlightGO = new GameObject("Highlight", typeof(Image));
                    highlightGO.transform.SetParent(slot.transform, false);
                    var highlightImg = highlightGO.GetComponent<Image>();
                    highlightImg.sprite = null;
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

                    GameObject countGO = new GameObject("Count", typeof(Text));
                    countGO.transform.SetParent(slot.transform, false);
                    var countText = countGO.GetComponent<Text>();
                    var outline = countGO.AddComponent<Outline>();
                    outline.effectColor = Color.black;
                    outline.effectDistance = new Vector2(1f, -1f);
                    outline.useGraphicAlpha = false;
                    countText.font = config.StackCountFont != null ? config.StackCountFont : config.DefaultFont;
                    countText.fontSize = config.StackCountFontSize;
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

                    var slotComponent = slot.AddComponent<InventorySlot>();
                    slotComponent.Initialize(this, i);

                    slotImages[i] = img;
                    slotCountTexts[i] = countText;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Inventory UI generation failed: {ex}");
            }

            int rows = Mathf.CeilToInt((float)model.Size / Mathf.Max(1, config.Columns));
            Vector2 resolvedWindowSize = new Vector2(
                config.Columns * config.SlotSize.x + (config.Columns - 1) * config.SlotSpacing.x + config.WindowPadding.x * 2f,
                rows * config.SlotSize.y + (rows - 1) * config.SlotSpacing.y + config.WindowPadding.y * 2f);

            rect.sizeDelta = new Vector2(resolvedWindowSize.x - config.WindowPadding.x * 2f, resolvedWindowSize.y - config.WindowPadding.y * 2f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            windowRect.sizeDelta = resolvedWindowSize;

            if (config.ShowCloseButton)
            {
                CloseButtonBuilder.Build(
                    window.transform,
                    () => CloseRequested?.Invoke(this),
                    new CloseButtonBuilder.Options
                    {
                        Font = config.DefaultFont,
                        TextRaycastTarget = false,
                        AnchoredPosition = new Vector2(-4f, -4f),
                        Size = new Vector2(16f, 16f)
                    });
            }

            tooltip = new GameObject("Tooltip", typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            tooltip.transform.SetParent(uiRoot.transform, false);

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
            tooltipNameText.font = config.TooltipNameFont != null ? config.TooltipNameFont : config.DefaultFont;
            tooltipNameText.alignment = TextAnchor.UpperLeft;
            tooltipNameText.color = config.TooltipNameColor;
            tooltipNameText.raycastTarget = false;
            tooltipNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipNameText.verticalOverflow = VerticalWrapMode.Overflow;

            var descGO = new GameObject("Description", typeof(Text));
            descGO.transform.SetParent(tooltip.transform, false);
            tooltipDescriptionText = descGO.GetComponent<Text>();
            tooltipDescriptionText.font = config.TooltipDescriptionFont != null ? config.TooltipDescriptionFont : config.DefaultFont;
            tooltipDescriptionText.alignment = TextAnchor.UpperLeft;
            tooltipDescriptionText.color = config.TooltipDescriptionColor;
            tooltipDescriptionText.supportRichText = true;
            tooltipDescriptionText.raycastTarget = false;
            tooltipDescriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipDescriptionText.verticalOverflow = VerticalWrapMode.Overflow;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            tooltipRect.pivot = new Vector2(0f, 1f);
            tooltip.SetActive(false);

            Font menuFont = config.StackCountFont != null ? config.StackCountFont : config.DefaultFont;
            itemContextMenu = InventoryItemContextMenu.Create(uiRoot.transform, menuFont);
            if (itemContextMenu != null)
                itemContextMenu.SelectionRequested += HandleItemContextMenuSelection;

            if (config.UseSharedRoot)
                SharedCanvasRoot = uiRoot.transform;
        }

        private void DestroyUi()
        {
            if (uiRoot != null)
            {
                UnityEngine.Object.Destroy(uiRoot);
                uiRoot = null;
            }

            if (itemContextMenu != null)
            {
                itemContextMenu.SelectionRequested -= HandleItemContextMenuSelection;
                itemContextMenu = null;
            }

            slotImages = null;
            slotCountTexts = null;
            slotHighlights = null;
            tooltip = null;
            tooltipNameText = null;
            tooltipDescriptionText = null;
        }

        private void CreateDragIcon(InventoryEntry entry)
        {
            DestroyActiveDragIcon();

            activeDragIcon = new GameObject("DraggingIcon", typeof(Image), typeof(Canvas));
            var dragCanvas = activeDragIcon.GetComponent<Canvas>();
            dragCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            dragCanvas.overrideSorting = true;
            dragCanvas.sortingOrder = short.MaxValue;

            Transform parent = SharedCanvasRoot != null && SharedCanvasRoot.gameObject.activeInHierarchy
                ? SharedCanvasRoot
                : uiRoot.transform;
            activeDragIcon.transform.SetParent(parent, false);
            activeDragIcon.transform.SetAsLastSibling();

            var img = activeDragIcon.GetComponent<Image>();
            img.raycastTarget = false;
            Sprite dragSprite = entry.item.GetIconForCount(entry.count);
            if (dragSprite == null)
                dragSprite = entry.item.icon != null ? entry.item.icon : config.SlotFrameSprite;
            if (dragSprite == null)
                dragSprite = config.SlotFrameSprite;
            img.sprite = dragSprite;
            img.color = Color.white;
            var rect = activeDragIcon.GetComponent<RectTransform>();
            rect.sizeDelta = config.SlotSize;
        }

        private void EndDragInternal()
        {
            if (activeDragController != null && activeDragIndex >= 0)
                activeDragController.RefreshSlot(activeDragIndex);

            DestroyActiveDragIcon();

            if (activeDragController != null)
                DragCancelled?.Invoke(activeDragController);

            activeDragController = null;
            activeDragIndex = -1;
        }

        private static void DestroyActiveDragIcon()
        {
            if (activeDragIcon != null)
            {
                UnityEngine.Object.Destroy(activeDragIcon);
                activeDragIcon = null;
            }
        }

        private string FormatStackCount(int count, out Color color)
        {
            if (count < 10000)
            {
                color = config.StackColorDefault;
                return count.ToString();
            }

            if (count >= 1000000000)
            {
                color = config.StackColor100m;
                return (count / 1000000000) + "b";
            }

            if (count >= 100000000)
            {
                color = config.StackColor100m;
                return (count / 1000000) + "m";
            }

            if (count >= 10000000)
            {
                color = config.StackColor10m;
                return (count / 1000000) + "m";
            }

            if (count >= 1000000)
            {
                color = config.StackColor100k;
                return (count / 1000000) + "m";
            }

            if (count >= 100000)
            {
                color = config.StackColor100k;
                return (count / 1000) + "k";
            }

            color = config.StackColor10k;
            return (count / 1000) + "k";
        }

        private static string BuildTooltipDescription(ItemData item)
        {
            if (item == null)
                return string.Empty;

            if (item.healAmount > 0)
                return $"Heals <color=#FF0000>+{item.healAmount}</color> hp";

            return item.description;
        }

        private static bool IsShiftHeld()
        {
            bool shift = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            shift |= Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
                shift |= keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
#endif
            return shift;
        }
    }

    /// <summary>
    /// Selection identifiers raised by <see cref="InventoryDropMenu"/>.
    /// </summary>
    public enum DropMenuSelection
    {
        DropOne,
        DropAll
    }
}
