using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Player;
using UI;
using Core.Input;

namespace Skills.Common
{
    /// <summary>
    ///     Generic controller that provides shared gathering behaviour (fishing, mining, woodcutting, etc.).
    ///     Handles interaction input, range validation, action cancellation, and capacity checks while exposing
    ///     abstract hooks so skill specific controllers can inject their own requirements.
    /// </summary>
    /// <typeparam name="TSkill">Specific skill component that drives the gathering logic.</typeparam>
    /// <typeparam name="TNode">Type of the resource node interacted with.</typeparam>
    [DisallowMultipleComponent]
    public abstract class GatheringController<TSkill, TNode> : MonoBehaviour
        where TSkill : MonoBehaviour
        where TNode : Component
    {
        [Header("Interaction")]
        [SerializeField]
        [Tooltip("Fallback interaction range used when the node definition does not provide one.")]
        private float defaultInteractRange = 1.5f;

        [SerializeField]
        [Tooltip("Distance at which the current action is automatically cancelled.")]
        private float defaultCancelDistance = 3f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Delay applied after a successful start before another interaction can occur.")]
        private float interactionCooldownSeconds = 0.2f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Cooldown between prospect (right click) interactions.")]
        private float prospectCooldownSeconds = 3f;

        [Header("Input")]
        [SerializeField]
        [Tooltip("Player input component supplying the default action map. Auto-assigned from the hierarchy when empty.")]
        private PlayerInput playerInput;

        [SerializeField]
        [Tooltip("Optional override for the Interact action (left click / confirm button).")]
        private InputActionReference interactActionReference;

        [SerializeField]
        [Tooltip("Optional override for the Prospect action (right click).")]
        private InputActionReference prospectActionReference;

        [SerializeField]
        [Tooltip("Optional override for the Cancel action (escape / back).")]
        private InputActionReference cancelActionReference;

        [Header("References")]
        [SerializeField]
        [Tooltip("Skill component that performs the actual gathering work.")]
        private TSkill skill;

        [SerializeField]
        [Tooltip("Player mover used to detect when the character begins to walk away from the resource.")]
        private PlayerMover playerMover;

        [SerializeField]
        [Tooltip("Camera used for translating screen clicks into world positions.")]
        private Camera worldCamera;

        // Node detected through trigger range so the player can press the interaction action instead of clicking.
        private TNode nearbyNode;

        // Time trackers for throttling interaction and prospect usage.
        private float nextInteractionAllowedTime;
        private float nextProspectAllowedTime;

        // Cached input actions resolved from the PlayerInput/asset.
        private InputAction interactAction;
        private InputAction prospectAction;
        private InputAction cancelAction;
        private bool interactActionOwned;
        private bool prospectActionOwned;
        private bool cancelActionOwned;

        // Flags queued by input callbacks so pointer interactions are processed from Update.
        private bool pendingInteract;
        private int pendingPointerId;
        private bool pendingProspect;

        /// <summary>
        ///     Cached reference to the skill component so derived classes can access it safely.
        /// </summary>
        protected TSkill Skill => skill;

        /// <summary>
        ///     Cached reference to the movement controller used for cancellation checks.
        /// </summary>
        protected PlayerMover PlayerMover => playerMover;

        /// <summary>
        ///     Camera used for cursor raycasts. Falls back to <see cref="Camera.main"/> when left empty.
        /// </summary>
        protected Camera WorldCamera => worldCamera;

        /// <summary>
        ///     Currently tracked node obtained via trigger proximity checks.
        /// </summary>
        protected TNode NearbyNode => nearbyNode;

        /// <summary>
        ///     Default range the base class will use if a node definition does not override it.
        /// </summary>
        protected float DefaultInteractRange => defaultInteractRange;

        /// <summary>
        ///     Default cancellation distance used when a node does not specify a custom value.
        /// </summary>
        protected float DefaultCancelDistance => defaultCancelDistance;

        /// <summary>
        ///     Cached cooldown configured for this component.
        /// </summary>
        protected float InteractionCooldownSeconds => interactionCooldownSeconds;

        /// <summary>
        ///     Cooldown between prospect actions.
        /// </summary>
        protected float ProspectCooldownSeconds => prospectCooldownSeconds;

        /// <summary>
        ///     Anchor used when spawning floating feedback text.
        /// </summary>
        protected virtual Transform FeedbackAnchor => transform;

        /// <summary>
        ///     Whether pointer interactions should be blocked while the cursor hovers a UI element.
        /// </summary>
        protected virtual bool BlockMouseWhilePointerOverUI => true;

        /// <summary>
        ///     Whether invoking the Cancel action should terminate the active gathering action.
        /// </summary>
        protected virtual bool AllowEscapeCancel => true;

        /// <summary>
        ///     Enables Interact action presses (keyboard / gamepad confirm) while a node trigger is active.
        /// </summary>
        protected virtual bool AllowQuickActionKey => true;

        /// <summary>
        ///     Cancels the action whenever the player starts moving away from the node.
        /// </summary>
        protected virtual bool CancelOnPlayerMovement => true;

        /// <summary>
        ///     If <c>true</c> the controller also stops when the current node becomes depleted.
        /// </summary>
        protected virtual bool CancelWhenNodeDepleted => true;

        /// <summary>
        ///     Enables right click prospecting behaviour.
        /// </summary>
        protected virtual bool SupportsProspecting => false;

        /// <summary>
        ///     Unity Awake callback used to automatically wire optional references.
        /// </summary>
        protected virtual void Awake()
        {
            if (skill == null)
                skill = GetComponent<TSkill>();
            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();
            if (worldCamera == null)
                worldCamera = Camera.main;
            if (playerInput == null)
                playerInput = GetComponent<PlayerInput>();
            if (playerInput == null)
                playerInput = GetComponentInParent<PlayerInput>();
        }

        /// <summary>
        ///     Ensures serialized values remain positive when edited inside the inspector.
        /// </summary>
        protected virtual void OnValidate()
        {
            defaultInteractRange = Mathf.Max(0f, defaultInteractRange);
            defaultCancelDistance = Mathf.Max(0f, defaultCancelDistance);
            interactionCooldownSeconds = Mathf.Max(0f, interactionCooldownSeconds);
            prospectCooldownSeconds = Mathf.Max(0f, prospectCooldownSeconds);
        }

        /// <summary>
        ///     Subscribe to the configured input actions when the controller becomes active.
        /// </summary>
        protected virtual void OnEnable()
        {
            SubscribeToInput();
        }

        /// <summary>
        ///     Ensure input actions are released whenever the controller is disabled.
        /// </summary>
        protected virtual void OnDisable()
        {
            UnsubscribeFromInput();
        }

        /// <summary>
        ///     Central update loop that refreshes the cached camera and evaluates cancellation rules.
        /// </summary>
        protected virtual void Update()
        {
            // Keep the cached camera fresh in case the active camera changes at runtime.
            if (worldCamera == null)
                worldCamera = Camera.main;

            EvaluateActiveAction();

            if (pendingInteract)
            {
                pendingInteract = false;
                if (!(BlockMouseWhilePointerOverUI &&
                      EventSystem.current != null &&
                      EventSystem.current.IsPointerOverGameObject(pendingPointerId)))
                {
                    var node = FindNodeUnderCursor();
                    if (node != null && IsInteractionReady())
                        AttemptStart(node);
                }
            }

            if (pendingProspect)
            {
                pendingProspect = false;
                if (!(BlockMouseWhilePointerOverUI &&
                      EventSystem.current != null &&
                      EventSystem.current.IsPointerOverGameObject(pendingPointerId)))
                {
                    var node = FindNodeUnderCursor();
                    if (node != null)
                    {
                        Prospect(node);
                        nextProspectAllowedTime = Time.time + prospectCooldownSeconds;
                    }
                }
            }
        }

        /// <summary>
        ///     Hook to be invoked whenever the interact action fires.
        /// </summary>
        private void HandleInteractAction(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            // Pointer devices (mouse, pen, touch) behave like the legacy left click handling.
            if (context.control != null && context.control.device is Pointer pointer)
            {
                if (!IsInteractionReady())
                    return;

                pendingInteract = true;
                pendingPointerId = pointer.deviceId;
                return;
            }

            if (!AllowQuickActionKey)
                return;

            if (!IsInteractionReady())
                return;

            if (nearbyNode != null)
                AttemptStart(nearbyNode);
        }

        /// <summary>
        ///     Handles the optional prospect action triggered by right click bindings.
        /// </summary>
        private void HandleProspectAction(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            if (!SupportsProspecting)
                return;

            if (context.control == null || !(context.control.device is Pointer pointer))
                return;

            if (Time.time < nextProspectAllowedTime)
                return;

            pendingProspect = true;
            pendingPointerId = pointer.deviceId;
        }

        /// <summary>
        ///     Cancels the current action when the cancel input is pressed.
        /// </summary>
        private void HandleCancelAction(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            if (!AllowEscapeCancel)
                return;

            RequestStopAction();
        }

        /// <summary>
        ///     Performs distance and depletion checks while an action is active and stops it when needed.
        /// </summary>
        private void EvaluateActiveAction()
        {
            if (!IsPerformingAction)
                return;

            if (CancelOnPlayerMovement && playerMover != null && playerMover.IsMoving)
            {
                RequestStopAction();
                return;
            }

            var node = CurrentNode;
            if (node == null)
            {
                RequestStopAction();
                return;
            }

            float cancelDistance = GetCancelDistance(node);
            if (cancelDistance > 0f)
            {
                float distance = Vector3.Distance(transform.position, GetNodePosition(node));
                if (distance > cancelDistance)
                {
                    RequestStopAction();
                    return;
                }
            }

            if (CancelWhenNodeDepleted && IsNodeDepleted(node))
                RequestStopAction();
        }

        /// <summary>
        ///     Converts the current pointer position into a node reference using the derived implementation.
        /// </summary>
        private TNode FindNodeUnderCursor()
        {
            if (worldCamera == null)
                return null;

            Vector2 fallback = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 screenPoint = InputActionResolver.GetPointerScreenPosition(fallback);
            Vector2 worldPoint = worldCamera.ScreenToWorldPoint(screenPoint);
            return FindNodeAtWorldPosition(worldPoint);
        }

        /// <summary>
        ///     Executes shared validation (range, depletion, inventory) before attempting to start the action.
        /// </summary>
        private void AttemptStart(TNode node)
        {
            if (node == null)
                return;

            if (!CanInteractWith(node, out string failure))
            {
                ShowFeedback(failure);
                return;
            }

            if (TryStartAction(node, out failure))
            {
                if (interactionCooldownSeconds > 0f)
                    nextInteractionAllowedTime = Time.time + interactionCooldownSeconds;
                OnActionStarted(node);
            }
            else
            {
                ShowFeedback(failure);
            }
        }

        /// <summary>
        ///     Validates common interaction rules before delegating to the concrete skill controller.
        /// </summary>
        private bool CanInteractWith(TNode node, out string failure)
        {
            failure = string.Empty;

            if (IsNodeDepleted(node))
            {
                failure = GetDepletedMessage(node);
                return false;
            }

            if (IsNodeBusy(node))
            {
                failure = GetBusyMessage(node);
                return false;
            }

            float distance = Vector3.Distance(transform.position, GetNodePosition(node));
            if (distance > GetInteractionRange(node))
                return false;

            if (!ValidateNode(node, out failure))
                return false;

            if (!HasInventorySpace(node, out failure))
                return false;

            return true;
        }

        /// <summary>
        ///     Stops the current action, ensuring derived classes receive an <see cref="OnActionStopped"/> callback.
        /// </summary>
        private void RequestStopAction()
        {
            StopAction();
            OnActionStopped();
        }

        /// <summary>
        ///     Displays floating feedback text when a validation fails.
        /// </summary>
        private void ShowFeedback(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Transform anchor = FeedbackAnchor != null ? FeedbackAnchor : transform;
            FloatingText.Show(message, anchor.position);
        }

        /// <summary>
        ///     Helper used to check whether the interaction cooldown has elapsed.
        /// </summary>
        private bool IsInteractionReady()
        {
            return Time.time >= nextInteractionAllowedTime;
        }

        /// <summary>
        ///     Resolves the resource node component from a trigger collider.
        /// </summary>
        protected virtual TNode ResolveNodeFromCollider(Collider2D collider)
        {
            if (collider == null)
                return null;
            return collider.GetComponentInParent<TNode>();
        }

        /// <summary>
        ///     Stores the closest node when entering its trigger.
        /// </summary>
        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            var node = ResolveNodeFromCollider(other);
            if (node != null)
                nearbyNode = node;
        }

