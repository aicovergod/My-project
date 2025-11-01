using UnityEngine;
using Player;
using Player.Movement;

namespace Util
{
    /// Attach to sprites that should auto‑sort by Y-position.
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteDepth : MonoBehaviour
    {
        public int offset;            // small positive/negative tweak if needed
        public int directionOffset;   // magnitude for direction-based tweak

        private SpriteRenderer sr;
        private IPlayerMovementController movementController;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            movementController = FindObjectOfType<PlayerMovementController>();
            if (movementController == null)
            {
                var mover = FindObjectOfType<PlayerMover>();
                if (mover != null)
                    movementController = mover.MovementController;
            }
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
            sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100f) + offset + dir;
        }
    }
}

