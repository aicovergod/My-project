using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Computes and applies sprite facing direction for NPCs.
    /// </summary>
    public class NpcFacing : MonoBehaviour
    {
        private NpcSpriteAnimator spriteAnimator;
        private SpriteRenderer spriteRenderer;

        /// <summary>
        /// Most recent facing direction.
        /// 0 = down, 1 = left, 2 = right, 3 = up.
        /// </summary>
        public int FacingDirection { get; private set; }

        /// <summary>
        /// Animator used for sprite swaps and attack animations.
        /// </summary>
        public NpcSpriteAnimator Animator => spriteAnimator;

        /// <summary>
        /// Renderer for simple sprite flipping when no animator is present.
        /// </summary>
        public SpriteRenderer Renderer => spriteRenderer;

        private void Awake()
        {
            spriteAnimator = GetComponent<NpcSpriteAnimator>() ?? GetComponentInChildren<NpcSpriteAnimator>();
            spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        }

        /// <summary>
        /// Faces the target transform, updating any animator or renderer.
        /// </summary>
        public void FaceTarget(Transform target)
        {
            if (target == null)
                return;

            Vector2 diff = target.position - transform.position;
            FaceDirection(diff);
        }

        /// <summary>
        /// Faces the supplied direction vector, updating any linked sprite animator or renderer.
        /// </summary>
        /// <param name="direction">Direction the NPC should face.</param>
        public void FaceDirection(Vector2 direction)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon)
                return;

            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
                FacingDirection = direction.x < 0f ? 1 : 2;
            else
                FacingDirection = direction.y < 0f ? 0 : 3;

            if (spriteAnimator != null)
                spriteAnimator.SetFacing(FacingDirection);
            else if (spriteRenderer != null)
                spriteRenderer.flipX = FacingDirection == 2;
        }
    }
}
