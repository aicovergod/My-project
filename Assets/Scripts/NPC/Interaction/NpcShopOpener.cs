using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ShopSystem;
using Combat;
using Pets;
using Core.Input;
using Player;

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

        [Header("Trade Range")]
        [SerializeField, Min(0f)]
        [Tooltip("Maximum distance in tiles the player can be from this NPC before the shop opens.")]
        private float tradeRangeTiles = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("Tiles subtracted from the range so auto-walk stops slightly inside the radius.")]
        private float approachStopBufferTiles = 0.1f;

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

        // Tracks the coroutine responsible for guiding the player into the trade radius.
        private Coroutine tradeApproachRoutine;

        // Stores the mover involved with the current trade approach so it can be cancelled cleanly.
        private PlayerMover approachingMover;

        // Project tiles map 1:1 to world units (64x64 pixel sprites per tile).
        private const float TileSize = 1f;

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
            CancelTradeApproach();
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

        /// <summary>
        ///     Opens the shop, optionally guiding the player into the configured trade radius first.
        /// </summary>
        public void OpenShop()
        {
            if (shop == null)
                return;

            CancelTradeApproach();

            if (!TryResolvePlayerMover(out var mover, out var playerTransform))
            {
                OpenShopInternal();
                return;
            }

            float requiredDistance = ResolveTradeRangeWorld();
            if (requiredDistance <= 0f)
            {
                OpenShopInternal();
                return;
            }

            Vector2 npcPosition = transform.position;
            Vector2 playerPosition = playerTransform.position;
            if (Vector2.SqrMagnitude(playerPosition - npcPosition) <= requiredDistance * requiredDistance)
            {
                OpenShopInternal();
                return;
            }

            approachingMover = mover;
            tradeApproachRoutine = StartCoroutine(ApproachAndOpenShopRoutine(mover, playerTransform, requiredDistance));
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
            {
                sharedPointerEventData.Reset();
            }

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

        /// <summary>
        ///     Attempts to resolve the player mover so the NPC can guide the character into trade range.
        /// </summary>
        private static bool TryResolvePlayerMover(out PlayerMover mover, out Transform playerTransform)
        {
            mover = null;
            playerTransform = null;

            if (!PlayerLocator.TryFindPlayer(out var playerObject) || playerObject == null)
                return false;

            if (!playerObject.TryGetComponent(out mover) || mover == null)
                return false;

            playerTransform = mover.transform;
            return playerTransform != null;
        }

        /// <summary>
        ///     Converts the configured trade radius from tiles into world units.
        /// </summary>
        private float ResolveTradeRangeWorld()
        {
            return Mathf.Max(0f, tradeRangeTiles * TileSize);
        }

        /// <summary>
        ///     Calculates the stop distance used by the auto-walk routine so the player ends inside the trade radius.
        /// </summary>
        private float ResolveApproachStopDistance(float requiredDistance)
        {
            float bufferWorld = Mathf.Max(0f, approachStopBufferTiles * TileSize);
            return Mathf.Max(0f, requiredDistance - bufferWorld);
        }

        /// <summary>
        ///     Guides the player into the trade radius and opens the shop once close enough.
        /// </summary>
        private IEnumerator ApproachAndOpenShopRoutine(PlayerMover mover, Transform playerTransform, float requiredDistance)
        {
            if (mover == null || playerTransform == null)
            {
                tradeApproachRoutine = null;
                approachingMover = null;
                yield break;
            }

            float stopDistance = ResolveApproachStopDistance(requiredDistance);
            mover.MoveTo(transform, stopDistance);

            float requiredDistanceSqr = requiredDistance * requiredDistance;
            while (mover != null && playerTransform != null)
            {
                Vector2 npcPosition = transform.position;
                Vector2 playerPosition = playerTransform.position;
                if (Vector2.SqrMagnitude(playerPosition - npcPosition) <= requiredDistanceSqr)
                    break;

                yield return null;
            }

            if (mover == null || playerTransform == null)
            {
                tradeApproachRoutine = null;
                approachingMover = null;
                yield break;
            }

            tradeApproachRoutine = null;
            approachingMover = null;

            OpenShopInternal();
        }

        /// <summary>
        ///     Stops any active trade approach and clears the mover reference when appropriate.
        /// </summary>
        private void CancelTradeApproach()
        {
            if (tradeApproachRoutine != null)
            {
                StopCoroutine(tradeApproachRoutine);
                tradeApproachRoutine = null;
            }

            if (approachingMover != null && approachingMover.IsAutoMoving)
                approachingMover.StopMovement();

            approachingMover = null;
        }

        /// <summary>
        ///     Opens the shop UI immediately without validating the player's distance.
        /// </summary>
        private void OpenShopInternal()
        {
            if (shop == null)
                return;

            var ui = ShopUI.Instance;
            if (ui == null)
                return;

            ui.Open(shop, GetComponent<NpcWanderer>());
        }
    }
}
