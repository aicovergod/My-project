using UnityEngine;

namespace Util
{
    /// Attach to sprites that should auto‑sort by Y-position.
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteDepth : MonoBehaviour
    {
        public int offset;            // small positive/negative tweak if needed
        private SpriteRenderer sr;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
        }

        void LateUpdate()
        {
            // Larger (more negative) Y => lower sorting order => appears behind
            sr.sortingOrder = Mathf.RoundToInt(-transform.position.y * 100f) + offset;
        }
    }
}