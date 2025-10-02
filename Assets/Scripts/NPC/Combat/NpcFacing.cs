using UnityEngine;
using Util;

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
        /// Most recent facing direction resolved through the shared <see cref="Direction8"/> helpers.
        /// </summary>
        public Direction8 FacingDirection { get; private set; } = Direction8.Down;

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

            FacingDirection = Direction8Utility.FromVector(direction, allowDiagonals: true, fallback: FacingDirection);

            if (spriteAnimator != null)
                spriteAnimator.SetFacing(Direction8Utility.ToAnimatorIndex(FacingDirection));
            else if (spriteRenderer != null)
                spriteRenderer.flipX = Direction8Utility.IsFacingRight(FacingDirection);
        }
    }
}
