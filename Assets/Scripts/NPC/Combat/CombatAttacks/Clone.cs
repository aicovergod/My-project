using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Component attached to clone prefabs to notify the ambush manager when
    /// the clone is destroyed.
    /// </summary>
    public class Clone : MonoBehaviour
    {
        SpectralCloneAmbush ambush;

        /// <summary>
        /// Initialize the clone with a reference to the ambush that spawned it.
        /// </summary>
        public void Initialize(SpectralCloneAmbush owner)
        {
            ambush = owner;
        }

        void OnDestroy()
        {
            if (ambush != null)
            {
                ambush.OnCloneDeath(gameObject);
            }
        }
    }
}
