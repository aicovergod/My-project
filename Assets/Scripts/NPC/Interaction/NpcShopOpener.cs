using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
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
            SubscribeToInput();
        }

        private void OnDisable()
        {
            pointerHovering = false;
            UnsubscribeFromInput();
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
                PetDropSystem.ActivePetCombat.CommandAttack(combatTarget);
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
        ///     Evaluates whether the active pointer is currently hovering UI that should block world interactions.
        /// </summary>
        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null)
                return false;

            if (!(EventSystem.current.currentInputModule is InputSystemUIInputModule module))
                return false;

            Pointer pointer = Pointer.current;
            if (pointer == null)
                return false;

            return module.IsPointerOverGameObject(pointer.pointerId);
        }
    }
}
