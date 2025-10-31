using UnityEngine;
using Player;
using Player.Movement;

namespace Util
{
    /// Attach to sprites that should auto‑sort by Y-position.
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteDepth : MonoBehaviour
    {
        [SerializeField]
        private Transform sortingAnchor; // optional override for the transform whose Y drives sorting

        public int offset;            // small positive/negative tweak if needed
        public int directionOffset;   // magnitude for direction-based tweak

        private SpriteRenderer sr;
        private IPlayerMovementController movementController;

        void Awake()
        {
            EnsureAnchorReference();
            sr = GetComponent<SpriteRenderer>();
            movementController = FindObjectOfType<PlayerMovementController>();
            if (movementController == null)
            {
                var mover = FindObjectOfType<PlayerMover>();
                if (mover != null)
                    movementController = mover.MovementController;
            }
        }

        void OnValidate()
        {
            EnsureAnchorReference();
        }

        void LateUpdate()
        {
            int dir = 0;
            if (movementController != null)
            {
                Direction8 facing = movementController.FacingDirection;
                if (Direction8Utility.IsFacingDown(facing))
                    dir = directionOffset;
                else if (Direction8Utility.IsFacingUp(facing))
                    dir = -directionOffset;
            }

            // Larger (more negative) Y => lower sorting order => appears behind
            // Default to this component's transform when no custom anchor is provided.
            float anchorPositionY = (sortingAnchor != null ? sortingAnchor : transform).position.y;
            sr.sortingOrder = Mathf.RoundToInt(-anchorPositionY * 100f) + offset + dir;
        }

        private void EnsureAnchorReference()
        {
            // Guarantee the anchor defaults to this component so inspector overrides remain optional.
            if (sortingAnchor == null)
                sortingAnchor = transform;
        }
    }
}

