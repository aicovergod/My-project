using System.Collections;
using Inventory;
using Player;
using Core.Input;
using UI;
using UI.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Util;

namespace NPC
{
    /// <summary>
    ///     Allows the player to use an empty bucket on a cow and receive a filled bucket after a four tick delay.
    ///     The component is attached to a cow NPC and coordinates range checks, tick timing, and feedback messages.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class CowMilking : MonoBehaviour, IPointerClickHandler, ITickable
    {
        [Header("Input")]
        [SerializeField]
        [Tooltip("Player input component providing the interaction action map. Auto-resolved when empty.")]
        private PlayerInput playerInput;

        [SerializeField]
        [Tooltip("Optional override for the Interact action used to trigger milking via the shared input system.")]
        private InputActionReference interactActionReference;

        [Header("Item Configuration")]
        [SerializeField]
        [Tooltip("Inventory identifier for the empty bucket item that must be used on the cow.")]
        private string emptyBucketItemId = "bucket";

        [SerializeField]
        [Tooltip("Inventory identifier for the filled bucket rewarded after a successful milking action.")]
        private string filledBucketItemId = "bucket_of_milk";

        [Header("Timing")]
        [SerializeField, Min(1)]
        [Tooltip("Number of OSRS ticks (0.6 seconds each) required to fill a single bucket.")]
        private int ticksPerMilking = 4;

        [Header("Range")]
        [SerializeField, Min(0f)]
        [Tooltip("Maximum world-space distance the player can stand from the cow to start milking.")]
        private float maxInteractionDistance = 1.5f;

        [SerializeField, Min(0f)]
        [Tooltip("Extra distance tolerated before cancelling so minor jitter does not stop the action immediately.")]
        private float cancelDistanceTolerance = 0.15f;

        [Header("Auto Movement")]
        [SerializeField]
        [Tooltip("When enabled the player will automatically walk into range before milking.")]
        private bool autoMoveIntoRange = true;

        [SerializeField, Min(0f)]
        [Tooltip("Buffer subtracted from the interaction range when auto moving so the player stops inside the radius.")]
        private float autoMoveStopBuffer = 0.15f;

        [Header("Feedback")]
        [SerializeField]
        [Tooltip("Optional popup shown when milking begins.")]
        private string startMessage = "You begin to milk the cow.";

        [SerializeField]
        [Tooltip("Optional popup shown each time a bucket is filled.")]
        private string successMessage = "You fill a bucket with milk.";

        [SerializeField]
        [Tooltip("Optional popup shown when the action is interrupted by movement or other failures.")]
        private string cancelledMessage = "You stop milking the cow.";

        [SerializeField]
        [Tooltip("Popup shown when the player is too far away to milk the cow.")]
        private string outOfRangeMessage = "You need to stand closer to milk the cow.";

        [SerializeField]
        [Tooltip("Popup shown when the player attempts to milk without selecting an empty bucket.")]
        private string missingBucketMessage = "You need an empty bucket to milk the cow.";

        [SerializeField]
        [Tooltip("Popup shown when the inventory cannot accept the milk bucket.")]
        private string inventoryFullMessage = "You need some free space to hold the milk.";

        [SerializeField]
        [Tooltip("World-space offset applied to floating text so messages appear above the cow.")]
        private Vector3 floatingTextOffset = new Vector3(0f, 1f, 0f);

        [SerializeField]
        [Tooltip("When enabled the player will turn to face the cow as soon as milking begins.")]
        private bool facePlayerOnStart = true;

        [SerializeField]
        [Tooltip("Controls whether floating text feedback is emitted for the interaction.")]
        private bool showFeedback = true;

        [SerializeField]
        [Tooltip("Logs diagnostic output to the console when milking starts, completes, or is cancelled.")]
        private bool enableDebugLogging;

        private Inventory.Inventory playerInventory;
        private PlayerMover playerMover;

        private ItemData emptyBucketItem;
        private ItemData filledBucketItem;

        private bool itemCacheDirty = true;
        private bool reportedMissingPlayer;
        private bool reportedMissingInventory;
        private bool reportedMissingMover;
        private bool reportedMissingEmptyBucket;
        private bool reportedMissingFilledBucket;
        private bool reportedMissingPlayerInput;
        private bool reportedMissingCamera;

        private bool milkingActive;
        private bool subscribedToTicker;
        private int ticksRemaining;
        private int preferredBucketSlot = -1;

        private Coroutine autoMoveRoutine;
        private bool autoMovePending;

        private InputAction interactAction;
        private bool interactActionEnabledByResolver;

        /// <summary>
        ///     Handle left-clicks on the cow and begin the milking routine when the player has a bucket selected.
        /// </summary>
        /// <param name="eventData">Pointer payload supplied by Unity's event system.</param>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!isActiveAndEnabled)
                return;

            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            if (TryBeginMilkingFromPointer(eventData.position, requireRaycastConfirmation: false))
                eventData.Use();
        }

