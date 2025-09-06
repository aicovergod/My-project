using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Handles spawning and tracking of spectral clones. Clones may optionally
    /// despawn after a delay and new spawns can be blocked until all existing
    /// clones have been destroyed.
    /// </summary>
    public class SpectralCloneAmbush : MonoBehaviour
    {
        [SerializeField] bool despawnAfterTime = true;
        [SerializeField] float despawnDelay = 10f;
        [SerializeField] bool blockSpawnUntilAllClonesDead = false;

        readonly List<GameObject> activeClones = new List<GameObject>();

        /// <summary>
        /// Spawn a clone from the given prefab.
        /// </summary>
        /// <param name="prefab">Clone prefab to instantiate.</param>
        /// <param name="position">World position for the clone.</param>
        /// <param name="rotation">World rotation for the clone.</param>
        /// <returns>The instantiated clone or null if spawning was blocked.</returns>
        public GameObject SpawnClone(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (blockSpawnUntilAllClonesDead && activeClones.Count > 0)
            {
                return null;
            }

            var clone = Instantiate(prefab, position, rotation);
            activeClones.Add(clone);

            var cloneScript = clone.GetComponent<Clone>();
            if (cloneScript != null)
            {
                cloneScript.Initialize(this);
            }

            if (despawnAfterTime)
            {
                StartCoroutine(DespawnRoutine(clone));
            }

            return clone;
        }

        IEnumerator DespawnRoutine(GameObject clone)
        {
            yield return new WaitForSeconds(despawnDelay);
            if (clone != null)
            {
                Destroy(clone);
            }
        }

        /// <summary>
        /// Called by clones when they are destroyed.
        /// </summary>
        /// <param name="clone">The clone that died.</param>
        public void OnCloneDeath(GameObject clone)
        {
            activeClones.Remove(clone);
        }
    }
}
