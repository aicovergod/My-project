using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using ShopSystem;
using Pets;
using Companions;
using Combat;
using Player;
using UI;
using UI.Utilities;
using Util;
using Core.Input;
using Skills.Thieving;
using Skills.Thieving.Core;

namespace NPC
{
    /// <summary>
    /// Allows the player to interact with an NPC via right-click context menu.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(NpcInteractionOptions))]
    public partial class NpcInteractable : MonoBehaviour
    {
        [Tooltip("Optional shop component for this NPC.")]
        public Shop shop;

        [Header("Input")]
        [SerializeField]
        [Tooltip("Player input component providing the interaction action map. Auto-resolved when empty.")]
        private PlayerInput playerInput;

        [SerializeField]
        [Tooltip("Optional override for the OpenMenu action used to display the context menu.")]
        private InputActionReference openMenuActionReference;

        [SerializeField]
        [Tooltip("Per-NPC configuration that decides which options appear in the context menu.")]
        private NpcInteractionOptions interactionOptions;

        // Shared instance so the menu persists across scene loads
        private static RightClickMenu menuInstance;

        private InputAction openMenuAction;
        private bool openMenuActionOwned;
        private bool pointerHovering;
        private bool hasPendingOpenMenuRequest;
        private Vector2 pendingScreenPosition;
        private bool pendingCameFromPointerDevice;
        private bool pendingCameFromTouch;
        private int pendingTouchId = -1;

        // Cached list used to capture UI raycast hits without allocating every frame.
        private readonly List<RaycastResult> pointerRaycastResults = new();

        private void Awake()
        {
            if (shop == null)
                shop = GetComponent<Shop>();

            if (interactionOptions == null)
                interactionOptions = GetComponent<NpcInteractionOptions>();
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

            string context = pendingCameFromTouch
                ? $"TouchOpenMenu(id:{pendingTouchId})"
                : pendingCameFromPointerDevice
                    ? "PointerOpenMenu"
                    : "OpenMenu";

            if (TryRaycastPointerUI(pendingScreenPosition, out var uiHits))
            {
                LogPointerBlocked(context, pendingScreenPosition, uiHits);
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

            var overlay = OverlayCanvasFactory.CreateOverlayCanvas(
                "NpcContextMenuCanvas",
                new Vector2(1920f, 1080f),
                dontDestroyOnLoad: true,
                assignToUiLayer: true);

            menuInstance = RightClickMenu.Create(overlay.Root.transform);
            return menuInstance != null;
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
        ///     Displays the NPC context menu at the specified screen position.
        /// </summary>
        private void ProcessOpenMenu(Vector2 screenPosition)
        {
            if (!EnsureMenuInstance())
                return;

            if (interactionOptions == null)
            {
                Debug.LogWarning($"{name} is missing NpcInteractionOptions and cannot display a context menu.", this);
                return;
            }

            menuInstance.Show(this, screenPosition, interactionOptions);
        }

        /// <summary>
        ///     Performs a UI raycast at the supplied screen position using the cached buffer.
        /// </summary>
        private bool TryRaycastPointerUI(Vector2 screenPosition, out List<RaycastResult> hits)
        {
            hits = pointerRaycastResults;
            hits.Clear();

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            var pointerEventData = new PointerEventData(eventSystem)
            {
                position = screenPosition
            };

            eventSystem.RaycastAll(pointerEventData, hits);

            // Remove world collider hits so only genuine UI blocks the interaction.
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                BaseRaycaster module = hits[i].module;
                if (module is PhysicsRaycaster || module is Physics2DRaycaster)
                    hits.RemoveAt(i);
            }

            return hits.Count > 0;
        }

        /// <summary>
        ///     Logs detailed diagnostics for pointer interactions that are blocked by UI elements.
        /// </summary>
        private void LogPointerBlocked(string context, Vector2 screenPosition, List<RaycastResult> uiHits)
        {
            if (uiHits == null || uiHits.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append('[').Append(GetType().Name).Append("] Pointer blocked (").Append(context)
              .Append(") at screen position ").Append(screenPosition).AppendLine(".");

            sb.Append("UI Raycast Hits (").Append(uiHits.Count).AppendLine("):");
            for (int i = 0; i < uiHits.Count; i++)
            {
                RaycastResult hit = uiHits[i];
                if (hit.gameObject == null)
                    continue;

                string layerName = FormatLayerName(hit.gameObject.layer);
                string moduleName = hit.module != null ? hit.module.GetType().Name : "UnknownModule";
                sb.Append("  ").Append(i + 1).Append(": ")
                  .Append(hit.gameObject.name)
                  .Append(" [Layer: ").Append(layerName).Append("] via ")
                  .Append(moduleName);

                if (hit.module != null && hit.module.eventCamera != null)
                    sb.Append(" (Camera: ").Append(hit.module.eventCamera.name).Append(')');

                sb.AppendLine();
            }

            Camera activeCamera = Camera.main;
            if (activeCamera != null)
            {
                Vector3 worldPoint3 = activeCamera.ScreenToWorldPoint(screenPosition);
                Vector2 worldPoint = new Vector2(worldPoint3.x, worldPoint3.y);
                sb.Append("Physics2D.OverlapPointAll at world ").Append(worldPoint).AppendLine(":");

                var colliders = Physics2D.OverlapPointAll(worldPoint);
                if (colliders.Length == 0)
                {
                    sb.AppendLine("  (none)");
                }
                else
                {
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        var collider = colliders[i];
                        if (collider == null)
                            continue;

                        string layerName = FormatLayerName(collider.gameObject.layer);
                        sb.Append("  ").Append(i + 1).Append(": ")
                          .Append(collider.name)
                          .Append(" [Layer: ").Append(layerName).Append("] ")
                          .Append(collider.GetType().Name)
                          .AppendLine();
                    }
                }
            }
            else
            {
                sb.AppendLine("No main camera available; skipped Physics2D.OverlapPointAll diagnostics.");
            }

            Debug.Log(sb.ToString(), this);
        }

        /// <summary>
        ///     Formats layer information to include both the human-readable name and numeric identifier when available.
        /// </summary>
        private static string FormatLayerName(int layer)
        {
            string layerName = LayerMask.LayerToName(layer);
            return string.IsNullOrEmpty(layerName) ? layer.ToString() : $"{layerName} ({layer})";
        }

        /// <summary>
        ///     Determines whether the current pointer is hovering UI that should prevent NPC interactions.
        /// </summary>
        private bool IsPointerOverUI()
        {
            if (EventSystem.current == null)
                return false;

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
                    if (TryRaycastPointerUI(touchPosition, out var uiHits))
                    {
                        LogPointerBlocked("TouchCheck", touchPosition, uiHits);
                        return true;
                    }
                }
            }

