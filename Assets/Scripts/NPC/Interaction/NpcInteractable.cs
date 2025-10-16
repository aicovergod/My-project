using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using ShopSystem;
using Pets;
using Combat;
using UI;
using Core.Input;

namespace NPC
{
    /// <summary>
    /// Allows the player to interact with an NPC via right-click context menu.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class NpcInteractable : MonoBehaviour
    {
        [Tooltip("Optional shop component for this NPC.")]
        public Shop shop;

        [Tooltip("Context menu prefab that provides Talk / Open Shop / Examine.")]
        public RightClickMenu menuPrefab;

        [Header("Input")]
        [SerializeField]
        [Tooltip("Player input component providing the interaction action map. Auto-resolved when empty.")]
        private PlayerInput playerInput;

        [SerializeField]
        [Tooltip("Optional override for the OpenMenu action used to display the context menu.")]
        private InputActionReference openMenuActionReference;

        // Shared instance so the menu persists across scene loads
        private static RightClickMenu menuInstance;
        private static Canvas menuCanvas;

        /// <summary>
        ///     Pointer identifier used by the EventSystem for mouse hover checks. Unity removed the
        ///     <c>PointerId</c> helper when consolidating the new input module, but the EventSystem still
        ///     expects <c>-1</c> for the active mouse pointer so we cache the constant locally.
        /// </summary>
        private const int MousePointerEventSystemId = -1;

        private InputAction openMenuAction;
        private bool openMenuActionOwned;
        private bool pointerHovering;
        private bool hasPendingOpenMenuRequest;
        private Vector2 pendingScreenPosition;
        private bool pendingCameFromPointerDevice;
        private bool pendingCameFromTouch;
        private int pendingTouchId = -1;

        private void Awake()
        {
            if (shop == null)
                shop = GetComponent<Shop>();
        }

        private void OnEnable()
        {
            pointerHovering = false;
            SubscribeToInput();
        }

        private void OnDisable()
        {
            pointerHovering = false;
            UnsubscribeFromInput();
            ClearPendingOpenMenuRequest();
        }

        private void OnMouseEnter()
        {
            pointerHovering = true;
        }

        private void OnMouseExit()
        {
            pointerHovering = false;
        }

        /// <summary>
        /// Display the NPC context menu (or command a pet attack) when the OpenMenu action is performed.
        /// </summary>
        private void HandleOpenMenu(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            if (!pointerHovering)
                return;

            if (TryQueuePointerOpenMenuRequest(context))
                return;

            if (IsPointerOverUI())
                return;

            Vector2 pointer = InputActionResolver.GetPointerScreenPosition(
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            ProcessOpenMenu(pointer);
        }

        private void Update()
        {
            if (!hasPendingOpenMenuRequest)
                return;

            if (!pointerHovering)
            {
                ClearPendingOpenMenuRequest();
                return;
            }

            bool pointerBlocked = false;
            if (EventSystem.current != null)
            {
                if (pendingCameFromTouch)
                {
                    pointerBlocked = EventSystem.current.IsPointerOverGameObject(pendingTouchId);
                }
                else if (pendingCameFromPointerDevice)
                {
                    pointerBlocked = EventSystem.current.IsPointerOverGameObject(MousePointerEventSystemId);
                }
                else if (IsPointerOverUI())
                {
                    pointerBlocked = true;
                }
            }
            else if (IsPointerOverUI())
            {
                pointerBlocked = true;
            }

            if (pointerBlocked)
            {
                ClearPendingOpenMenuRequest();
                return;
            }

            ProcessOpenMenu(pendingScreenPosition);
            ClearPendingOpenMenuRequest();
        }

        /// <summary>
        /// Ensures the static menu instance exists before attempting to display it.
        /// </summary>
        private bool EnsureMenuInstance()
        {
            if (menuInstance != null)
                return true;

            if (menuPrefab == null)
                menuPrefab = Resources.Load<RightClickMenu>("Interfaces/RightClickMenu");

            if (menuPrefab == null)
            {
                Debug.LogError("RightClickMenu prefab not assigned and could not be loaded.");
                return false;
            }

            var canvasGO = new GameObject("ContextMenuCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            menuCanvas = canvasGO.GetComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            DontDestroyOnLoad(canvasGO);

            menuInstance = Instantiate(menuPrefab, menuCanvas.transform);
            return true;
        }

        private void SubscribeToInput()
        {
            UnsubscribeFromInput();

            if (playerInput == null)
                playerInput = FindObjectOfType<PlayerInput>();

            openMenuAction = InputActionResolver.Resolve(playerInput, openMenuActionReference, "OpenMenu",
                out openMenuActionOwned);
            if (openMenuAction != null)
                openMenuAction.performed += HandleOpenMenu;
        }

        private void UnsubscribeFromInput()
        {
            if (openMenuAction != null)
            {
                openMenuAction.performed -= HandleOpenMenu;
                if (openMenuActionOwned)
                    openMenuAction.Disable();
                openMenuAction = null;
                openMenuActionOwned = false;
            }
        }

        /// <summary>
        ///     Attempts to queue a pointer-driven open menu request so UI blocking can be re-evaluated safely in Update.
        /// </summary>
        private bool TryQueuePointerOpenMenuRequest(InputAction.CallbackContext context)
        {
            if (context.control == null)
                return false;

            if (context.control.parent is TouchControl touchControl)
            {
                int touchId = touchControl.touchId.ReadValue();
                QueuePendingOpenMenuRequest(
                    touchControl.position.ReadValue(),
                    cameFromPointerDevice: true,
                    cameFromTouch: true,
                    touchId: touchId);
                return true;
            }

            if (context.control.device is Pointer pointer && !(pointer is Touchscreen))
            {
                QueuePendingOpenMenuRequest(
                    pointer.position.ReadValue(),
                    cameFromPointerDevice: true,
                    cameFromTouch: false,
                    touchId: -1);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Stores the pending data associated with a pointer-triggered open menu request.
        /// </summary>
        private void QueuePendingOpenMenuRequest(Vector2 screenPosition, bool cameFromPointerDevice, bool cameFromTouch, int touchId)
        {
            pendingScreenPosition = screenPosition;
            pendingCameFromPointerDevice = cameFromPointerDevice;
            pendingCameFromTouch = cameFromTouch;
            pendingTouchId = touchId;
            hasPendingOpenMenuRequest = true;
        }

        /// <summary>
        ///     Clears any cached pointer interaction data to avoid leaking callbacks across disable/enable cycles.
        /// </summary>
        private void ClearPendingOpenMenuRequest()
        {
            hasPendingOpenMenuRequest = false;
            pendingScreenPosition = default;
            pendingCameFromPointerDevice = false;
            pendingCameFromTouch = false;
            pendingTouchId = -1;
        }

        /// <summary>
        ///     Executes the pet attack fallback or shows the NPC context menu at the specified screen position.
        /// </summary>
        private void ProcessOpenMenu(Vector2 screenPosition)
        {
            var combatTarget = GetComponent<CombatTarget>();
            if (!PetDropSystem.GuardModeEnabled && PetDropSystem.ActivePetCombat != null && combatTarget != null)
            {
                PetDropSystem.ActivePetCombat.CommandAttack(combatTarget, true);
                return;
            }

            if (!EnsureMenuInstance())
                return;

            menuInstance.Show(this, screenPosition);
        }

        /// <summary>
        ///     Determines whether the current pointer is hovering UI that should prevent NPC interactions.
        /// </summary>
        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null)
                return false;

            // Evaluate active touches first so mobile presses correctly block world interactions.
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var touchControl = touches[i];
                    if (!touchControl.press.isPressed)
                        continue;

                    if (EventSystem.current.IsPointerOverGameObject(touchControl.touchId.ReadValue()))
                        return true;
                }
            }

            // If a mouse or pen pointer is available, rely on the default EventSystem behaviour.
            Pointer pointer = Pointer.current;
            if (pointer != null && !(pointer is Touchscreen))
                return EventSystem.current.IsPointerOverGameObject(MousePointerEventSystemId);

            return false;
        }

        public virtual void Talk()
        {
            Debug.Log($"{name} has nothing to say yet.");
        }

        public void OpenShop()
        {
            if (shop == null) return;
            var ui = ShopUI.Instance;
            if (ui != null)
            {
                ui.Open(shop, GetComponent<NpcWanderer>());
            }
        }

        public void Examine()
        {
            Debug.Log($"You examine {name}.");
        }

        public void AttackWithPet()
        {
            var pet = PetDropSystem.ActivePetCombat;
            var target = GetComponent<CombatTarget>();
            if (pet != null && target != null)
                pet.CommandAttack(target, true);
        }
    }
}
