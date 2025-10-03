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

            movementFrozenByEat = false;
            if (TryFreezeMovement())
                movementFrozenByEat = true;

            if (spriteController != null)
            {
                spriteController.PlayConsumeAnimation(facingDirection, () =>
                {
                    if (movementFrozenByEat)
                        RestoreMovement();

                    ForceMovementVisualRefresh();

                    if (Time.time > nextEatTime)
                        nextEatTime = Time.time;
                });
            }
            else if (movementFrozenByEat)
            {
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
    }
}