            Pointer pointer = Pointer.current;
            if (pointer != null && !(pointer is Touchscreen))
            {
                Vector2 pointerPosition = pointer.position.ReadValue();
                if (TryRaycastPointerUI(pointerPosition, out var uiHits))
                {
                    LogPointerBlocked("PointerCheck", pointerPosition, uiHits);
                    return true;
                }
            }

            return false;
        }

        public virtual void Talk()
        {
            Debug.Log($"{name} has nothing to say yet.");
        }

        /// <summary>
        ///     Opens the NPC's shop, delegating to <see cref="NpcShopOpener"/> when available so
        ///     the auto-walk coroutine ushers the player into range before showing the UI.
        /// </summary>
        public void OpenShop()
        {
            if (TryGetComponent(out NpcShopOpener shopOpener) && shopOpener != null)
            {
                shopOpener.OpenShop();
                return;
            }

            if (shop == null)
                return;

            var ui = ShopUI.Instance;
            if (ui != null)
            {
                ui.Open(shop, GetComponent<NpcWanderer>());
            }
        }

        /// <summary>
        ///     Determines whether this NPC currently supports player-initiated combat via the context menu.
        /// </summary>
        public bool CanPlayerAttack()
        {
            if (interactionOptions == null || !interactionOptions.IsAttackEnabled)
                return false;

            if (!TryResolveCombatTarget(out _))
                return false;

            if (!TryGetComponent(out NpcAttackOnClick attackComponent) || attackComponent == null)
                return false;

            return attackComponent.isActiveAndEnabled;
        }

        /// <summary>
        ///     Attempts to command the player to attack this NPC using the standard combat pipeline.
        /// </summary>
        /// <returns>True when an attack command was issued or queued.</returns>
        public bool TryCommandPlayerAttack()
        {
            if (!CanPlayerAttack())
                return false;

            if (!TryGetComponent(out NpcAttackOnClick attackComponent) || attackComponent == null)
                return false;

            return attackComponent.TryCommandPlayerAttack(true);
        }

