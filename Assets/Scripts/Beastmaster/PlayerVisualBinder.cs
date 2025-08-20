using UnityEngine;
using Pets;
using Player;

namespace Beastmaster
{
    /// <summary>
    /// Handles swapping the player's visuals when merging with a pet.
    /// </summary>
    public class PlayerVisualBinder : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerMover playerMover;
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
            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();
            if (playerMover == null)
                playerMover = GetComponentInChildren<PlayerMover>();
            if (attackAnimator == null)
                attackAnimator = GetComponent<MergedPetAttackAnimator>();
            if (attackAnimator == null)
                attackAnimator = gameObject.AddComponent<MergedPetAttackAnimator>();
            if (spriteRenderer != null)
                originalSprite = spriteRenderer.sprite;
            if (animator != null)
                originalController = animator.runtimeAnimatorController;
            if (playerMover != null)
            {
                origIdleDown = playerMover.idleDown;
                origIdleLeft = playerMover.idleLeft;
                origIdleRight = playerMover.idleRight;
                origIdleUp = playerMover.idleUp;
                origWalkDown = playerMover.walkDown;
                origWalkLeft = playerMover.walkLeft;
                origWalkRight = playerMover.walkRight;
                origWalkUp = playerMover.walkUp;
                origUseFlipXForLeft = playerMover.useFlipXForLeft;
                origUseFlipXForRight = playerMover.useFlipXForRight;
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
            if (playerMover != null)
            {
                if (profile.controller == null)
                {
                    if (profile.idleDown != null) playerMover.idleDown = profile.idleDown;
                    if (profile.idleLeft != null) playerMover.idleLeft = profile.idleLeft;
                    if (profile.idleRight != null) playerMover.idleRight = profile.idleRight;
                    if (profile.idleUp != null) playerMover.idleUp = profile.idleUp;
                    if (profile.walkDown != null) playerMover.walkDown = profile.walkDown;
                    if (profile.walkLeft != null) playerMover.walkLeft = profile.walkLeft;
                    if (profile.walkRight != null) playerMover.walkRight = profile.walkRight;
                    if (profile.walkUp != null) playerMover.walkUp = profile.walkUp;
                    playerMover.useFlipXForLeft = profile.useFlipXForLeft;
                    playerMover.useFlipXForRight = profile.useFlipXForRight;
                }
                else
                {
                    playerMover.idleDown = null;
                    playerMover.idleLeft = null;
                    playerMover.idleRight = null;
                    playerMover.idleUp = null;
                    playerMover.walkDown = null;
                    playerMover.walkLeft = null;
                    playerMover.walkRight = null;
                    playerMover.walkUp = null;
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
            if (playerMover != null)
            {
                playerMover.idleDown = origIdleDown;
                playerMover.idleLeft = origIdleLeft;
                playerMover.idleRight = origIdleRight;
                playerMover.idleUp = origIdleUp;
                playerMover.walkDown = origWalkDown;
                playerMover.walkLeft = origWalkLeft;
                playerMover.walkRight = origWalkRight;
                playerMover.walkUp = origWalkUp;
                playerMover.useFlipXForLeft = origUseFlipXForLeft;
                playerMover.useFlipXForRight = origUseFlipXForRight;
            }
            attackAnimator?.ClearPetLook();
        }
    }
}