        /// <summary>
        ///     Clears the cached node when leaving its trigger.
        /// </summary>
        protected virtual void OnTriggerExit2D(Collider2D other)
        {
            var node = ResolveNodeFromCollider(other);
            if (node != null && node == nearbyNode)
                nearbyNode = null;
        }

        /// <summary>
        ///     Called after a successful <see cref="TryStartAction"/> allowing derived classes to react (animations, UI, etc.).
        /// </summary>
        /// <param name="node">Node that the action was started on.</param>
        protected virtual void OnActionStarted(TNode node) { }

        /// <summary>
        ///     Called every time the controller stops the current action.
        /// </summary>
        protected virtual void OnActionStopped() { }

        /// <summary>
        ///     Derived classes must supply the logic that actually starts the action.
        ///     Returning <c>false</c> keeps the cooldown untouched and forwards any message back to the player.
        /// </summary>
        protected abstract bool TryStartAction(TNode node, out string failureMessage);

        /// <summary>
        ///     Hook for validating skill specific requirements such as tool tiers or level gating.
        /// </summary>
        protected virtual bool ValidateNode(TNode node, out string failureMessage)
        {
            failureMessage = string.Empty;
            return true;
        }

        /// <summary>
        ///     Ensures the player's storage can receive at least one output from the node.
        /// </summary>
        protected abstract bool HasInventorySpace(TNode node, out string failureMessage);