        /// <summary>
        ///     Evaluates whether a combat pet or companion can receive a manual attack command for this NPC.
        /// </summary>
        /// <param name="type">Outputs the follower type capable of attacking.</param>
        /// <param name="label">Outputs the localized label that should be displayed in the menu.</param>
        /// <returns>True when a pet or companion is ready to accept the command.</returns>
        public bool TryGetFollowerAttackOption(out NpcFollowerAttackType type, out string label)
        {
            type = NpcFollowerAttackType.None;
            label = null;

            if (!CanPlayerAttack())
                return false;

            if (!TryResolveCombatTarget(out _))
                return false;

            bool allowPetAttack = interactionOptions == null || interactionOptions.IsPetAttackEnabled;
            bool allowCompanionAttack = interactionOptions == null || interactionOptions.IsCompanionAttackEnabled;

            var petCombat = PetDropSystem.ActivePetCombat;
            if (allowPetAttack && petCombat != null && petCombat.isActiveAndEnabled && petCombat.CanFight && !PetDropSystem.GuardModeEnabled)
            {
                type = NpcFollowerAttackType.Pet;
                label = "Pet Attack";
                return true;
            }

            if (allowCompanionAttack && CompanionManager.HasActiveCompanion && !CompanionManager.GuardModeEnabled && !CompanionManager.IsGuardModeLockedByCombatCooldown)
            {
                var companion = CompanionManager.ActiveCompanion;
                if (companion != null && companion.isActiveAndEnabled && companion.CanFight)
                {
                    type = NpcFollowerAttackType.Companion;
                    label = "Companion Attack";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Issues the follower attack command previously advertised by <see cref="TryGetFollowerAttackOption"/>.
        /// </summary>
        /// <param name="type">Follower that should attack the NPC.</param>
        public void ExecuteFollowerAttack(NpcFollowerAttackType type)
        {
            if (!TryResolveCombatTarget(out var target) || target == null)
                return;

            if (interactionOptions != null)
            {
                if (type == NpcFollowerAttackType.Pet && !interactionOptions.IsPetAttackEnabled)
                    return;

                if (type == NpcFollowerAttackType.Companion && !interactionOptions.IsCompanionAttackEnabled)
                    return;
            }

            switch (type)
            {
                case NpcFollowerAttackType.Pet:
                    var petCombat = PetDropSystem.ActivePetCombat;
                    if (petCombat != null && petCombat.isActiveAndEnabled && petCombat.CanFight && !PetDropSystem.GuardModeEnabled)
                        petCombat.CommandAttack(target, true);
                    break;
                case NpcFollowerAttackType.Companion:
                    CompanionManager.TryCommandAttack(target);
                    break;
            }
        }

        /// <summary>
        ///     Attempts to resolve the <see cref="CombatTarget"/> adapter for this NPC.
        /// </summary>
        private bool TryResolveCombatTarget(out CombatTarget target)
        {
            target = GetComponent<CombatTarget>();
            return target != null && target.IsAlive;
        }

        public void AttackWithPet()
        {
            if (interactionOptions != null && !interactionOptions.IsPetAttackEnabled)
                return;

            ExecuteFollowerAttack(NpcFollowerAttackType.Pet);
        }

        public virtual void Pickpocket()
        {
            if (!PlayerLocator.TryFindPlayer(out var playerObject) || playerObject == null)
            {
                Debug.LogWarning($"{nameof(NpcInteractable)}.{nameof(Pickpocket)} could not locate the player.", this);
                return;
            }

            if (!playerObject.TryGetComponent(out ThievingSkill thievingSkill) || thievingSkill == null)
            {
                Debug.LogWarning($"{nameof(NpcInteractable)}.{nameof(Pickpocket)} missing ThievingSkill on player.", playerObject);
                return;
            }

            if (!TryGetComponent(out NpcThievingTarget thievingTarget) || thievingTarget == null)
            {
                Debug.LogWarning($"{nameof(NpcInteractable)}.{nameof(Pickpocket)} missing {nameof(NpcThievingTarget)}.", this);
                return;
            }

            thievingSkill.TryStartPickpocket(thievingTarget);
        }
    }

    /// <summary>
    /// Enumerates the follower attack commands that can be exposed within the right-click menu.
    /// </summary>
    public enum NpcFollowerAttackType
    {
        None,
        Pet,
        Companion
    }
}
