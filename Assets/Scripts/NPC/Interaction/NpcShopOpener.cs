using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ShopSystem;
using Combat;
using Pets;
using Core.Input;

namespace NPC
{
    /// <summary>
    /// Opens an NPC's shop either directly on right-click or via context menu through <see cref="NpcInteractable"/>.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class NpcShopOpener : MonoBehaviour
    {
        [Tooltip("Shop component for this NPC. If not assigned, will look on this GameObject.")]
        public Shop shop;

        [Tooltip("If true, open the shop immediately when right-clicked.")]
        public bool openDirectly;

        [Header("Input")]
        [SerializeField]
        [Tooltip("Player input component providing the interaction map. Auto-resolved when empty.")]
        private PlayerInput playerInput;

        [SerializeField]
        [Tooltip("Optional override for the OpenMenu action used for direct shop opening.")]
        private InputActionReference openMenuActionReference;

        private NpcInteractable interactable;
        private InputAction openMenuAction;
        private bool openMenuActionOwned;
        private bool pointerHovering;

        private void Awake()
        {
            if (shop == null)
                shop = GetComponent<Shop>();
            interactable = GetComponent<NpcInteractable>();
            if (!openDirectly && interactable != null)
                interactable.shop = shop;
        }

        private void OnEnable()
        {
            pointerHovering = false;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SubscribeToInput();
        }

        private void OnDisable()
        {
            pointerHovering = false;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeFromInput();
        }

        /// <summary>
        ///     Re-evaluates the player input bindings whenever a new scene loads so NPCs
        ///     automatically reconnect after the player object spawns.
        /// </summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SubscribeToInput();
        }

        private void OnMouseEnter()
        {
            pointerHovering = true;
        }

        private void OnMouseExit()
        {
            pointerHovering = false;
        }

        private void SubscribeToInput()
        {
            UnsubscribeFromInput();

            if (!openDirectly)
                return;

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
        /// Trigger shop opening (or pet commands) when the input action fires.
        /// </summary>
        private void HandleOpenMenu(InputAction.CallbackContext context)
        {
            if (!openDirectly || !context.performed)
                return;

            if (!pointerHovering)
                return;

            if (IsPointerOverUI())
                return;

            var combatTarget = GetComponent<CombatTarget>();
            if (!PetDropSystem.GuardModeEnabled && PetDropSystem.ActivePetCombat != null && combatTarget != null)
            {
                PetDropSystem.ActivePetCombat.CommandAttack(combatTarget, true);
                return;
            }

            OpenShop();
        }

        public void OpenShop()
        {
            if (shop == null) return;
            var ui = ShopUI.Instance;
            if (ui != null)
                ui.Open(shop, GetComponent<NpcWanderer>());
        }

        /// <summary>
        ///     Shared buffer for UI raycasts so diagnostics can reuse the filtered hit list without allocations.
        /// </summary>
        private static readonly List<RaycastResult> pointerRaycastResults = new List<RaycastResult>(8);

        /// <summary>
        ///     Cached pointer event data instance aligned with the active event system for repeated raycasts.
        /// </summary>
        private static PointerEventData sharedPointerEventData;

        /// <summary>
        ///     Tracks which event system owns <see cref="sharedPointerEventData"/> so we can rebuild it when scenes swap.
        /// </summary>
        private static EventSystem sharedPointerEventSystem;

        /// <summary>
        ///     Evaluates whether the active pointer is currently hovering UI that should block world interactions.
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

                    Vector2 touchPosition = touchControl.position.ReadValue();
                    if (TryRaycastPointerUI(touchPosition, out _))
                        return true;
                }
            }

            // Evaluate mouse or pen pointers through the same filtered raycast path.
            Pointer pointer = Pointer.current;
            if (pointer != null && !(pointer is Touchscreen))
            {
                Vector2 pointerPosition = pointer.position.ReadValue();
                if (TryRaycastPointerUI(pointerPosition, out _))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Performs a UI raycast using the shared buffer and strips physics raycaster hits so only UI blocks interactions.
        /// </summary>
        private static bool TryRaycastPointerUI(Vector2 screenPosition, out List<RaycastResult> hits)
        {
            hits = pointerRaycastResults;
            hits.Clear();

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            if (sharedPointerEventData == null || sharedPointerEventSystem != eventSystem)
            {
                sharedPointerEventData = new PointerEventData(eventSystem);
                sharedPointerEventSystem = eventSystem;
            }
            else
                sharedPointerEventData.Reset();

            sharedPointerEventData.position = screenPosition;
            eventSystem.RaycastAll(sharedPointerEventData, hits);

            for (int i = hits.Count - 1; i >= 0; i--)
            {
                BaseRaycaster module = hits[i].module;
                if (module is PhysicsRaycaster || module is Physics2DRaycaster)
                    hits.RemoveAt(i);
            }

            return hits.Count > 0;
        }
    }
}
