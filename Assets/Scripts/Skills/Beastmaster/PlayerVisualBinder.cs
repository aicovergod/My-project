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
        private Sprite origIdleDown, origIdleLeft, origIdleRight, origIdleUp;
        private Sprite origWalkDown, origWalkLeft, origWalkRight, origWalkUp;
        private Vector3 origScale;
        private bool origFlipX;
        private bool origUseFlipXForLeft, origUseFlipXForRight;

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
                origIdleLeft = spriteController.GetIdleSprite(Direction8.Left);
                origIdleRight = spriteController.GetIdleSprite(Direction8.Right);
                origIdleUp = spriteController.GetIdleSprite(Direction8.Up);
                origWalkDown = spriteController.GetWalkSprite(Direction8.Down);
                origWalkLeft = spriteController.GetWalkSprite(Direction8.Left);
                origWalkRight = spriteController.GetWalkSprite(Direction8.Right);
                origWalkUp = spriteController.GetWalkSprite(Direction8.Up);
                origUseFlipXForLeft = spriteController.UseFlipXForLeft;
                origUseFlipXForRight = spriteController.UseFlipXForRight;
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
                    if (profile.idleLeft != null) spriteController.SetIdleSprite(Direction8.Left, profile.idleLeft);
                    if (profile.idleRight != null) spriteController.SetIdleSprite(Direction8.Right, profile.idleRight);
                    if (profile.idleUp != null) spriteController.SetIdleSprite(Direction8.Up, profile.idleUp);
                    if (profile.walkDown != null) spriteController.SetWalkSprite(Direction8.Down, profile.walkDown);
                    if (profile.walkLeft != null) spriteController.SetWalkSprite(Direction8.Left, profile.walkLeft);
                    if (profile.walkRight != null) spriteController.SetWalkSprite(Direction8.Right, profile.walkRight);
                    if (profile.walkUp != null) spriteController.SetWalkSprite(Direction8.Up, profile.walkUp);
                    spriteController.UseFlipXForLeft = profile.useFlipXForLeft;
                    spriteController.UseFlipXForRight = profile.useFlipXForRight;
                }
                else
                {
                    spriteController.SetIdleSprite(Direction8.Down, null);
                    spriteController.SetIdleSprite(Direction8.Left, null);
                    spriteController.SetIdleSprite(Direction8.Right, null);
                    spriteController.SetIdleSprite(Direction8.Up, null);
                    spriteController.SetWalkSprite(Direction8.Down, null);
                    spriteController.SetWalkSprite(Direction8.Left, null);
                    spriteController.SetWalkSprite(Direction8.Right, null);
                    spriteController.SetWalkSprite(Direction8.Up, null);
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
                spriteController.SetIdleSprite(Direction8.Left, origIdleLeft);
                spriteController.SetIdleSprite(Direction8.Right, origIdleRight);
                spriteController.SetIdleSprite(Direction8.Up, origIdleUp);
                spriteController.SetWalkSprite(Direction8.Down, origWalkDown);
                spriteController.SetWalkSprite(Direction8.Left, origWalkLeft);
                spriteController.SetWalkSprite(Direction8.Right, origWalkRight);
                spriteController.SetWalkSprite(Direction8.Up, origWalkUp);
                spriteController.UseFlipXForLeft = origUseFlipXForLeft;
                spriteController.UseFlipXForRight = origUseFlipXForRight;
            }
            attackAnimator?.ClearPetLook();
        }
    }
}
