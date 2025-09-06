using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat;

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
        /// Optional callback invoked when all spawned clones have been destroyed.
        /// </summary>
        public Action onAllClonesGone;

        // Owner used for tracking concurrent ambushes
        BaseNpcCombat owner;

        static readonly Dictionary<BaseNpcCombat, SpectralCloneAmbush> activeAmbushes =
            new Dictionary<BaseNpcCombat, SpectralCloneAmbush>();

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
            if (activeClones.Count == 0)
            {
                onAllClonesGone?.Invoke();
                if (owner != null)
                    activeAmbushes.Remove(owner);
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Spawns spectral clones around the target. A random clone deals real
        /// damage while the rest are decoys.
        /// </summary>
        public static void Perform(BaseNpcCombat owner, CombatTarget target,
            GameObject[] clonePrefabs, int cloneCount, float cloneLifespan,
            float spawnRadius, int realCloneDamage, Action onAllClonesGone = null,
            bool preventConcurrent = false)
        {
            if (owner == null || target == null || clonePrefabs == null ||
                clonePrefabs.Length == 0 || cloneCount <= 0)
                return;

            if (preventConcurrent && activeAmbushes.ContainsKey(owner))
                return;

            owner.StartCoroutine(SpawnClones(owner, target, clonePrefabs, cloneCount,
                cloneLifespan, spawnRadius, realCloneDamage, onAllClonesGone,
                preventConcurrent));
        }

        static IEnumerator SpawnClones(BaseNpcCombat owner, CombatTarget target,
            GameObject[] clonePrefabs, int cloneCount, float cloneLifespan,
            float spawnRadius, int realCloneDamage, Action onAllClonesGone,
            bool preventConcurrent)
        {
            var go = new GameObject("SpectralCloneAmbush");
            var ambush = go.AddComponent<SpectralCloneAmbush>();
            ambush.owner = owner;
            ambush.despawnAfterTime = cloneLifespan > 0f;
            ambush.despawnDelay = cloneLifespan;
            ambush.onAllClonesGone = onAllClonesGone;
            if (preventConcurrent)
                activeAmbushes[owner] = ambush;

            int realIndex = UnityEngine.Random.Range(0, cloneCount);

            for (int i = 0; i < cloneCount; i++)
            {
                var prefab = clonePrefabs[UnityEngine.Random.Range(0, clonePrefabs.Length)];
                if (prefab == null)
                    continue;

                float angle = i * Mathf.PI * 2f / cloneCount;
                Vector2 pos = (Vector2)target.transform.position +
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spawnRadius;
                var clone = ambush.SpawnClone(prefab, pos, Quaternion.identity);
                if (clone != null && i == realIndex && realCloneDamage > 0)
                {
                    target.ApplyDamage(realCloneDamage, DamageType.Magic, owner);
                }
            }

            yield break;
        }
    }
}
