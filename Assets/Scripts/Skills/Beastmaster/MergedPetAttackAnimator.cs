using UnityEngine;
using Combat;
using Pets;
using Player;
using Player.Movement;
using Player.Visuals;
using Util;

namespace Beastmaster
{
    /// <summary>
    /// Plays pet attack animations when the player attacks while merged with a pet.
    /// </summary>
    public class MergedPetAttackAnimator : MonoBehaviour
    {
        [SerializeField] private CombatController combat;
        [SerializeField] private PlayerMovementController movementController;
        [SerializeField] private PlayerSpriteController spriteController;
        [SerializeField] private Animator animator;
        [SerializeField] private PetSpriteAnimator spriteAnimator;

        private void Awake()
        {
            if (combat == null)
                combat = GetComponent<CombatController>() ?? GetComponentInParent<CombatController>();
            if (movementController == null)
                movementController = GetComponent<PlayerMovementController>() ?? GetComponentInChildren<PlayerMovementController>();
            if (movementController == null)
            {
                var moverFacade = GetComponent<PlayerMover>() ?? GetComponentInChildren<PlayerMover>();
                if (moverFacade != null)
                    movementController = moverFacade.MovementController;
            }
            if (spriteController == null)
                spriteController = GetComponent<PlayerSpriteController>() ?? GetComponentInChildren<PlayerSpriteController>();
            if (spriteController == null)
            {
                var moverFacade = GetComponent<PlayerMover>() ?? GetComponentInChildren<PlayerMover>();
                if (moverFacade != null)
                    spriteController = moverFacade.SpriteController;
            }
            if (animator == null)
                animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
            if (spriteAnimator == null)
                spriteAnimator = GetComponent<PetSpriteAnimator>();
            if (spriteAnimator == null)
                spriteAnimator = gameObject.AddComponent<PetSpriteAnimator>();
        }

        private void OnEnable()
        {
            if (combat != null)
                combat.OnAttackStart += HandleAttack;
        }

        private void OnDisable()
        {
            if (combat != null)
                combat.OnAttackStart -= HandleAttack;
        }

        public void ApplyPetLook(PetVisualProfile profile)
        {
            if (profile == null || spriteAnimator == null)
                return;
            if (spriteAnimator.spriteRenderer == null)
                spriteAnimator.spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
            spriteAnimator.hitDown = profile.hitDown;
            spriteAnimator.hitDownRight = profile.hitDownRight;
            spriteAnimator.hitRight = profile.hitRight;
            spriteAnimator.hitUpRight = profile.hitUpRight;
            spriteAnimator.hitUp = profile.hitUp;
            spriteAnimator.hitUpLeft = profile.hitUpLeft;
            spriteAnimator.hitLeft = profile.hitLeft;
            spriteAnimator.hitDownLeft = profile.hitDownLeft;
            spriteAnimator.useFlipXForLeft = profile.useFlipXForLeft;
            spriteAnimator.useFlipXForRight = profile.useFlipXForRight;
            spriteAnimator.useFlipXForDownLeft = profile.useFlipXForDownLeft;
            spriteAnimator.useFlipXForUpLeft = profile.useFlipXForUpLeft;
            spriteAnimator.useFlipXForUpRight = profile.useFlipXForUpRight;
            spriteAnimator.useFlipXForDownRight = profile.useFlipXForDownRight;
        }

        public void ClearPetLook()
        {
            if (spriteAnimator == null)
                return;
            spriteAnimator.hitDown = null;
            spriteAnimator.hitDownRight = null;
            spriteAnimator.hitLeft = null;
            spriteAnimator.hitRight = null;
            spriteAnimator.hitUp = null;
            spriteAnimator.hitUpRight = null;
            spriteAnimator.hitUpLeft = null;
            spriteAnimator.hitDownLeft = null;
        }

        private void HandleAttack()
        {
            Direction8 dir = movementController != null ? movementController.FacingDirection : Direction8.Down;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetInteger("Dir", Direction8Utility.ToAnimatorIndex8(dir));
                animator.SetTrigger("Attack");
            }
            if (spriteAnimator != null && spriteAnimator.HasHitAnimation(dir))
                StartCoroutine(PlayHit(dir));
        }

        private System.Collections.IEnumerator PlayHit(Direction8 dir)
        {
            bool originalFreeze = spriteController != null && spriteController.FreezeSprite;
            if (spriteController != null)
                spriteController.FreezeSprite = true;
            yield return StartCoroutine(spriteAnimator.PlayHitAnimation(dir));
            if (spriteController != null)
                spriteController.FreezeSprite = originalFreeze;
        }
    }
}
