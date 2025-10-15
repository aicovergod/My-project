using System;
using System.Collections.Generic;
using Core.Input;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Inventory.GroundItems
{
    /// <summary>
    /// Runtime-built OSRS-style dropdown that lists all ground items on a tile.
    /// Appears near the cursor, keeps text within screen bounds, and closes when the
    /// player clicks elsewhere or the manager hides it.
    /// </summary>
    [DisallowMultipleComponent]
    public class GroundItemPickupMenu : MonoBehaviour
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

        [Header("Input")]
        [SerializeField]
        [Tooltip("Input action triggered for primary clicks when the menu should evaluate closing.")]
        private InputActionReference leftClickActionReference;

        [SerializeField]
        [Tooltip("Input action triggered for secondary clicks when the menu should evaluate closing.")]
        private InputActionReference rightClickActionReference;

        [SerializeField]
        [Tooltip("Pointer position action used to track the current cursor location in screen space.")]
        private InputActionReference pointerPositionActionReference;

        private readonly List<OptionEntry> optionEntries = new List<OptionEntry>();
        private readonly List<ItemPickup> currentPickups = new List<ItemPickup>();

        private Canvas canvas;
        private RectTransform menuRect;
        private VerticalLayoutGroup layoutGroup;
        private ContentSizeFitter contentSizeFitter;

        private Action<ItemPickup> onOptionSelected;
        private Vector2 lastRequestedScreenPosition;

        private PlayerInput playerInput;
        private readonly UiInputActionSubscription leftClickSubscription = new UiInputActionSubscription();
        private readonly UiInputActionSubscription rightClickSubscription = new UiInputActionSubscription();
        private readonly UiInputActionSubscription pointerPositionSubscription = new UiInputActionSubscription();

        /// <summary>Pixels of cursor leeway before the menu auto-closes.</summary>
        public float SafePadding { get; set; } = 12f;

        /// <summary>Invoked whenever the menu hides itself for any reason.</summary>
        public event Action MenuHidden;

        private Camera CanvasCamera => canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        private void Awake()
        {
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
            ResolveInputActions();
            gameObject.SetActive(false);
        }

        private void OnValidate()
        {
            if (menuRect == null)
                return;

            ApplyMenuScale();
        }

        private void OnEnable()
        {
            if (leftClickSubscription.Action == null || rightClickSubscription.Action == null ||
                pointerPositionSubscription.Action == null)
            {
                ResolveInputActions();
            }

            SubscribeInput();
            if (gameObject.activeSelf)
                EvaluatePointerPosition(GetCurrentPointerPosition(), false);
        }

        private void OnDisable()
        {
            UnsubscribeInput();
        }

        private void OnDestroy()
        {
            leftClickSubscription.Release();
            rightClickSubscription.Release();
            pointerPositionSubscription.Release();
        }

        /// <summary>Displays the menu using the supplied pickup list.</summary>
        public void Show(List<ItemPickup> pickups, Vector2 screenPosition, Action<ItemPickup> onOptionSelected)
        {
            if (pickups == null || pickups.Count == 0)
            {
                Hide();
                return;
            }

            this.onOptionSelected = onOptionSelected;
            currentPickups.Clear();
            currentPickups.AddRange(pickups);
            lastRequestedScreenPosition = screenPosition;

            gameObject.SetActive(true);
            RebuildOptions();
            PositionMenu(lastRequestedScreenPosition);
            transform.SetAsLastSibling();
        }

        /// <summary>Updates the menu to reflect a new list of pickups while open.</summary>
        public void RefreshFrom(List<ItemPickup> pickups)
        {
            if (!gameObject.activeSelf)
                return;

            currentPickups.Clear();
            if (pickups != null)
                currentPickups.AddRange(pickups);

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

        private void ResolveInputActions()
        {
            if (playerInput == null)
                playerInput = GetComponentInParent<PlayerInput>();

            if (playerInput == null)
                playerInput = FindObjectOfType<PlayerInput>();

            leftClickSubscription.Resolve(playerInput, leftClickActionReference, "UI/Click");
            rightClickSubscription.Resolve(playerInput, rightClickActionReference, "UI/RightClick");
            pointerPositionSubscription.Resolve(playerInput, pointerPositionActionReference, "UI/Point");
        }

        private void SubscribeInput()
        {
            leftClickSubscription.Subscribe(HandleLeftClickPerformed);
            rightClickSubscription.Subscribe(HandleRightClickPerformed);
            pointerPositionSubscription.Subscribe(HandlePointerMoved);
        }

        private void UnsubscribeInput()
        {
            leftClickSubscription.Unsubscribe(HandleLeftClickPerformed);
            rightClickSubscription.Unsubscribe(HandleRightClickPerformed);
            pointerPositionSubscription.Unsubscribe(HandlePointerMoved);
        }

        private void HandleLeftClickPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !gameObject.activeSelf)
                return;

            EvaluatePointerPosition(GetCurrentPointerPosition(), true);
        }

        private void HandleRightClickPerformed(InputAction.CallbackContext context)
        {
            if (!context.performed || !gameObject.activeSelf)
                return;

            EvaluatePointerPosition(GetCurrentPointerPosition(), true);
        }

        private void HandlePointerMoved(InputAction.CallbackContext context)
        {
            if (!gameObject.activeSelf)
                return;

            Vector2 pointerPosition = context.ReadValue<Vector2>();
            EvaluatePointerPosition(pointerPosition, false);
        }

        private void EvaluatePointerPosition(Vector2 pointerPosition, bool triggeredByClick)
        {
            bool insideMenu;
            bool withinSafeZone = IsCursorWithinSafeZone(pointerPosition, out insideMenu);
            if (!withinSafeZone)
            {
                Hide();
                return;
            }

            if (triggeredByClick && !insideMenu)
                Hide();
        }

        private Vector2 GetCurrentPointerPosition()
        {
            if (pointerPositionSubscription.Action != null)
                return pointerPositionSubscription.Action.ReadValue<Vector2>();

            return InputActionResolver.GetPointerScreenPosition(lastRequestedScreenPosition);
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

            button.onClick.AddListener(() => OnOptionClicked(entry));
            button.gameObject.SetActive(false);
            return entry;
        }

        private void OnOptionClicked(OptionEntry entry)
        {
            if (entry?.Pickup == null)
                return;

            onOptionSelected?.Invoke(entry.Pickup);
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
            LayoutRebuilder.ForceRebuildLayoutImmediate(menuRect);

            Vector2 size = menuRect.rect.size;
            size.x = Mathf.Max(size.x, minimumWidth);
            Vector3 lossyScale = menuRect.lossyScale;
            Vector2 scaledSize = new Vector2(size.x * lossyScale.x, size.y * lossyScale.y);

            float minX = 0f;
            float maxX = Mathf.Max(minX, Screen.width - scaledSize.x);
            float minY = Mathf.Min(Screen.height, scaledSize.y);
            float maxY = Screen.height;

            Vector2 clamped = new Vector2(
                Mathf.Clamp(requestedPosition.x, minX, maxX),
                Mathf.Clamp(requestedPosition.y, minY, maxY));

            menuRect.position = clamped;
        }

        private bool IsCursorWithinSafeZone(Vector2 pointerPosition, out bool insideMenu)
        {
            insideMenu = RectTransformUtility.RectangleContainsScreenPoint(menuRect, pointerPosition, CanvasCamera);

            if (SafePadding <= 0f)
                return insideMenu;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(menuRect, pointerPosition, CanvasCamera, out var local))
                return insideMenu;

            Rect paddedRect = menuRect.rect;
            paddedRect.xMin -= SafePadding;
            paddedRect.xMax += SafePadding;
            paddedRect.yMin -= SafePadding;
            paddedRect.yMax += SafePadding;

            return paddedRect.Contains(local);
        }

        /// <summary>Applies the serialized scale to the menu root so designers can resize it.</summary>
        private void ApplyMenuScale()
        {
            float clampedScale = Mathf.Max(0.01f, menuScale);
            menuRect.localScale = new Vector3(clampedScale, clampedScale, 1f);
        }
    }
}
