/// Feature: Added pointer-button aware callbacks for companion ground item pickup commands.
using System;
using System.Collections.Generic;
using UI;
using UI.ContextMenus;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory.GroundItems
{
    /// <summary>
    /// Runtime-built OSRS-style dropdown that lists all ground items on a tile.
    /// Appears near the cursor, keeps text within screen bounds, and closes when the
    /// player clicks elsewhere or the manager hides it.
    /// </summary>
    [DisallowMultipleComponent]
    public class GroundItemPickupMenu : ContextMenuBase
    {
        private class OptionEntry
        {
            public Button Button;
            public Text Label;
            public ItemPickup Pickup;
        }

        [Header("Visuals")]
        [SerializeField]
        [Tooltip("Base background colour for the dropdown panel.")]
        private Color backgroundColor = new Color(0f, 0f, 0f, 0.92f);

        [SerializeField]
        [Tooltip("Minimum width for the dropdown to mirror the OSRS context menu feel.")]
        private float minimumWidth = 180f;

        [SerializeField]
        [Tooltip("Uniform scale applied to the entire menu. Use values below 1 to shrink the window.")]
        private float menuScale = 0.5f;

        [SerializeField]
        [Tooltip("Vertical spacing between options.")]
        private float optionSpacing = 2f;

        [SerializeField]
        [Tooltip("Pixel padding applied around the menu content.")]
        private Vector2Int optionPadding = new Vector2Int(4, 4);

        [SerializeField]
        [Tooltip("Height of each selectable option in pixels.")]
        private float optionHeight = 24f;

        private readonly List<OptionEntry> optionEntries = new List<OptionEntry>();
        private readonly List<ItemPickup> currentPickups = new List<ItemPickup>();
        private readonly Vector3[] worldCorners = new Vector3[4];

        private Canvas canvas;
        private RectTransform menuRect;
        private VerticalLayoutGroup layoutGroup;
        private ContentSizeFitter contentSizeFitter;

        private Action<ItemPickup, PointerEventData.InputButton> onOptionSelected;
        private Vector2 lastRequestedScreenPosition;
        
        /// <summary>Pixels of cursor leeway before the menu auto-closes.</summary>
        public float SafePadding
        {
            get => SafePaddingPixels;
            set => SafePaddingPixels = value;
        }

        /// <summary>Invoked whenever the menu hides itself for any reason.</summary>
        public event Action MenuHidden;

        protected override void Awake()
        {
            base.Awake();
            canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            if (TryGetComponent(out CanvasScaler scaler) == false)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            }

            if (!TryGetComponent(out GraphicRaycaster _))
                gameObject.AddComponent<GraphicRaycaster>();

            var rootRect = GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);

            BuildMenuRoot();
            AssignCanvas(canvas);
            SetMenuRectTransform(menuRect);
            gameObject.SetActive(false);
        }

        private void OnValidate()
        {
            if (menuRect == null)
                return;

            ApplyMenuScale();
        }

        /// <summary>Displays the menu using the supplied pickup list.</summary>
        public void Show(
            IReadOnlyList<ItemPickup> pickups,
            Vector2 screenPosition,
            Action<ItemPickup, PointerEventData.InputButton> onOptionSelected)
        {
            if (pickups == null || pickups.Count == 0)
            {
                Hide();
                return;
            }

            this.onOptionSelected = onOptionSelected;
            currentPickups.Clear();
            CopyIntoCurrentPickups(pickups);
            lastRequestedScreenPosition = screenPosition;

            gameObject.SetActive(true);
            DeferSafeZoneCheck();
            RebuildOptions();
            PositionMenu(lastRequestedScreenPosition);
            transform.SetAsLastSibling();
        }

        /// <summary>Updates the menu to reflect a new list of pickups while open.</summary>
        public void RefreshFrom(IReadOnlyList<ItemPickup> pickups)
        {
            if (!gameObject.activeSelf)
                return;

            currentPickups.Clear();
            if (pickups != null)
                CopyIntoCurrentPickups(pickups);

            if (currentPickups.Count == 0)
            {
                Hide();
                return;
            }

            RebuildOptions();
            PositionMenu(lastRequestedScreenPosition);
        }

        /// <summary>Hides the menu and clears state.</summary>
        public void Hide()
        {
            if (!gameObject.activeSelf)
                return;

            gameObject.SetActive(false);
            currentPickups.Clear();
            onOptionSelected = null;

            foreach (var entry in optionEntries)
            {
                entry.Pickup = null;
                entry.Button.gameObject.SetActive(false);
            }

            MenuHidden?.Invoke();
        }

        /// <inheritdoc />
        protected override void OnCloseRequested()
        {
            Hide();
        }

        private void BuildMenuRoot()
        {
            var menuGO = new GameObject("Menu", typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            menuGO.transform.SetParent(transform, false);
            menuRect = menuGO.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(0f, 1f);
            menuRect.anchorMax = new Vector2(0f, 1f);
            menuRect.pivot = new Vector2(0f, 1f);
            menuRect.sizeDelta = new Vector2(minimumWidth, 0f);

            ApplyMenuScale();

            var image = menuGO.GetComponent<Image>();
            image.color = backgroundColor;

            layoutGroup = menuGO.GetComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.UpperLeft;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = true;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.spacing = optionSpacing;
            layoutGroup.padding = new RectOffset(optionPadding.x, optionPadding.x, optionPadding.y, optionPadding.y);

            contentSizeFitter = menuGO.GetComponent<ContentSizeFitter>();
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RebuildOptions()
        {
            EnsureOptionPool(currentPickups.Count);

            for (int i = 0; i < optionEntries.Count; i++)
            {
                OptionEntry entry = optionEntries[i];
                if (i < currentPickups.Count)
                {
                    entry.Pickup = currentPickups[i];
                    entry.Label.text = BuildLabel(entry.Pickup);
                    entry.Button.gameObject.SetActive(true);
                }
                else
                {
                    entry.Pickup = null;
                    entry.Button.gameObject.SetActive(false);
                }
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(menuRect);
        }

        private void EnsureOptionPool(int required)
        {
            while (optionEntries.Count < required)
            {
                optionEntries.Add(CreateOptionEntry());
            }
        }

        private OptionEntry CreateOptionEntry()
        {
            var optionGO = new GameObject($"Option_{optionEntries.Count}", typeof(Image), typeof(Button), typeof(LayoutElement));
            optionGO.transform.SetParent(menuRect, false);

            var img = optionGO.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);

            var layoutElement = optionGO.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = optionHeight;
            layoutElement.minHeight = optionHeight;
            layoutElement.preferredWidth = minimumWidth;

            var button = optionGO.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;

            var colors = button.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = new Color(0.2f, 0.2f, 0.2f, 0.75f);
            colors.pressedColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0f, 0f, 0f, 0.4f);
            button.colors = colors;

            var textGO = new GameObject("Label", typeof(Text));
            textGO.transform.SetParent(optionGO.transform, false);
            var text = textGO.GetComponent<Text>();
            LegacyFontProvider.ApplyTo(text);
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.fontSize = 18;

            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = new Vector2(-6f, 0f);

            var entry = new OptionEntry
            {
                Button = button,
                Label = text,
                Pickup = null
            };

            var forwarder = optionGO.AddComponent<OptionClickForwarder>();
            forwarder.Initialise(this, entry);

            button.onClick.AddListener(() => HandleOptionPointerClicked(entry, null));

            button.gameObject.SetActive(false);
            return entry;
        }

        /// <summary>
        /// Invokes the pickup callback for the supplied option entry, capturing the pointer button.
        /// Private scope keeps accessibility aligned with the nested <see cref="OptionEntry"/> type.
        /// </summary>
        private void HandleOptionPointerClicked(OptionEntry entry, PointerEventData eventData)
        {
            if (entry == null)
                return;

            if (entry.Pickup == null)
                return;

            var button = eventData != null ? eventData.button : PointerEventData.InputButton.Left;
            onOptionSelected?.Invoke(entry.Pickup, button);
        }

        private string BuildLabel(ItemPickup pickup)
        {
            if (pickup == null)
                return string.Empty;

            string itemName = pickup.Item != null ? pickup.Item.itemName : pickup.name;
            if (pickup.Amount > 1)
                return $"Take {itemName} (x{pickup.Amount})";

            return $"Take {itemName}";
        }

        private void PositionMenu(Vector2 requestedPosition)
        {
            Canvas.ForceUpdateCanvases();

            var canvas = MenuCanvas;
            if (canvas == null)
                return;

            ContextMenuPositioner.PositionMenu(
                menuRect,
                canvas,
                MenuCanvasCamera,
                requestedPosition,
                worldCorners);
        }

        /// <summary>Applies the serialized scale to the menu root so designers can resize it.</summary>
        private void ApplyMenuScale()
        {
            float clampedScale = Mathf.Max(0.01f, menuScale);
            menuRect.localScale = new Vector3(clampedScale, clampedScale, 1f);
        }

        /// <summary>
        /// Copies the provided read-only pickup sequence into the reusable working list.
        /// Utilises span-backed iteration when possible to avoid enumerator allocations.
        /// </summary>
        private void CopyIntoCurrentPickups(IReadOnlyList<ItemPickup> pickups)
        {
            if (pickups == null || pickups.Count == 0)
                return;

            if (pickups is List<ItemPickup> pickupList)
            {
                for (int i = 0; i < pickupList.Count; i++)
                {
                    currentPickups.Add(pickupList[i]);
                }
                return;
            }

            for (int i = 0; i < pickups.Count; i++)
            {
                currentPickups.Add(pickups[i]);
            }
        }

        /// <summary>
        /// Forwarder component that captures pointer button information for each option entry.
        /// </summary>
        private sealed class OptionClickForwarder : MonoBehaviour, IPointerClickHandler
        {
            private GroundItemPickupMenu owner;
            private OptionEntry entry;

            /// <summary>Initialises the forwarder with the owning menu and entry reference.</summary>
            public void Initialise(GroundItemPickupMenu owningMenu, OptionEntry optionEntry)
            {
                owner = owningMenu;
                entry = optionEntry;
            }

            /// <inheritdoc />
            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData == null)
                {
                    owner?.HandleOptionPointerClicked(entry, null);
                    return;
                }

                if (eventData.button == PointerEventData.InputButton.Left)
                    return;

                owner?.HandleOptionPointerClicked(entry, eventData);
            }
        }
    }
}
