/// Feature: Extracted pickup orchestration into a dedicated companion component.
using System;
using System.Collections;
using System.Reflection;
using Inventory;
using Inventory.GroundItems;
using MyGame.Drops;
using Pets;
using UI.Chat;
using UnityEngine;
using Util;

namespace Companions
{
    /// <summary>
    /// Handles directed ground item pickups for the companion, coordinating navigation,
    /// follower locking, and chat feedback while routing collected loot into the backpack.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionPickupController : MonoBehaviour
    {
        [Header("Pickup Commands")]
        [SerializeField]
        [Tooltip("Distance from the target drop before the companion stops to collect it.")]
        private float pickupStopDistance = 0.35f;

        [SerializeField]
        [Tooltip("Waypoint tolerance supplied to the path mover while approaching drops.")]
        private float pickupWaypointTolerance = 0.075f;

        [SerializeField]
        [Tooltip("Distance change required before the pickup path requests a fresh solution.")]
        private float pickupRepathDistance = 0.6f;

        [SerializeField]
        [Tooltip("Multiplier applied to the companion's follow speed while collecting drops.")]
        private float pickupMoveSpeedMultiplier = 1f;

        [SerializeField]
        [Tooltip("Seconds without progress before the pickup command gives up as obstructed.")]
        private float pickupStuckTimeoutSeconds = 2.5f;

        /// <summary>Backpack interface used to store collected loot.</summary>
        [SerializeField]
        [Tooltip("Inventory wrapper that stores collected drops. Resolved automatically when left blank.")]
        private CompanionInventory companionInventory;

        private PetFollower follower;
        private PetPathMover pathMover;
        private Rigidbody2D body2D;
        private PetSpriteAnimator spriteAnimator;
        private Coroutine pickupRoutine;
        private bool followerDisabledByPickup;
        private WaitForFixedUpdate pickupFixedUpdateYield;

        /// <summary>Raised whenever the pickup flow toggles the follower hold state.</summary>
        public event Action<bool> FollowerHoldChanged;

        /// <summary>Raised when an item stack has been added to the companion inventory.</summary>
        public event Action<ItemStack> PickupSucceeded;

        /// <summary>Raised when the pickup fails because the inventory is full.</summary>
        public event Action InventoryFull;

        /// <summary>True when the pickup routine has disabled the follower component.</summary>
        public bool HasActiveFollowerHold => followerDisabledByPickup;

        /// <summary>True while a pickup coroutine is actively steering the companion.</summary>
        public bool PickupInProgress => pickupRoutine != null;

        /// <summary>
        /// Wires dependencies required to perform pickup commands.
        /// </summary>
        /// <param name="controller">Owning companion controller.</param>
        /// <param name="inventory">Inventory that should receive collected drops.</param>
        public void Initialise(CompanionController controller, CompanionInventory inventory)
        {
            companionInventory = inventory != null
                ? inventory
                : controller != null && controller.Inventory != null
                    ? controller.Inventory
                    : GetComponent<CompanionInventory>();
            ResolveHelperComponents();
            pickupFixedUpdateYield ??= new WaitForFixedUpdate();
        }

        /// <summary>
        /// Directs the companion to collect the supplied world drop using the custom pathing stack.
        /// </summary>
        /// <param name="targetDrop">Drop the companion should attempt to collect.</param>
        public void CommandPickup(WorldDrop targetDrop)
        {
            if (targetDrop == null)
                return;

            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                return;

            if (!targetDrop.IsAvailable)
                return;

            CancelActivePickupInternal();
            pickupRoutine = StartCoroutine(PickupRoutine(targetDrop));
        }

        /// <summary>
        /// Cancels the active pickup routine and restores the follower state.
        /// </summary>
        public void CancelActivePickup()
        {
            CancelActivePickupInternal();
        }

        private IEnumerator PickupRoutine(WorldDrop targetDrop)
        {
            if (targetDrop == null)
                yield break;

            ResolveHelperComponents();
            DisableFollowerForPickup();

            pathMover?.ResetAttackTracking();
            pathMover?.ResetCachedVelocity();

            float lastProgressSample = Time.unscaledTime;
            float lastProgressDistance = float.MaxValue;
            Vector3 lastProgressPosition = body2D != null ? (Vector3)body2D.position : transform.position;
            const float progressPositionThreshold = 0.02f;
            const float progressDistanceThreshold = 0.01f;
            float stuckTimeout = Mathf.Max(0.1f, pickupStuckTimeoutSeconds);

            try
            {
                while (enabled)
                {
                    if (targetDrop == null || !targetDrop.IsAvailable)
                        break;

                    Transform pickupTransform = targetDrop.PickupTransform;
                    if (pickupTransform == null)
                        break;

                    float stopDistance = Mathf.Max(0.05f, pickupStopDistance);
                    Vector3 targetPosition = pickupTransform.position;
                    Vector3 currentPosition = body2D != null ? (Vector3)body2D.position : transform.position;
                    float distance = Vector2.Distance(currentPosition, targetPosition);

                    if (distance <= stopDistance)
                        break;

                    bool distanceImproved = distance <= lastProgressDistance - progressDistanceThreshold;
                    if (distanceImproved)
                    {
                        lastProgressSample = Time.unscaledTime;
                        lastProgressDistance = distance;
                        lastProgressPosition = currentPosition;
                    }
                    else
                    {
                        float movementDelta = Vector2.Distance(currentPosition, lastProgressPosition);
                        if (movementDelta >= progressPositionThreshold)
                        {
                            lastProgressSample = Time.unscaledTime;
                            lastProgressDistance = distance;
                            lastProgressPosition = currentPosition;
                        }
                        else if (Time.unscaledTime - lastProgressSample >= stuckTimeout)
                        {
                            if (CompanionManager.EnableDebugLogging)
                            {
                                Debug.Log("[Companion Pickup] Navigation stalled while approaching drop. Cancelled pickup.", this);
                            }

                            yield break;
                        }
                    }

                    bool reached = false;
                    Vector3 nextPosition = transform.position;
                    Vector2 velocity = Vector2.zero;
                    bool teleported = false;

                    if (TryStepWithNavigation(targetDrop, stopDistance, out nextPosition, out velocity, out teleported))
                    {
                        ApplyPickupMovement(nextPosition, velocity, teleported);
                    }
                    else
                    {
                        StepDirectlyTowards(targetPosition, ResolvePickupMoveSpeed(), out nextPosition, out velocity);
                        ApplyPickupMovement(nextPosition, velocity, teleported: false);
                    }

                    reached = Vector2.Distance(nextPosition, targetPosition) <= stopDistance;
                    if (reached)
                        break;

                    if (pickupFixedUpdateYield != null)
                        yield return pickupFixedUpdateYield;
                    else
                        yield return null;
                }

                if (targetDrop != null && targetDrop.IsAvailable)
                {
                    Transform pickupTransform = targetDrop.PickupTransform;
                    float stopDistance = Mathf.Max(0.05f, pickupStopDistance);
                    if (pickupTransform != null)
                    {
                        Vector3 evaluationPosition = body2D != null ? (Vector3)body2D.position : transform.position;
                        if (Vector2.Distance(evaluationPosition, pickupTransform.position) <= stopDistance + 0.05f)
                        {
                            FacePickup(pickupTransform.position);
                            TryCollectDrop(targetDrop);
                        }
                    }
                }
            }
            finally
            {
                ResetPickupMovementState();
                pickupRoutine = null;
            }
        }

        private bool TryStepWithNavigation(
            WorldDrop targetDrop,
            float stopDistance,
            out Vector3 nextPosition,
            out Vector2 velocity,
            out bool teleported)
        {
            nextPosition = transform.position;
            velocity = Vector2.zero;
            teleported = false;

            if (pathMover == null || !pathMover.isActiveAndEnabled)
                return false;

            if (!pathMover.HasActiveNavigationGrid)
                return false;

            float deltaTime = body2D != null
                ? Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon)
                : Mathf.Max(Time.deltaTime, Mathf.Epsilon);

            Vector2 navNext;
            Vector2 navVelocity;
            bool navTeleported;
            bool goalUnreachable;

            bool stepped = pathMover.TryStepAttack(
                deltaTime,
                ResolvePickupMoveSpeed(),
                stopDistance,
                Mathf.Max(0.01f, pickupWaypointTolerance),
                () => targetDrop != null && targetDrop.PickupTransform != null
                    ? (Vector2)targetDrop.PickupTransform.position
                    : (Vector2)transform.position,
                Mathf.Max(stopDistance * 0.75f, pickupRepathDistance),
                float.PositiveInfinity,
                out navNext,
                out navVelocity,
                out navTeleported,
                out goalUnreachable);

            if (goalUnreachable)
            {
                if (CompanionManager.EnableDebugLogging)
                {
                    Debug.Log("[Companion Pickup] Navigation reported the drop as unreachable. Falling back to direct steering.", this);
                }

                return false;
            }

            if (stepped)
            {
                nextPosition = new Vector3(navNext.x, navNext.y, transform.position.z);
                velocity = navVelocity;
                teleported = navTeleported;
                return true;
            }

            Vector3 currentPosition = body2D != null ? (Vector3)body2D.position : transform.position;
            nextPosition = new Vector3(currentPosition.x, currentPosition.y, transform.position.z);
            velocity = Vector2.zero;
            teleported = false;
            return true;
        }

