using Pets;
using UnityEngine;
using Util;

namespace Companions
{
    /// <summary>
    /// Provides shared follower, movement, and player binding utilities for gathering-focused companion controllers.
    /// Consolidates the repeated component lookups and reset routines that fishing, mining, woodcutting,
    /// and cooking controllers previously duplicated.
    /// </summary>
    public abstract class CompanionSkillingControllerBase : MonoBehaviour
    {
        /// <summary>
        /// Cached follower component responsible for tethering the companion to the player when idle.
        /// </summary>
        protected PetFollower petFollower;

        /// <summary>
        /// Handles waypoint-based pathing when the companion moves independently toward gathering targets.
        /// </summary>
        protected PetPathMover pathMover;

        /// <summary>
        /// Rigidbody reference used for manual velocity checks and nudges when handling stuck detection.
        /// </summary>
        protected Rigidbody2D body;

        /// <summary>
        /// Animator driving sprite flips and directional playback when the companion is animating.
        /// </summary>
        protected PetSpriteAnimator petSpriteAnimator;

        /// <summary>
        /// Sprite fallback that allows derived classes to flip sprites even when the animator is absent.
        /// </summary>
        protected SpriteRenderer fallbackSpriteRenderer;

        /// <summary>
        /// Tracks the last facing direction so derived classes can maintain directional idle state.
        /// </summary>
        protected Direction8 lastFacing = Direction8.Down;

        /// <summary>
        /// Indicates how many systems currently hold the follower disabled. Shared so cooldown routines
        /// and command layers can cooperate without fighting over re-enabling the follower.
        /// </summary>
        protected int followerDisableLockCount;

        /// <summary>
        /// Remembers whether the controller itself toggled the follower off so we can undo the change
        /// without stomping other locks sourced from UI or scripted events.
        /// </summary>
        protected bool followerHoldToggledFollower;

        /// <summary>
        /// Resolves all movement-related components shared across the companion skilling controllers.
        /// Must be invoked during initialisation before the derived controller attempts to path or animate.
        /// </summary>
        protected void InitialiseMovementComponents()
        {
            petFollower = GetComponent<PetFollower>();
            pathMover = GetComponent<PetPathMover>();
            body = GetComponent<Rigidbody2D>();
            petSpriteAnimator = GetComponent<PetSpriteAnimator>() ?? GetComponentInChildren<PetSpriteAnimator>();

            if (petSpriteAnimator != null)
            {
                fallbackSpriteRenderer = petSpriteAnimator.spriteRenderer;
                if (fallbackSpriteRenderer == null)
                {
                    fallbackSpriteRenderer = petSpriteAnimator.GetComponent<SpriteRenderer>() ??
                        petSpriteAnimator.GetComponentInChildren<SpriteRenderer>();
                }
            }
            else
            {
                fallbackSpriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
            }
        }

        /// <summary>
        /// Resets the follower related state so freshly initialised controllers start from a known baseline.
        /// Derived implementations can call this before setting controller-specific flags.
        /// </summary>
        protected void ResetFollowerState()
        {
            lastFacing = Direction8.Down;
            followerDisableLockCount = 0;
            followerHoldToggledFollower = false;
        }

        /// <summary>
        /// Sqr magnitude threshold that determines when facing changes should be ignored to avoid jitter.
        /// Derived classes can override this when a different tolerance is required for their animations.
        /// </summary>
        protected virtual float FacingDeadzoneSqrMagnitude => 0.0001f;

        /// <summary>
        /// Updates the cached player skill reference when the owning player object respawns or changes scenes.
        /// </summary>
        /// <typeparam name="TSkill">Concrete skill type that should be resolved from the player transform.</typeparam>
        /// <param name="playerTransform">Transform belonging to the active player instance.</param>
        /// <param name="cachedSkill">Reference that will receive the resolved component.</param>
        protected void RebindPlayerSkill<TSkill>(Transform playerTransform, ref TSkill cachedSkill)
            where TSkill : Component
        {
            cachedSkill = null;
            if (playerTransform == null)
                return;

            cachedSkill = playerTransform.GetComponent<TSkill>();
        }

        /// <summary>
        /// Applies the supplied movement step to the rigidbody (when present) and synchronises sprite
        /// orientation with the displacement so followers remain visually consistent across companion skills.
        /// </summary>
        /// <param name="nextPosition">Target world position for the current step.</param>
        /// <param name="velocity">Velocity that should be assigned to the rigidbody when advancing toward the step.</param>
        /// <param name="teleported">When true, the rigidbody is repositioned without velocity to avoid interpolation spikes.</param>
        protected void ApplyMovement(Vector3 nextPosition, Vector2 velocity, bool teleported)
        {
            Vector3 currentPosition = body != null ? (Vector3)body.position : transform.position;
            Vector2 displacement = (Vector2)(nextPosition - currentPosition);
            Vector2 appliedVelocity = teleported ? Vector2.zero : velocity;

            if (body != null)
            {
                if (teleported)
                {
                    body.position = nextPosition;
                    body.linearVelocity = Vector2.zero;
                }
                else
                {
                    body.MovePosition(nextPosition);
                    body.linearVelocity = appliedVelocity;
                }
            }
            else
            {
                transform.position = nextPosition;
            }

            UpdateMovementVisuals(displacement, appliedVelocity);
        }

        /// <summary>
        /// Updates the sprite orientation and animation vector so the companion continues to face its movement
        /// direction, even when movement steps are extremely small (common when approaching interaction targets).
        /// </summary>
        /// <param name="displacement">Raw displacement applied during this step.</param>
        /// <param name="appliedVelocity">Velocity forwarded to the rigidbody when advancing toward the step.</param>
        protected void UpdateMovementVisuals(Vector2 displacement, Vector2 appliedVelocity)
        {
            Vector2 visualVector = displacement.sqrMagnitude > FacingDeadzoneSqrMagnitude
                ? displacement
                : appliedVelocity;

            if (visualVector.sqrMagnitude > FacingDeadzoneSqrMagnitude)
                lastFacing = Direction8Utility.FromVector(visualVector, allowDiagonals: true, fallback: lastFacing);

            if (petSpriteAnimator != null)
            {
                petSpriteAnimator.SetFacing(lastFacing);
                petSpriteAnimator.UpdateVisuals(visualVector);
                return;
            }

            if (fallbackSpriteRenderer == null)
                return;

            fallbackSpriteRenderer.flipX = Direction8Utility.IsFacingLeft(lastFacing);
        }
    }
}
