using UnityEngine;
using Player;

namespace Util
{
    /// Attach to sprites that should auto‑sort by Y-position.
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteDepth : MonoBehaviour
    {
        public int offset;            // small positive/negative tweak if needed
        public int directionOffset;   // magnitude for direction-based tweak

        private SpriteRenderer sr;
        private PlayerMover player;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            player = FindObjectOfType<PlayerMover>();
        }

        void LateUpdate()
        {
            int dir = 0;
            if (player != null)
            {
                switch (player.FacingDir)
                {
                    case 0: // facing down
                        dir = directionOffset;
                        break;
                    case 3: // facing up
                        dir = -directionOffset;
                        break;
                }
            }

            // Larger (more negative) Y => lower sorting order => appears behind
            sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100f) + offset + dir;
        }
    }
}

