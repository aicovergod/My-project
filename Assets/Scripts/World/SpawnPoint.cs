using UnityEngine;

namespace World
{
    /// <summary>
    /// Simple marker component used to designate spawn locations that doors can
    /// send the player to when loading a new scene.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        [Tooltip("Identifier used by doors to look up this spawn point in the target scene.")]
        public string id;
    }
}
