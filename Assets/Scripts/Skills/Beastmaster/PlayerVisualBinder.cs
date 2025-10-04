using UnityEngine;
using Pets;
using Player;
using Player.Visuals;
using Util;

namespace Beastmaster
{
    /// <summary>
    /// Handles swapping the player's visuals when merging with a pet.
    /// </summary>
    public class PlayerVisualBinder : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerSpriteController spriteController;
        [SerializeField] private MergedPetAttackAnimator attackAnimator;

        private RuntimeAnimatorController originalController;
        private Sprite originalSprite;
        private Sprite origIdleDown, origIdleDownRight, origIdleRight, origIdleUpRight, origIdleUp, origIdleUpLeft, origIdleLeft, origIdleDownLeft;
        private Sprite origWalkDown, origWalkDownRight, origWalkRight, origWalkUpRight, origWalkUp, origWalkUpLeft, origWalkLeft, origWalkDownLeft;
        private Vector3 origScale;
        private bool origFlipX;
        private bool origUseFlipXForLeft, origUseFlipXForRight;
        private bool origUseFlipXForDownLeft, origUseFlipXForUpLeft, origUseFlipXForUpRight, origUseFlipXForDownRight;

        private void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (animator == null)
                animator = GetComponent<Animator>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (spriteController == null)
                spriteController = GetComponent<PlayerSpriteController>() ?? GetComponentInChildren<PlayerSpriteController>();
            if (spriteController == null)
            {
                var mover = GetComponent<PlayerMover>() ?? GetComponentInChildren<PlayerMover>();
                if (mover != null)
                    spriteController = mover.SpriteController;
            }
            if (attackAnimator == null)
                attackAnimator = GetComponent<MergedPetAttackAnimator>();
            if (attackAnimator == null)
                attackAnimator = gameObject.AddComponent<MergedPetAttackAnimator>();
            if (spriteRenderer != null)
                originalSprite = spriteRenderer.sprite;
            if (animator != null)
                originalController = animator.runtimeAnimatorController;
            if (spriteController != null)
            {
                origIdleDown = spriteController.GetIdleSprite(Direction8.Down);
                origIdleDownRight = spriteController.GetIdleSprite(Direction8.DownRight);
                origIdleLeft = spriteController.GetIdleSprite(Direction8.Left);
                origIdleUpLeft = spriteController.GetIdleSprite(Direction8.UpLeft);
                origIdleRight = spriteController.GetIdleSprite(Direction8.Right);
                origIdleUpRight = spriteController.GetIdleSprite(Direction8.UpRight);
                origIdleUp = spriteController.GetIdleSprite(Direction8.Up);
                origIdleDownLeft = spriteController.GetIdleSprite(Direction8.DownLeft);
                origWalkDown = spriteController.GetWalkSprite(Direction8.Down);
                origWalkDownRight = spriteController.GetWalkSprite(Direction8.DownRight);
                origWalkLeft = spriteController.GetWalkSprite(Direction8.Left);
                origWalkUpLeft = spriteController.GetWalkSprite(Direction8.UpLeft);
                origWalkRight = spriteController.GetWalkSprite(Direction8.Right);
                origWalkUpRight = spriteController.GetWalkSprite(Direction8.UpRight);
                origWalkUp = spriteController.GetWalkSprite(Direction8.Up);
                origWalkDownLeft = spriteController.GetWalkSprite(Direction8.DownLeft);
                origUseFlipXForLeft = spriteController.UseFlipXForLeft;
                origUseFlipXForRight = spriteController.UseFlipXForRight;
                origUseFlipXForDownLeft = spriteController.UseFlipXForDownLeft;
                origUseFlipXForUpLeft = spriteController.UseFlipXForUpLeft;
                origUseFlipXForUpRight = spriteController.UseFlipXForUpRight;
                origUseFlipXForDownRight = spriteController.UseFlipXForDownRight;
            }
            origScale = transform.localScale;
            origFlipX = spriteRenderer != null && spriteRenderer.flipX;
        }

        /// <summary>
        /// Apply the visual appearance of a pet to the player.
        /// </summary>
        public void ApplyPetLook(PetVisualProfile profile)
        {
            if (profile == null)
                return;
            if (animator != null && profile.controller != null)
                animator.runtimeAnimatorController = profile.controller;
            if (spriteRenderer != null && profile.baseSprite != null)
                spriteRenderer.sprite = profile.baseSprite;
            if (spriteController != null)
            {
                if (profile.controller == null)
                {
                    if (profile.idleDown != null) spriteController.SetIdleSprite(Direction8.Down, profile.idleDown);
                    spriteController.SetIdleSprite(Direction8.DownRight, profile.idleDownRight);
                    if (profile.idleLeft != null) spriteController.SetIdleSprite(Direction8.Left, profile.idleLeft);
                    spriteController.SetIdleSprite(Direction8.UpLeft, profile.idleUpLeft);
                    if (profile.idleRight != null) spriteController.SetIdleSprite(Direction8.Right, profile.idleRight);
                    spriteController.SetIdleSprite(Direction8.UpRight, profile.idleUpRight);
                    if (profile.idleUp != null) spriteController.SetIdleSprite(Direction8.Up, profile.idleUp);
                    spriteController.SetIdleSprite(Direction8.DownLeft, profile.idleDownLeft);
                    if (profile.walkDown != null) spriteController.SetWalkSprite(Direction8.Down, profile.walkDown);
                    spriteController.SetWalkSprite(Direction8.DownRight, profile.walkDownRight);
                    if (profile.walkLeft != null) spriteController.SetWalkSprite(Direction8.Left, profile.walkLeft);
                    spriteController.SetWalkSprite(Direction8.UpLeft, profile.walkUpLeft);
                    if (profile.walkRight != null) spriteController.SetWalkSprite(Direction8.Right, profile.walkRight);
                    spriteController.SetWalkSprite(Direction8.UpRight, profile.walkUpRight);
                    if (profile.walkUp != null) spriteController.SetWalkSprite(Direction8.Up, profile.walkUp);
                    spriteController.SetWalkSprite(Direction8.DownLeft, profile.walkDownLeft);
                    spriteController.UseFlipXForLeft = profile.useFlipXForLeft;
                    spriteController.UseFlipXForRight = profile.useFlipXForRight;
                    spriteController.UseFlipXForDownLeft = profile.useFlipXForDownLeft;
                    spriteController.UseFlipXForUpLeft = profile.useFlipXForUpLeft;
                    spriteController.UseFlipXForUpRight = profile.useFlipXForUpRight;
                    spriteController.UseFlipXForDownRight = profile.useFlipXForDownRight;
                }
                else
                {
                    spriteController.SetIdleSprite(Direction8.Down, null);
                    spriteController.SetIdleSprite(Direction8.DownRight, null);
                    spriteController.SetIdleSprite(Direction8.Left, null);
                    spriteController.SetIdleSprite(Direction8.UpLeft, null);
                    spriteController.SetIdleSprite(Direction8.Right, null);
                    spriteController.SetIdleSprite(Direction8.UpRight, null);
                    spriteController.SetIdleSprite(Direction8.Up, null);
                    spriteController.SetIdleSprite(Direction8.DownLeft, null);
                    spriteController.SetWalkSprite(Direction8.Down, null);
                    spriteController.SetWalkSprite(Direction8.DownRight, null);
                    spriteController.SetWalkSprite(Direction8.Left, null);
                    spriteController.SetWalkSprite(Direction8.UpLeft, null);
                    spriteController.SetWalkSprite(Direction8.Right, null);
                    spriteController.SetWalkSprite(Direction8.UpRight, null);
                    spriteController.SetWalkSprite(Direction8.Up, null);
                    spriteController.SetWalkSprite(Direction8.DownLeft, null);
                }
            }
            transform.localScale = profile.localScale;
            if (spriteRenderer != null)
                spriteRenderer.flipX = false;
            attackAnimator?.ApplyPetLook(profile);
        }

        /// <summary>
        /// Restore the player's original visuals.
        /// </summary>
        public void RestorePlayerLook()
        {
            if (animator != null)
                animator.runtimeAnimatorController = originalController;
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = originalSprite;
                spriteRenderer.flipX = origFlipX;
            }
            transform.localScale = origScale;
            if (spriteController != null)
            {
                spriteController.SetIdleSprite(Direction8.Down, origIdleDown);
                spriteController.SetIdleSprite(Direction8.DownRight, origIdleDownRight);
                spriteController.SetIdleSprite(Direction8.Left, origIdleLeft);
                spriteController.SetIdleSprite(Direction8.UpLeft, origIdleUpLeft);
                spriteController.SetIdleSprite(Direction8.Right, origIdleRight);
                spriteController.SetIdleSprite(Direction8.UpRight, origIdleUpRight);
                spriteController.SetIdleSprite(Direction8.Up, origIdleUp);
                spriteController.SetIdleSprite(Direction8.DownLeft, origIdleDownLeft);
                spriteController.SetWalkSprite(Direction8.Down, origWalkDown);
                spriteController.SetWalkSprite(Direction8.DownRight, origWalkDownRight);
                spriteController.SetWalkSprite(Direction8.Left, origWalkLeft);
                spriteController.SetWalkSprite(Direction8.UpLeft, origWalkUpLeft);
                spriteController.SetWalkSprite(Direction8.Right, origWalkRight);
                spriteController.SetWalkSprite(Direction8.UpRight, origWalkUpRight);
                spriteController.SetWalkSprite(Direction8.Up, origWalkUp);
                spriteController.SetWalkSprite(Direction8.DownLeft, origWalkDownLeft);
                spriteController.UseFlipXForLeft = origUseFlipXForLeft;
                spriteController.UseFlipXForRight = origUseFlipXForRight;
                spriteController.UseFlipXForDownLeft = origUseFlipXForDownLeft;
                spriteController.UseFlipXForUpLeft = origUseFlipXForUpLeft;
                spriteController.UseFlipXForUpRight = origUseFlipXForUpRight;
                spriteController.UseFlipXForDownRight = origUseFlipXForDownRight;
            }
            attackAnimator?.ClearPetLook();
        }
    }
}