        /// <summary>
        ///     Advance the milking routine on each global tick, filling buckets at the configured cadence.
        /// </summary>
        public void OnTick()
        {
            if (!milkingActive)
                return;

            if (!TryResolvePlayerComponents())
            {
                StopMilking(true, cancelledMessage);
                return;
            }

            if (!ValidateItemDefinitions())
            {
                StopMilking(true, cancelledMessage);
                return;
            }

            if (!HasAnyBuckets())
            {
                StopMilking(false);
                return;
            }

            if (playerMover.IsMoving || !IsPlayerWithinMaintainRange())
            {
                StopMilking(true, cancelledMessage);
                return;
            }

            if (ticksRemaining > 0)
            {
                ticksRemaining--;
                if (ticksRemaining > 0)
                    return;
            }

            if (!TryMilkSingleBucket(out string failureMessage))
            {
                StopMilking(true, string.IsNullOrEmpty(failureMessage) ? cancelledMessage : failureMessage);
                return;
            }

            ShowFloatingText(successMessage);

            if (HasAnyBuckets())
            {
                ticksRemaining = ticksPerMilking;
            }
            else
            {
                StopMilking(false);
            }
        }

        private void Awake()
        {
            // Ensure the cached items refresh whenever the component is instantiated.
            itemCacheDirty = true;
        }

        private void OnEnable()
        {
            itemCacheDirty = true;
            SubscribeToInteractAction();
        }

