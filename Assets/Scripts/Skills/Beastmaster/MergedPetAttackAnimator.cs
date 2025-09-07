using UnityEngine;
using Combat;
using Pets;
using Player;

namespace Beastmaster
{
    /// <summary>
    /// Plays pet attack animations when the player attacks while merged with a pet.
    /// </summary>
    public class MergedPetAttackAnimator : MonoBehaviour
    {
        [SerializeField] private CombatController combat;
        [SerializeField] private PlayerMover mover;
        [SerializeField] private Animator animator;
        [SerializeField] private PetSpriteAnimator spriteAnimator;

        private void Awake()
        {
            if (combat == null)
                combat = GetComponent<CombatController>() ?? GetComponentInParent<CombatController>();
            if (mover == null)
                mover = GetComponent<PlayerMover>() ?? GetComponentInChildren<PlayerMover>();
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
            spriteAnimator.hitLeft = profile.hitLeft;
            spriteAnimator.hitRight = profile.hitRight;
            spriteAnimator.hitUp = profile.hitUp;
            spriteAnimator.useFlipXForLeft = profile.useFlipXForLeft;
            spriteAnimator.useFlipXForRight = profile.useFlipXForRight;
        }

        public void ClearPetLook()
        {
            if (spriteAnimator == null)
                return;
            spriteAnimator.hitDown = null;
            spriteAnimator.hitLeft = null;
            spriteAnimator.hitRight = null;
            spriteAnimator.hitUp = null;
        }

        private void HandleAttack()
        {
            int dir = mover != null ? mover.FacingDir : 0;
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetTrigger("Attack");
            if (spriteAnimator != null && spriteAnimator.HasHitAnimation(dir))
                StartCoroutine(PlayHit(dir));
        }

        private System.Collections.IEnumerator PlayHit(int dir)
        {
            if (mover != null)
                mover.freezeSprite = true;
            yield return StartCoroutine(spriteAnimator.PlayHitAnimation(dir));
            if (mover != null)
                mover.freezeSprite = false;
        }
    }
}
