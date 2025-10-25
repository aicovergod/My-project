// Assets/Scripts/Inventory/UI/InventoryItemContextMenu.cs
using System;
using System.Collections.Generic;
using UI.ContextMenus;
using UnityEngine;
using UnityEngine.UI;

namespace Inventory.UI
{
    /// <summary>
    /// Action identifiers raised when a context menu option is selected.
    /// </summary>
    public enum InventoryItemContextAction
    {
        Use,
        Eat,
        Equip,
        Drop,
        Transfer,
        Examine
    }

    /// <summary>
    /// OSRS-inspired context menu that surfaces all available actions for an inventory item.
    /// The hierarchy is created entirely in code so windows do not require prefab setup and the
    /// presentation remains consistent with the existing drop quantity menu.
    /// </summary>
    public sealed class InventoryItemContextMenu : ContextMenuBase
    {

        /// <summary>
        /// Represents a single option displayed inside the context menu.
        /// </summary>
        public readonly struct Option
        {
            public Option(string label, InventoryItemContextAction action, bool interactable)
            {
                Label = label ?? throw new ArgumentNullException(nameof(label));
                Action = action;
                Interactable = interactable;
            }

            /// <summary>Visible label rendered on the button.</summary>
            public string Label { get; }

            /// <summary>Action identifier raised when the button is clicked.</summary>
            public InventoryItemContextAction Action { get; }

            /// <summary>True when the option can currently be executed.</summary>
            public bool Interactable { get; }
        }

        /// <summary>Raised whenever the player selects a context menu option.</summary>
        public event Action<InventoryWindowController, int, InventoryItemContextAction, Vector2> SelectionRequested;

        private readonly List<Button> buttons = new();
        private readonly List<Text> buttonLabels = new();

        private InventoryWindowController controller;
        private int slotIndex = -1;
        private Font labelFont;
        private RectTransform menuRect;
        private Vector2 lastPointerPosition;

        /// <summary>
        /// Creates a new context menu instance and parents it under the supplied transform.
        /// </summary>
        public static InventoryItemContextMenu Create(Transform parent, Font font)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            var go = new GameObject("InventoryItemContextMenu", typeof(Image), typeof(InventoryItemContextMenu));
            go.transform.SetParent(parent, false);

            var menu = go.GetComponent<InventoryItemContextMenu>();
            menu.labelFont = font;
            menu.menuRect = go.GetComponent<RectTransform>();
            menu.BuildUserInterface();
            go.SetActive(false);
            return menu;
        }

        /// <summary>
        /// Most context menus are built entirely in code so ensure the canvas reference is assigned here.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            menuRect ??= GetComponent<RectTransform>();
            AssignCanvas(GetComponentInParent<Canvas>());
            SetMenuRectTransform(menuRect);
        }

        /// <summary>Hides the menu when it should be dismissed.</summary>
        protected override void OnCloseRequested()
        {
            Hide();
        }

        /// <summary>Returns the last pointer position supplied to <see cref="Show"/>.</summary>
        public Vector2 LastPointerPosition => lastPointerPosition;

        /// <summary>
        /// Displays the context menu using the provided option list.
        /// </summary>
        /// <param name="owner">Controller that owns the menu.</param>
        /// <param name="index">Slot index associated with the context menu.</param>
        /// <param name="pointerPosition">Screen-space pointer position used for placement.</param>
        /// <param name="options">Collection of menu entries that should be displayed.</param>
        public void Show(
            InventoryWindowController owner,
            int index,
            Vector2 pointerPosition,
            IReadOnlyList<Option> options)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            controller = owner;
            slotIndex = index;
            lastPointerPosition = pointerPosition;

            EnsureButtonPool(options.Count);

            for (int i = 0; i < buttons.Count; i++)
            {
                bool active = i < options.Count;
                var button = buttons[i];
                var label = buttonLabels[i];
                button.gameObject.SetActive(active);
                if (!active)
                    continue;

                var option = options[i];
                label.text = option.Label;
                button.interactable = option.Interactable;

                button.onClick.RemoveAllListeners();
                var capturedAction = option.Action;
                button.onClick.AddListener(() => HandleSelection(capturedAction));
            }

            transform.position = pointerPosition;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            DeferSafeZoneCheck();
        }

        /// <summary>Hides the context menu without destroying pooled buttons.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
            controller = null;
            slotIndex = -1;
        }

        private void BuildUserInterface()
        {
            if (menuRect == null)
                menuRect = GetComponent<RectTransform>();

            menuRect.pivot = new Vector2(0f, 1f);

            var background = GetComponent<Image>();
            background.color = new Color(0.07f, 0.07f, 0.07f, 0.95f);
            background.raycastTarget = true;

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;
            layout.padding = new RectOffset(4, 4, 4, 4);

            var fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void EnsureButtonPool(int required)
        {
            for (int i = buttons.Count; i < required; i++)
            {
                buttons.Add(CreateButton(out var label));
                buttonLabels.Add(label);
            }
        }

        private Button CreateButton(out Text label)
        {
            var buttonGo = new GameObject("Option", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(transform, false);

            var image = buttonGo.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;

            // Ensure the layout group can determine the correct sizing for each option entry.
            var layoutElement = buttonGo.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 140f;
            layoutElement.minWidth = 140f;
            layoutElement.flexibleWidth = 1f;
            layoutElement.preferredHeight = 26f;
            layoutElement.minHeight = 26f;

            var button = buttonGo.GetComponent<Button>();

            var textGo = new GameObject("Label", typeof(Text));
            textGo.transform.SetParent(buttonGo.transform, false);
            label = textGo.GetComponent<Text>();
            label.font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            var labelRect = textGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(-6f, 0f);

            var buttonRect = buttonGo.GetComponent<RectTransform>();
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.sizeDelta = new Vector2(140f, 26f);

            return button;
        }

        private void HandleSelection(InventoryItemContextAction action)
        {
            if (controller == null)
                return;

            var target = controller;
            int index = slotIndex;
            Vector2 pointer = lastPointerPosition;

            Hide();
            SelectionRequested?.Invoke(target, index, action, pointer);
        }
    }
}