        private void OnDisable()
        {
            UnsubscribeFromInteractAction();
            StopAutoMoveRoutine(true);
            StopMilking(false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ticksPerMilking = Mathf.Max(1, ticksPerMilking);
            maxInteractionDistance = Mathf.Max(0f, maxInteractionDistance);
            cancelDistanceTolerance = Mathf.Max(0f, cancelDistanceTolerance);
            autoMoveStopBuffer = Mathf.Max(0f, autoMoveStopBuffer);
            itemCacheDirty = true;
        }
#endif

        /// <summary>
        ///     Begins the milking loop by clearing the bucket selection, facing the cow, and subscribing to the ticker.
        /// </summary>
        private void BeginMilking()
        {
            StopAutoMoveRoutine(false);

            if (milkingActive)
            {
                LogDebug("Milking command ignored because an action is already in progress.");
                return;
            }

            if (!HasAnyBuckets())
            {
                ShowFloatingText(missingBucketMessage);
                return;
            }

            if (!SubscribeToTicker())
                return;

            milkingActive = true;
            ticksRemaining = ticksPerMilking;

            // Consuming the item selection mirrors OSRS behaviour after an item-on-entity interaction.
            if (playerInventory != null)
            {
                playerInventory.selectedIndex = -1;
                playerInventory.WindowController?.ClearHighlight();
            }

            if (playerMover != null)
            {
                playerMover.StopMovement();
                if (facePlayerOnStart)
                    playerMover.FaceTarget(transform);
            }

            ShowFloatingText(startMessage);
            LogDebug("Milking started.");
        }

        /// <summary>
        ///     Stops the milking routine, unsubscribes from the ticker, and optionally displays feedback.
        /// </summary>
        /// <param name="showMessage">True to emit floating text.</param>
        /// <param name="message">Optional override for the displayed text.</param>
        private void StopMilking(bool showMessage, string message = null)
        {
            milkingActive = false;
            ticksRemaining = 0;
            preferredBucketSlot = -1;
            StopAutoMoveRoutine(false);
            UnsubscribeFromTicker();

            string reason = message;
            if (showMessage)
            {
                string display = message ?? cancelledMessage;
                ShowFloatingText(display);
                reason ??= display;
            }

            reason ??= "complete";
            LogDebug("Milking stopped: " + reason);
        }

        /// <summary>
        ///     Attempts to replace a single empty bucket with a filled bucket. Returns false when blocked by capacity or desync.
        /// </summary>
        /// <param name="failureMessage">Outputs a user-facing message that explains why the attempt failed.</param>
        private bool TryMilkSingleBucket(out string failureMessage)
        {
            failureMessage = null;

            if (!TryFindBucketSlot(out int slotIndex, out InventoryEntry entry))
                return false;

            bool slotHasStack = entry.item != null && entry.item.stackable && entry.count > 1;

            if (slotHasStack)
            {
                if (!playerInventory.CanAddItem(filledBucketItem, 1))
                {
                    failureMessage = inventoryFullMessage;
                    return false;
                }

                playerInventory.RemoveFromSlot(slotIndex, 1);
                if (!playerInventory.AddItem(filledBucketItem, 1))
                {
                    playerInventory.AddItem(emptyBucketItem, 1);
                    failureMessage = inventoryFullMessage;
                    return false;
                }

                preferredBucketSlot = -1;
                LogDebug("Filled a bucket from a stack.");
                return true;
            }

            if (playerInventory.ReplaceItem(slotIndex, entry.item, filledBucketItem, 1))
            {
                preferredBucketSlot = -1;
                LogDebug("Replaced bucket in slot " + slotIndex + ".");
                return true;
            }

            // Fallback path if another system modified the slot before replacement occurred.
            playerInventory.RemoveFromSlot(slotIndex, 1);
            if (!playerInventory.AddItem(filledBucketItem, 1))
            {
                playerInventory.AddItem(emptyBucketItem, 1);
                failureMessage = inventoryFullMessage;
                return false;
            }

            preferredBucketSlot = -1;
            LogDebug("Filled a bucket via removal fallback.");
            return true;
        }

        /// <summary>
        ///     Resolves the player's inventory and movement components so milking can access them directly.
        /// </summary>
        private bool TryResolvePlayerComponents()
        {
            if (playerInventory != null && playerMover != null)
                return true;

            if (!PlayerLocator.TryFindPlayer(out var playerObject) || playerObject == null)
            {
                if (!reportedMissingPlayer)
                {
                    Debug.LogWarning("CowMilking could not locate the player in the scene.", this);
                    reportedMissingPlayer = true;
                }
                return false;
            }

            reportedMissingPlayer = false;

            if (!playerObject.TryGetComponent(out playerInventory) || playerInventory == null)
            {
                if (!reportedMissingInventory)
                {
                    Debug.LogWarning("CowMilking requires the player to have an Inventory component.", this);
                    reportedMissingInventory = true;
                }
                playerMover = null;
                return false;
            }

            reportedMissingInventory = false;

            if (!playerObject.TryGetComponent(out playerMover) || playerMover == null)
            {
                if (!reportedMissingMover)
                {
                    Debug.LogWarning("CowMilking requires the player to have a PlayerMover component.", this);
                    reportedMissingMover = true;
                }
                return false;
            }

            reportedMissingMover = false;
            return true;
        }

        /// <summary>
        ///     Ensures the required ItemData assets are cached before performing inventory operations.
        /// </summary>
        private bool ValidateItemDefinitions()
        {
            EnsureItemsCached();

            if (emptyBucketItem == null)
            {
                if (!reportedMissingEmptyBucket && !string.IsNullOrEmpty(emptyBucketItemId))
                {
                    Debug.LogError($"CowMilking could not resolve empty bucket item '{emptyBucketItemId}'.", this);
                    reportedMissingEmptyBucket = true;
                }
                return false;
            }

            reportedMissingEmptyBucket = false;

            if (filledBucketItem == null)
            {
                if (!reportedMissingFilledBucket && !string.IsNullOrEmpty(filledBucketItemId))
                {
                    Debug.LogError($"CowMilking could not resolve milk bucket item '{filledBucketItemId}'.", this);
                    reportedMissingFilledBucket = true;
                }
                return false;
            }

            reportedMissingFilledBucket = false;
            return true;
        }

        /// <summary>
        ///     Loads the ItemData assets for the configured identifiers when needed.
        /// </summary>
        private void EnsureItemsCached()
        {
            if (!itemCacheDirty)
                return;

            emptyBucketItem = ResolveItem(emptyBucketItemId);
            filledBucketItem = ResolveItem(filledBucketItemId);
            itemCacheDirty = false;
        }

        /// <summary>
        ///     Helper that performs the actual ItemDatabase lookup for an identifier.
        /// </summary>
        private static ItemData ResolveItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;
            return ItemDatabase.GetItem(itemId);
        }

