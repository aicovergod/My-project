using UnityEngine;

namespace World
{
    /// <summary>
    /// Marks a location the player should respawn at after death.
    /// </summary>
    public class RespawnPoint : MonoBehaviour
    {
        /// <summary>
        /// The currently active respawn point in the scene.
        /// </summary>
        public static RespawnPoint Current { get; private set; }

        /// <summary>
        /// Registers this respawn point as the current active point when enabled.
        /// </summary>
        private void OnEnable()
        {
            Current = this;
        }

        /// <summary>
        /// Clears the static reference when this respawn point is disabled.
        /// </summary>
        private void OnDisable()
        {
            if (Current == this)
                Current = null;
        }

        /// <summary>
        /// Ensures the static reference is cleared if the respawn point is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }
    }
}
