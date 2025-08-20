using UnityEngine;
using Pets;

namespace Beastmaster
{
    /// <summary>
    /// Handles swapping the player's visuals when merging with a pet.
    /// </summary>
    public class PlayerVisualBinder : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Animator animator;

        private RuntimeAnimatorController originalController;
        private Sprite originalSprite;

        private void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (animator == null)
                animator = GetComponent<Animator>();
            if (spriteRenderer != null)
                originalSprite = spriteRenderer.sprite;
            if (animator != null)
                originalController = animator.runtimeAnimatorController;
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
        }

        /// <summary>
        /// Restore the player's original visuals.
        /// </summary>
        public void RestorePlayerLook()
        {
            if (animator != null)
                animator.runtimeAnimatorController = originalController;
            if (spriteRenderer != null)
                spriteRenderer.sprite = originalSprite;
        }
    }
}