        /// <summary>
        ///     Determines whether the player has selected an empty bucket in their inventory.
        /// </summary>
        private bool TryValidateSelectedBucket(out int slotIndex)
        {
            slotIndex = -1;

            if (playerInventory == null)
                return false;

            int selectedIndex = playerInventory.selectedIndex;
            if (selectedIndex < 0 || selectedIndex >= playerInventory.size)
                return false;

            var entry = playerInventory.GetSlot(selectedIndex);
            if (entry.item != emptyBucketItem || entry.count <= 0)
                return false;

            slotIndex = selectedIndex;
            return true;
        }

        /// <summary>
        ///     Returns true while the player has at least one empty bucket in the inventory.
        /// </summary>
        private bool HasAnyBuckets()
        {
            return playerInventory != null && emptyBucketItem != null && playerInventory.GetItemCount(emptyBucketItem) > 0;
        }

        /// <summary>
        ///     Searches the inventory for the next empty bucket slot, prioritising the initially selected slot.
        /// </summary>
        private bool TryFindBucketSlot(out int slotIndex, out InventoryEntry entry)
        {
            entry = default;
            slotIndex = -1;

            if (playerInventory == null || emptyBucketItem == null)
                return false;

            if (preferredBucketSlot >= 0)
            {
                var preferredEntry = playerInventory.GetSlot(preferredBucketSlot);
                if (preferredEntry.item == emptyBucketItem && preferredEntry.count > 0)
                {
                    slotIndex = preferredBucketSlot;
                    entry = preferredEntry;
                    preferredBucketSlot = -1;
                    return true;
                }

                preferredBucketSlot = -1;
            }

            int slotCount = Mathf.Max(0, playerInventory.size);
            for (int i = 0; i < slotCount; i++)
            {
                var candidate = playerInventory.GetSlot(i);
                if (candidate.item == emptyBucketItem && candidate.count > 0)
                {
                    slotIndex = i;
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Checks whether the player is close enough to begin milking.
        /// </summary>
        private bool IsPlayerWithinStartRange()
        {
            if (playerMover == null)
                return false;

            if (maxInteractionDistance <= 0f)
                return true;

            Vector3 delta = playerMover.transform.position - transform.position;
            delta.z = 0f;
            return delta.magnitude <= maxInteractionDistance;
        }

        /// <summary>
        ///     Checks whether the player remains close enough to continue milking without interruption.
        /// </summary>
        private bool IsPlayerWithinMaintainRange()
        {
            if (playerMover == null)
                return false;

            float allowed = maxInteractionDistance + cancelDistanceTolerance;
            if (allowed <= 0f)
                return true;

            Vector3 delta = playerMover.transform.position - transform.position;
            delta.z = 0f;
            return delta.magnitude <= allowed;
        }

        /// <summary>
        ///     Registers this component with the global ticker so it receives tick callbacks.
        /// </summary>
        private bool SubscribeToTicker()
        {
            if (subscribedToTicker)
                return true;

            if (Ticker.Instance == null)
            {
                Debug.LogError("CowMilking requires a Ticker in the scene to operate.", this);
                return false;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
            return true;
        }

        /// <summary>
        ///     Unregisters the component from the ticker.
        /// </summary>
        private void UnsubscribeFromTicker()
        {
            if (!subscribedToTicker)
                return;

            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);

            subscribedToTicker = false;
        }

        /// <summary>
        ///     Displays anchored floating text when feedback is enabled.
        /// </summary>
        private void ShowFloatingText(string message)
        {
            if (!showFeedback || string.IsNullOrEmpty(message))
                return;

            FloatingText.ShowAnchored(message, transform, floatingTextOffset);
        }

        /// <summary>
        ///     Emits optional debug output controlled by the inspector toggle.
        /// </summary>
        private void LogDebug(string message)
        {
            if (!enableDebugLogging || string.IsNullOrEmpty(message))
                return;

            Debug.Log($"[CowMilking] {message}", this);
        }

        /// <summary>
        ///     Attempts to start milking using shared pointer validation so both clicks and the Interact action behave the same.
        /// </summary>
        /// <param name="screenPosition">Screen-space coordinates of the triggering pointer.</param>
        /// <param name="requireRaycastConfirmation">
        ///     True to require that a Physics2D raycast confirms the pointer is targeting this cow.
        /// </param>
        private bool TryBeginMilkingFromPointer(Vector2 screenPosition, bool requireRaycastConfirmation)
        {
            if (PointerRaycastUtility.IsPointerOverBlockingUI(screenPosition))
                return false;

            if (requireRaycastConfirmation && !RaycastConfirmsThisCow(screenPosition))
                return false;

            return TryBeginMilkingInteraction();
        }

        /// <summary>
        ///     Shared milking start logic used by both pointer events and input actions.
        /// </summary>
        private bool TryBeginMilkingInteraction()
        {
            if (!TryResolvePlayerComponents())
                return false;

            if (!ValidateItemDefinitions())
                return false;

            if (!TryValidateSelectedBucket(out int selectedSlot))
            {
                // Fallback to locating any empty bucket when the player has one but has not explicitly selected it.
                if (!TryFindBucketSlot(out selectedSlot, out _))
                {
                    ShowFloatingText(missingBucketMessage);
                    return false;
                }
            }

            preferredBucketSlot = selectedSlot;

            if (!IsPlayerWithinStartRange())
            {
                if (TryStartAutoMove())
                    return true;

                preferredBucketSlot = -1;
                ShowFloatingText(outOfRangeMessage);
                return false;
            }

            BeginMilking();
            return true;
        }

        /// <summary>
        ///     Attempts to walk the player into range so milking can begin automatically upon arrival.
        /// </summary>
        private bool TryStartAutoMove()
        {
            if (!autoMoveIntoRange)
                return false;

            if (!TryResolvePlayerComponents())
                return false;

            if (playerMover == null)
                return false;

            StartAutoMoveRoutine();
            return true;
        }

        /// <summary>
        ///     Resolves the Interact input action so controller and keyboard input can trigger milking.
        /// </summary>
        private void SubscribeToInteractAction()
        {
            UnsubscribeFromInteractAction();

            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
                if (playerInput == null)
                    playerInput = GetComponentInParent<PlayerInput>();
                if (playerInput == null)
                    playerInput = FindObjectOfType<PlayerInput>();
            }

            interactAction = InputActionResolver.Resolve(playerInput, interactActionReference, "Interact", out interactActionEnabledByResolver);
            if (interactAction != null)
            {
                interactAction.performed += HandleInteractAction;
                reportedMissingPlayerInput = false;
            }
            else if (!reportedMissingPlayerInput)
            {
                Debug.LogWarning("CowMilking could not locate the Interact action. Assign a PlayerInput or InputActionReference.", this);
                reportedMissingPlayerInput = true;
            }
        }

        /// <summary>
        ///     Tears down the interact action subscription and disables any actions owned by the resolver.
        /// </summary>
        private void UnsubscribeFromInteractAction()
        {
            if (interactAction != null)
            {
                interactAction.performed -= HandleInteractAction;
                if (interactActionEnabledByResolver)
                    interactAction.Disable();

                interactAction = null;
                interactActionEnabledByResolver = false;
            }
        }

        /// <summary>
        ///     Handles interact action callbacks from the shared player input system.
        /// </summary>
        private void HandleInteractAction(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            if (!isActiveAndEnabled)
                return;

            Vector2 pointerPosition = InputActionResolver.GetPointerScreenPosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

            TryBeginMilkingFromPointer(pointerPosition, requireRaycastConfirmation: true);
        }

        /// <summary>
        ///     Confirms whether the provided pointer position targets this cow using a Physics2D raycast.
        /// </summary>
        private bool RaycastConfirmsThisCow(Vector2 screenPosition)
        {
            var activeCamera = Camera.main;
            if (activeCamera == null)
            {
                if (!reportedMissingCamera)
                {
                    Debug.LogWarning("CowMilking could not locate the active camera to validate interact input.", this);
                    reportedMissingCamera = true;
                }

                return false;
            }

            reportedMissingCamera = false;

            var ray = activeCamera.ScreenPointToRay(screenPosition);
            float maxDistance = Mathf.Max(activeCamera.farClipPlane, 0f);
            if (maxDistance <= 0f)
                maxDistance = float.PositiveInfinity;

            var hit = Physics2D.GetRayIntersection(ray, maxDistance);
            if (hit.collider == null)
                return false;

            return hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform);
        }

        /// <summary>
        ///     Begins monitoring the player's automatic movement towards the cow.
        /// </summary>
        private void StartAutoMoveRoutine()
        {
            StopAutoMoveRoutine(false);

            autoMoveRoutine = StartCoroutine(AutoMoveIntoRangeCoroutine());
        }

        /// <summary>
        ///     Stops any pending automatic movement routine, optionally halting the player immediately.
        /// </summary>
        private void StopAutoMoveRoutine(bool stopMovement)
        {
            if (autoMoveRoutine != null)
            {
                StopCoroutine(autoMoveRoutine);
                autoMoveRoutine = null;
            }

            autoMovePending = false;

            if (stopMovement && playerMover != null)
                playerMover.StopMovement();
        }

        /// <summary>
        ///     Calculates the stop distance used when auto moving into range so the player does not overshoot the cow.
        /// </summary>
        private float CalculateAutoMoveStopDistance()
        {
            if (maxInteractionDistance <= 0f)
                return 0f;

            float stopDistance = maxInteractionDistance - autoMoveStopBuffer;
            return Mathf.Clamp(stopDistance, 0f, maxInteractionDistance);
        }

        /// <summary>
        ///     Coroutine that walks the player into range and starts milking once close enough.
        /// </summary>
        private IEnumerator AutoMoveIntoRangeCoroutine()
        {
            if (playerMover == null)
            {
                yield break;
            }

            autoMovePending = true;

            int reissueAttempts = 0;
            float stopDistance = CalculateAutoMoveStopDistance();
            bool moveCompleted = false;

            void HandleMoveComplete()
            {
                moveCompleted = true;
            }

            playerMover.MoveTo(transform, stopDistance, HandleMoveComplete);

            while (autoMovePending)
            {
                if (!isActiveAndEnabled)
                    break;

                if (!TryResolvePlayerComponents() || playerMover == null)
                    break;

                if (!ValidateItemDefinitions())
                    break;

                if (!HasAnyBuckets())
                {
                    ShowFloatingText(missingBucketMessage);
                    break;
                }

                if (IsPlayerWithinStartRange())
                {
                    BeginMilking();
                    yield break;
                }

                if (!playerMover.IsAutoMoving)
                {
                    if (moveCompleted)
                    {
                        moveCompleted = false;

                        if (++reissueAttempts >= 3)
                        {
                            ShowFloatingText(outOfRangeMessage);
                            break;
                        }

                        playerMover.MoveTo(transform, stopDistance, HandleMoveComplete);
                    }
                    else
                    {
                        // Movement was cancelled or interrupted before the callback fired.
                        break;
                    }
                }

                yield return null;
            }

            autoMovePending = false;
            autoMoveRoutine = null;
        }
    }
}
