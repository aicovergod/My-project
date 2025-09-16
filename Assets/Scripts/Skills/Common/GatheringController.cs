using UnityEngine;
using UnityEngine.EventSystems;
using Player;
using UI;

namespace Skills.Common
{
    /// <summary>
    ///     Generic controller that provides shared gathering behaviour (fishing, mining, woodcutting, etc.).
    ///     Handles click input, range validation, action cancellation, and capacity checks while exposing
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

        [SerializeField]
        [Tooltip("Key used to trigger the closest node while standing inside its trigger volume.")]
        private KeyCode quickActionKey = KeyCode.E;

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

        // Node detected through trigger range so the player can press the interaction key instead of clicking.
        private TNode nearbyNode;

        // Time trackers for throttling mouse input and prospect interactions.
        private float nextInteractionAllowedTime;
        private float nextProspectAllowedTime;

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
        ///     Proximity key used when inside a node trigger.
        /// </summary>
        protected KeyCode QuickInteractionKey => quickActionKey;

        /// <summary>
        ///     Anchor used when spawning floating feedback text.
        /// </summary>
        protected virtual Transform FeedbackAnchor => transform;

        /// <summary>
        ///     Whether mouse clicks should be blocked while the pointer is over UI.
        /// </summary>
        protected virtual bool BlockMouseWhilePointerOverUI => true;

        /// <summary>
        ///     Whether pressing escape should terminate the active gathering action.
        /// </summary>
        protected virtual bool AllowEscapeCancel => true;

        /// <summary>
        ///     Enables pressing <see cref="QuickInteractionKey"/> while a node trigger is active.
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
        ///     Central update loop handling click input, proximity interactions and cancellation rules.
        /// </summary>
        protected virtual void Update()
        {
            // Keep the cached camera fresh in case the active camera changes at runtime.
            if (worldCamera == null)
                worldCamera = Camera.main;

            HandleEscapeCancel();
            HandlePrimaryClick();
            HandleProspectClick();
            HandleQuickActionKey();
            EvaluateActiveAction();
        }

        /// <summary>
        ///     Reacts to the escape key cancelling the current action when allowed.
        /// </summary>
        private void HandleEscapeCancel()
        {
            if (!AllowEscapeCancel)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
                RequestStopAction();
        }

        /// <summary>
        ///     Processes left click input and attempts to start interacting with a node.
        /// </summary>
        private void HandlePrimaryClick()
        {
            if (!Input.GetMouseButtonDown(0))
                return;

            if (!IsInteractionReady())
                return;

            if (IsPointerOverUI())
                return;

            var node = FindNodeUnderCursor();
            if (node != null)
                AttemptStart(node);
        }

        /// <summary>
        ///     Handles the optional prospect (right click) action shared by mining/fishing.
        /// </summary>
        private void HandleProspectClick()
        {
            if (!SupportsProspecting)
                return;

            if (!Input.GetMouseButtonDown(1))
                return;

            if (Time.time < nextProspectAllowedTime)
                return;

            if (IsPointerOverUI())
                return;

            var node = FindNodeUnderCursor();
            if (node == null)
                return;

            Prospect(node);
            nextProspectAllowedTime = Time.time + prospectCooldownSeconds;
        }

        /// <summary>
        ///     Allows keyboard interaction with the closest node when standing inside its trigger.
        /// </summary>
        private void HandleQuickActionKey()
        {
            if (!AllowQuickActionKey)
                return;

            if (!Input.GetKeyDown(quickActionKey))
                return;

            if (!IsInteractionReady())
                return;

            if (nearbyNode != null)
                AttemptStart(nearbyNode);
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
        ///     Converts the current mouse position into a node reference using the derived implementation.
        /// </summary>
        private TNode FindNodeUnderCursor()
        {
            if (worldCamera == null)
                return null;

            Vector2 worldPoint = worldCamera.ScreenToWorldPoint(Input.mousePosition);
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
        ///     Returns <c>true</c> when the pointer is currently hovering a UI element that should block input.
        /// </summary>
        private bool IsPointerOverUI()
        {
            if (!BlockMouseWhilePointerOverUI)
                return false;

            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
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
    }
}
