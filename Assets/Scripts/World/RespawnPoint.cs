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

        private void OnEnable()
        {
            Current = this;
        }
    }
}
