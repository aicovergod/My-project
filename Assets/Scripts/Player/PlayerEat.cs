using UnityEngine;
using Inventory;
using Player.Movement;
using Player.Visuals;
using Util;

namespace Player
{
    /// <summary>
    /// Handles consuming food items to restore player hitpoints.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerEat : MonoBehaviour
    {
        private PlayerHitpoints hitpoints;
        [SerializeField]
        private PlayerMover playerMover;
        [SerializeField]
        private PlayerMovementController movementController;
        [SerializeField]
        private PlayerSpriteController spriteController;

        /// <summary>Tracks whether this component is responsible for the current movement freeze state.</summary>
        private bool movementFrozenByEat;

        private float nextEatTime;
        private const float EatDelay = 0.6f; // Delay between eating actions in seconds

        private void Awake()
        {
            hitpoints = GetComponent<PlayerHitpoints>();
            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();

            if (movementController == null)
            {
                if (playerMover != null)
                    movementController = playerMover.MovementController;
                if (movementController == null)
                    movementController = GetComponent<PlayerMovementController>();
            }

            if (spriteController == null)
            {
                if (playerMover != null)
                    spriteController = playerMover.SpriteController;
                if (spriteController == null)
                    spriteController = GetComponent<PlayerSpriteController>();
            }
        }

        /// <summary>
        /// Consume the given food item and heal the player.
        /// </summary>
        /// <param name="item">Item data describing the food.</param>
        /// <returns>True if the item was consumed.</returns>
        public bool Eat(ItemData item)
        {
            if (item == null || hitpoints == null || item.healAmount <= 0)
                return false;

            if (Time.time < nextEatTime)
                return false;

            hitpoints.Heal(item.healAmount);

            ResolveMovementState(out Direction8 facingDirection, out _);

            bool cachedFreezeSprite = spriteController != null && spriteController.FreezeSprite;
            bool spriteFreezeChangedByEat = false;
            bool obtainedMovementLockThisCall = false;

            if (!movementFrozenByEat)
            {
                obtainedMovementLockThisCall = TryFreezeMovement();
                if (obtainedMovementLockThisCall)
                    movementFrozenByEat = true;
            }

            if (movementFrozenByEat && spriteController != null)
            {
                // Only adjust the sprite freeze flag if we either grabbed the movement lock just now
                // or if another system re-enabled sprite freezing while we still own the lock.
                bool shouldUnfreezeSprite = obtainedMovementLockThisCall || spriteController.FreezeSprite;
                if (shouldUnfreezeSprite)
                {
                    spriteFreezeChangedByEat = spriteController.FreezeSprite;
                    if (spriteFreezeChangedByEat)
                        spriteController.FreezeSprite = false;
                }
            }

            if (spriteController != null)
            {
                bool capturedMovementFrozenByEat = movementFrozenByEat;
                bool capturedSpriteFreezeChanged = spriteFreezeChangedByEat;
                bool capturedCachedFreezeSprite = cachedFreezeSprite;

                spriteController.PlayConsumeAnimation(facingDirection, () =>
                {
                    RestoreSpriteFreezeState(capturedSpriteFreezeChanged, capturedCachedFreezeSprite);

                    if (capturedMovementFrozenByEat)
                        RestoreMovement();

                    ForceMovementVisualRefresh();

                    if (Time.time > nextEatTime)
                        nextEatTime = Time.time;
                });
            }
            else
            {
                RestoreSpriteFreezeState(spriteFreezeChangedByEat, cachedFreezeSprite);

                if (movementFrozenByEat)
                    RestoreMovement();
            }

            nextEatTime = Time.time + EatDelay;
            return true;
        }

        /// <summary>
        /// Attempts to freeze player movement while the consume animation plays.
        /// </summary>
        private bool TryFreezeMovement()
        {
            if (playerMover != null)
            {
                if (!playerMover.IsMovementFrozen)
                {
                    playerMover.SetMovementFrozen(true);
                    return true;
                }
                return false;
            }

            if (movementController != null && !movementController.IsMovementFrozen)
            {
                movementController.SetMovementFrozen(true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Restores player movement if it was frozen by this component.
        /// </summary>
        private void RestoreMovement()
        {
            if (playerMover != null)
                playerMover.SetMovementFrozen(false);
            else if (movementController != null)
                movementController.SetMovementFrozen(false);

            movementFrozenByEat = false;
        }

        /// <summary>
        /// Refreshes the movement visuals once the consume animation has ended.
        /// </summary>
        private void ForceMovementVisualRefresh()
        {
            if (spriteController == null)
                return;

            ResolveMovementState(out Direction8 direction, out bool isMoving);
            spriteController.ApplyMovementVisuals(direction, isMoving);
        }

        /// <summary>
        /// Resolves the player's current facing direction and movement state.
        /// </summary>
        private void ResolveMovementState(out Direction8 direction, out bool isMoving)
        {
            if (playerMover != null)
            {
                direction = playerMover.FacingDir;
                isMoving = playerMover.IsMoving;
                return;
            }

            if (movementController != null)
            {
                direction = movementController.FacingDirection;
                isMoving = movementController.IsMoving;
                return;
            }

            direction = Direction8.Down;
            isMoving = false;
        }

        /// <summary>
        /// Restores the sprite freeze flag to its cached value if this component modified it.
        /// </summary>
        /// <param name="changedByEat">True if the eat action changed the freeze state.</param>
        /// <param name="cachedState">The cached freeze state captured before eating.</param>
        private void RestoreSpriteFreezeState(bool changedByEat, bool cachedState)
        {
            if (!changedByEat || spriteController == null)
                return;

            spriteController.FreezeSprite = cachedState;
        }
    }
}