        /// <summary>
        ///     Stops the active gathering action.
        /// </summary>
        protected abstract void StopAction();

        /// <summary>
        ///     Indicates whether the underlying skill is currently gathering.
        /// </summary>
        protected abstract bool IsPerformingAction { get; }

        /// <summary>
        ///     Provides the node currently being interacted with.
        /// </summary>
        protected abstract TNode CurrentNode { get; }

        /// <summary>
        ///     Allows derived classes to offer a right click prospect action when supported by the skill.
        /// </summary>
        protected virtual void Prospect(TNode node) { }

        /// <summary>
        ///     Supplies the node located under the provided world position.
        /// </summary>
        protected abstract TNode FindNodeAtWorldPosition(Vector2 worldPosition);

        /// <summary>
        ///     Returns <c>true</c> when the provided node is currently depleted.
        /// </summary>
        protected virtual bool IsNodeDepleted(TNode node) => false;

        /// <summary>
        ///     Returns <c>true</c> when the node is already in use by another player.
        /// </summary>
        protected virtual bool IsNodeBusy(TNode node) => false;

        /// <summary>
        ///     Override this when the node position used for distance checks differs from <see cref="Component.transform"/>.
        /// </summary>
        protected virtual Vector3 GetNodePosition(TNode node) => node.transform.position;

