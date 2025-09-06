using System;
using System.Collections;
using System.Collections.Generic;
using Combat;
using UnityEngine;
using Random = UnityEngine.Random;

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
        bool ambushRunning = false;
        Action onAllClonesGone;

        /// <summary>
        /// Initiates the spectral clone ambush around the target.
        /// </summary>
        /// <param name="owner">NPC initiating the ambush.</param>
        /// <param name="target">Target to ambush.</param>
        /// <param name="clonePrefabs">Possible clone prefabs.</param>
        /// <param name="cloneCount">Number of clones to spawn.</param>
        /// <param name="cloneLifespan">Lifetime of each clone.</param>
        /// <param name="spawnRadius">Radius around the target to spawn clones.</param>
        /// <param name="realCloneDamage">Damage dealt by the ambush.</param>
        /// <param name="onAllClonesGone">Callback invoked when all clones are destroyed.</param>
        /// <param name="preventConcurrent">If true, another ambush won't start while one is running.</param>
        public static void Perform(BaseNpcCombat owner, CombatTarget target, GameObject[] clonePrefabs,
            int cloneCount, float cloneLifespan, float spawnRadius, int realCloneDamage,
            Action onAllClonesGone = null, bool preventConcurrent = false)
        {
            if (owner == null || target == null || clonePrefabs == null || clonePrefabs.Length == 0 || cloneCount <= 0)
                return;

            var ambush = owner.GetComponent<SpectralCloneAmbush>();
            if (ambush == null)
                ambush = owner.gameObject.AddComponent<SpectralCloneAmbush>();

            if (preventConcurrent && ambush.ambushRunning)
                return;

            ambush.despawnDelay = cloneLifespan;
            ambush.onAllClonesGone = onAllClonesGone;
            ambush.blockSpawnUntilAllClonesDead = preventConcurrent;
            ambush.despawnAfterTime = true;

            ambush.StartCoroutine(ambush.AmbushRoutine(owner, target, clonePrefabs, cloneCount, spawnRadius, realCloneDamage));
        }

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

        IEnumerator AmbushRoutine(BaseNpcCombat owner, CombatTarget target, GameObject[] clonePrefabs,
            int cloneCount, float spawnRadius, int realCloneDamage)
        {
            ambushRunning = true;
            activeClones.Clear();

            for (int i = 0; i < cloneCount; i++)
            {
                Vector2 pos = (Vector2)target.transform.position + Random.insideUnitCircle * spawnRadius;
                GameObject prefab = clonePrefabs[Random.Range(0, clonePrefabs.Length)];
                SpawnClone(prefab, pos, Quaternion.identity);
            }

            if (realCloneDamage > 0 && target != null)
                target.ApplyDamage(realCloneDamage, DamageType.Magic, owner);

            yield break;
        }

        /// <summary>
        /// Called by clones when they are destroyed.
        /// </summary>
        /// <param name="clone">The clone that died.</param>
        public void OnCloneDeath(GameObject clone)
        {
            activeClones.Remove(clone);
            if (activeClones.Count == 0)
            {
                ambushRunning = false;
                onAllClonesGone?.Invoke();
            }
        }
    }
}
