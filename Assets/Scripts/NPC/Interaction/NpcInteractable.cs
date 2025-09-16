using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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

        private InputAction openMenuAction;
        private bool openMenuActionOwned;
        private bool pointerHovering;

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

            if (IsPointerOverUI())
                return;

            var combatTarget = GetComponent<CombatTarget>();
            if (!PetDropSystem.GuardModeEnabled && PetDropSystem.ActivePetCombat != null && combatTarget != null)
            {
                PetDropSystem.ActivePetCombat.CommandAttack(combatTarget);
                return;
            }

            if (!EnsureMenuInstance())
                return;

            Vector2 pointer = InputActionResolver.GetPointerScreenPosition(
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            menuInstance.Show(this, pointer);
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
                return EventSystem.current.IsPointerOverGameObject();

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
                pet.CommandAttack(target);
        }
    }
}