        /// <summary>
        ///     Allows derived classes to provide custom interaction ranges per node.
        /// </summary>
        protected virtual float GetInteractionRange(TNode node) => defaultInteractRange;

        /// <summary>
        ///     Allows derived classes to provide custom cancel distances per node.
        /// </summary>
        protected virtual float GetCancelDistance(TNode node) => defaultCancelDistance;

        /// <summary>
        ///     Message displayed when attempting to interact with a depleted node. Empty means no feedback.
        /// </summary>
        protected virtual string GetDepletedMessage(TNode node) => string.Empty;

        /// <summary>
        ///     Message displayed when attempting to interact with an already occupied node.
        /// </summary>
        protected virtual string GetBusyMessage(TNode node) => string.Empty;

        /// <summary>
        ///     Resolve and subscribe to the configured input actions.
        /// </summary>
        private void SubscribeToInput()
        {
            UnsubscribeFromInput();

            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
                if (playerInput == null)
                    playerInput = GetComponentInParent<PlayerInput>();
            }

            interactAction = InputActionResolver.Resolve(playerInput, interactActionReference, "Interact",
                out interactActionOwned);
            if (interactAction != null)
                interactAction.performed += HandleInteractAction;

            if (SupportsProspecting)
            {
                prospectAction = InputActionResolver.Resolve(playerInput, prospectActionReference, "Prospect",
                    out prospectActionOwned);
                if (prospectAction != null)
                    prospectAction.performed += HandleProspectAction;
            }

            if (AllowEscapeCancel)
            {
                cancelAction = InputActionResolver.Resolve(playerInput, cancelActionReference, "Cancel",
                    out cancelActionOwned);
                if (cancelAction != null)
                    cancelAction.performed += HandleCancelAction;
            }
        }

        /// <summary>
        ///     Unsubscribe from cached input actions and disable them if the resolver enabled them.
        /// </summary>
        private void UnsubscribeFromInput()
        {
            if (interactAction != null)
            {
                interactAction.performed -= HandleInteractAction;
                if (interactActionOwned)
                    interactAction.Disable();
                interactAction = null;
                interactActionOwned = false;
            }

            if (prospectAction != null)
            {
                prospectAction.performed -= HandleProspectAction;
                if (prospectActionOwned)
                    prospectAction.Disable();
                prospectAction = null;
                prospectActionOwned = false;
            }

            if (cancelAction != null)
            {
                cancelAction.performed -= HandleCancelAction;
                if (cancelActionOwned)
                    cancelAction.Disable();
                cancelAction = null;
                cancelActionOwned = false;
            }
        }
    }
}