        private void StepDirectlyTowards(Vector3 targetPosition, float moveSpeed, out Vector3 nextPosition, out Vector2 velocity)
        {
            Vector3 currentPosition = body2D != null ? (Vector3)body2D.position : transform.position;
            float deltaTime = body2D != null
                ? Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon)
                : Mathf.Max(Time.deltaTime, Mathf.Epsilon);

            nextPosition = Vector3.MoveTowards(currentPosition, targetPosition, moveSpeed * deltaTime);
            nextPosition.z = currentPosition.z;

            Vector2 displacement = nextPosition - currentPosition;
            float clampedDelta = Mathf.Max(deltaTime, Mathf.Epsilon);
            velocity = displacement / clampedDelta;
        }

        private void ApplyPickupMovement(Vector3 nextPosition, Vector2 velocity, bool teleported)
        {
            Vector3 currentPosition = body2D != null ? (Vector3)body2D.position : transform.position;
            Vector2 displacement = (Vector2)(nextPosition - currentPosition);

            if (body2D != null)
            {
                if (teleported)
                {
                    body2D.position = nextPosition;
                    body2D.linearVelocity = Vector2.zero;
                }
                else
                {
                    body2D.MovePosition(nextPosition);
                    body2D.linearVelocity = velocity;
                }
            }
            else
            {
                transform.position = nextPosition;
            }

            UpdatePickupMovementVisuals(displacement, teleported ? Vector2.zero : velocity);
        }

        private void UpdatePickupMovementVisuals(Vector2 displacement, Vector2 velocity)
        {
            if (spriteAnimator != null)
            {
                spriteAnimator.UpdateVisuals(velocity);
            }
        }

        private void DisableFollowerForPickup()
        {
            if (follower == null)
                return;

            if (followerDisabledByPickup)
                return;

            if (!follower.enabled)
                return;

            follower.enabled = false;
            followerDisabledByPickup = true;
            FollowerHoldChanged?.Invoke(true);
        }

        private void ResetPickupMovementState()
        {
            pathMover?.ResetAttackTracking();
            pathMover?.ResetFollowTracking();
            pathMover?.ResetCachedVelocity();

            if (body2D != null)
            {
                body2D.linearVelocity = Vector2.zero;
                body2D.angularVelocity = 0f;
            }

            spriteAnimator?.UpdateVisuals(Vector2.zero);

            transform.rotation = Quaternion.identity;

            if (followerDisabledByPickup && follower != null && !follower.enabled)
            {
                follower.enabled = true;
            }

            if (followerDisabledByPickup)
            {
                followerDisabledByPickup = false;
                FollowerHoldChanged?.Invoke(false);
            }
        }

        private float ResolvePickupMoveSpeed()
        {
            float baseSpeed = follower != null ? Mathf.Max(0.1f, follower.moveSpeed) : 5f;
            float multiplier = Mathf.Max(0.1f, pickupMoveSpeedMultiplier);
            return Mathf.Max(0.1f, baseSpeed * multiplier);
        }

        private void TryCollectDrop(WorldDrop drop)
        {
            if (drop == null || companionInventory == null)
                return;

            if (!drop.IsAvailable)
                return;

            ItemStack stack = drop.Stack;
            if (!stack.IsValid)
                return;

            bool added = companionInventory.TryAddItem(stack);
            if (added)
            {
                drop.Despawn();
                TryPlayPickupAnimation();
                MaybePostPickupSuccessMessage();
                PickupSucceeded?.Invoke(stack);
            }
            else
            {
                PostInventoryFullMessage();
                InventoryFull?.Invoke();
            }
        }

        private void FacePickup(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.z = 0f;

            Vector2 planar = new Vector2(direction.x, direction.y);
            if (spriteAnimator != null)
            {
                Direction8 facing = Direction8Utility.FromVector(planar, allowDiagonals: true, fallback: Direction8.Down);
                spriteAnimator.SetFacing(facing);
                spriteAnimator.UpdateVisuals(Vector2.zero);
            }

            transform.rotation = Quaternion.identity;
        }

        private void PostInventoryFullMessage()
        {
            string message = CompanionPickupDialogueLibrary.GetRandomInventoryFullResponse(ResolveActivePlayerName());
            if (string.IsNullOrEmpty(message))
                return;

            ChatboxUI.PostSystemMessage(message);
        }

        private void MaybePostPickupSuccessMessage()
        {
            if (!CompanionPickupDialogueLibrary.TryGetPickupSuccessResponse(ResolveActivePlayerName(), out string message))
                return;

            var chat = ChatService.Instance;
            if (chat != null)
            {
                string companionName = CompanionManager.GetCompanionDisplayName();
                if (string.IsNullOrWhiteSpace(companionName))
                    companionName = "Companion";

                chat.PublishCompanionMessage(companionName, message);
            }
            else
            {
                ChatboxUI.PostSystemMessage(message);
            }
        }

        private static string ResolveActivePlayerName()
        {
            var chat = ChatService.Instance;
            return chat != null ? chat.ActiveUsername : string.Empty;
        }

        private void TryPlayPickupAnimation()
        {
            const string controllerTypeName = "Companions.CompanionAnimationController";
            Type controllerType = Type.GetType(controllerTypeName) ??
                                    Type.GetType($"{controllerTypeName}, Assembly-CSharp");
            if (controllerType == null)
                return;

            Component component = GetComponent(controllerType) ?? GetComponentInChildren(controllerType, true);
            if (component == null)
                return;

            MethodInfo playPickup = controllerType.GetMethod("PlayPickup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            playPickup?.Invoke(component, null);
        }

        private void CancelActivePickupInternal()
        {
            if (pickupRoutine != null)
            {
                StopCoroutine(pickupRoutine);
                ResetPickupMovementState();
                pickupRoutine = null;
            }
        }

        private void ResolveHelperComponents()
        {
            companionInventory = companionInventory != null ? companionInventory : GetComponent<CompanionInventory>();
            follower ??= GetComponent<PetFollower>();
            pathMover ??= GetComponent<PetPathMover>();
            body2D ??= GetComponent<Rigidbody2D>();
            spriteAnimator ??= GetComponent<PetSpriteAnimator>() ?? GetComponentInChildren<PetSpriteAnimator>();
            pickupFixedUpdateYield ??= new WaitForFixedUpdate();
        }

        private void OnDisable()
        {
            CancelActivePickupInternal();
        }

        private void OnDestroy()
        {
            CancelActivePickupInternal();
        }
    }
}
