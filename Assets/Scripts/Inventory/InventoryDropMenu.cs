using Core.Input;
using Inventory.UI;
using UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Inventory
{
    /// <summary>
    /// Simple right-click context menu for inventory drop options.
    /// Built entirely in code so no prefab is needed.
    /// </summary>
    public class InventoryDropMenu : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Screen-space padding the cursor can move beyond the menu before the popup auto-closes.")]
        private float closePaddingPixels = 12f;

        [Header("Input")]
        [SerializeField]
        [Tooltip("Input action triggered for primary clicks while the drop menu is visible.")]
        private InputActionReference leftClickActionReference;

        [SerializeField]
        [Tooltip("Input action triggered for secondary clicks while the drop menu is visible.")]
        private InputActionReference rightClickActionReference;

        [SerializeField]
        [Tooltip("Pointer position action supplying the current cursor position in screen space.")]
        private InputActionReference pointerPositionActionReference;

        private InventoryWindowController controller;
        private int slotIndex;
        private Font font;
        private RectTransform rect;
        private Canvas menuCanvas;
        private Camera canvasCamera;
        private PlayerInput playerInput;
        private readonly UiInputActionSubscription leftClickSubscription = new UiInputActionSubscription();
        private readonly UiInputActionSubscription rightClickSubscription = new UiInputActionSubscription();
        private readonly UiInputActionSubscription pointerPositionSubscription = new UiInputActionSubscription();

        public static InventoryDropMenu Create(Transform parent, Font font)
        {
            var go = new GameObject("InventoryDropMenu", typeof(Image), typeof(InventoryDropMenu));
            go.transform.SetParent(parent, false);
            var menu = go.GetComponent<InventoryDropMenu>();
            menu.font = font;
            menu.rect = go.GetComponent<RectTransform>();
            menu.BuildUI();
            go.SetActive(false);
            return menu;
        }

        private void Awake()
        {
            rect ??= GetComponent<RectTransform>();
            menuCanvas = GetComponentInParent<Canvas>();

            // Cache the correct camera reference so hover checks work with any canvas render mode.
            if (menuCanvas != null && menuCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                canvasCamera = menuCanvas.worldCamera;
            }
            else
            {
                canvasCamera = null;
            }

            ResolveInputActions();
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

        /// <summary>
        /// Determines if the cursor remains within the menu rectangle plus a tolerance band to prevent accidental closures.
        /// </summary>
        private bool IsCursorWithinSafeZone(Vector2 pointerPosition, out bool insideMenu)
        {
            insideMenu = RectTransformUtility.RectangleContainsScreenPoint(rect, pointerPosition, canvasCamera);

            if (closePaddingPixels <= 0f)
            {
                return insideMenu;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, pointerPosition, canvasCamera, out var localPoint))
            {
                // If the conversion fails, fall back to the strict rectangle check to preserve existing behaviour.
                return insideMenu;
            }

            var paddedRect = rect.rect;
            paddedRect.xMin -= closePaddingPixels;
            paddedRect.xMax += closePaddingPixels;
            paddedRect.yMin -= closePaddingPixels;
            paddedRect.yMax += closePaddingPixels;

            return paddedRect.Contains(localPoint);
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

            EvaluatePointerPosition(context.ReadValue<Vector2>(), false);
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

            return InputActionResolver.GetPointerScreenPosition(rect.position);
        }

        private void BuildUI()
        {
            var bg = GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.9f);
            rect.pivot = new Vector2(0f, 1f);

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;
            layout.padding = new RectOffset(2, 2, 2, 2);

            CreateButton("Drop 1", () => OnSelection(DropMenuSelection.DropOne));
            CreateButton("Drop All", () => OnSelection(DropMenuSelection.DropAll));
            CreateButton("Drop X", () => OnSelection(DropMenuSelection.DropX));
        }

        private void CreateButton(string label, UnityAction onClick)
        {
            var btnGO = new GameObject(label, typeof(Image), typeof(Button));
            btnGO.transform.SetParent(transform, false);
            var img = btnGO.GetComponent<Image>();
            img.sprite = Resources.Load<Sprite>("Sprites/BankUI/Button_1");
            img.color = new Color(0f, 0f, 0f, 0f);
            var btn = btnGO.GetComponent<Button>();
            btn.onClick.AddListener(onClick);

            var txtGO = new GameObject("Text", typeof(Text));
            txtGO.transform.SetParent(btnGO.transform, false);
            var txt = txtGO.GetComponent<Text>();
            txt.font = font;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = Color.white;
            txt.text = label;

            var btnRect = btnGO.GetComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(100f, 20f);
            btnRect.pivot = new Vector2(0f, 1f);

            var txtRect = txtGO.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
        }

        public void Show(InventoryWindowController controller, int index, Vector2 position)
        {
            this.controller = controller;
            slotIndex = index;
            transform.position = position;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            controller = null;
        }

        private void OnSelection(DropMenuSelection selection)
        {
            controller?.HandleDropMenuSelection(slotIndex, selection);
            Hide();
        }
    }
}
