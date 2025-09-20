using UnityEngine;
using Player;

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

        [Tooltip("Optional SpawnPoint marker that shares this respawn location for scene transitions.")]
        [SerializeField]
        private SpawnPoint linkedSpawnPoint;

        /// <summary>
        /// Provides the identifier of an associated <see cref="SpawnPoint"/> if one is present.
        /// </summary>
        public string SpawnIdentifier => linkedSpawnPoint != null ? linkedSpawnPoint.id : null;

        /// <summary>
        /// Retrieves the Unity scene name that owns this respawn marker.
        /// </summary>
        public string SceneName => gameObject.scene.name;

        /// <summary>
        /// Returns the precise world-space position of the respawn marker.
        /// </summary>
        public Vector3 WorldPosition => transform.position;

        private void Reset()
        {
            // Default the linked spawn point to the local component so designers do not
            // need to wire the field manually when the SpawnPoint lives on the same object.
            if (linkedSpawnPoint == null)
                linkedSpawnPoint = GetComponent<SpawnPoint>();
        }

        private void Awake()
        {
            if (linkedSpawnPoint == null)
                linkedSpawnPoint = GetComponent<SpawnPoint>();
        }

        /// <summary>
        /// Registers this respawn point as the current active point when enabled.
        /// </summary>
        private void OnEnable()
        {
            if (linkedSpawnPoint == null)
                linkedSpawnPoint = GetComponent<SpawnPoint>();

            Current = this;

            // Immediately notify the respawn system so it can cache the scene, spawn
            // identifier and fallback position instead of relying solely on the static
            // Current pointer.
            if (PlayerRespawnSystem.Instance != null)
                PlayerRespawnSystem.Instance.RegisterRespawnPoint(this);
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
