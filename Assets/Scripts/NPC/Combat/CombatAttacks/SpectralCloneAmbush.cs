using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Combat;

namespace NPC
{
    /// <summary>
    /// Utility to spawn spectral clones around a target. One clone deals real damage
    /// while the rest are harmless decoys. Designed for use by NPC combat scripts.
    /// </summary>
    public static class SpectralCloneAmbush
    {
        private static readonly Dictionary<BaseNpcCombat, CloneManager> activeManagers =
            new Dictionary<BaseNpcCombat, CloneManager>();
        /// <summary>
        /// Spawns spectral clones that surround a target. One random clone performs
        /// the real strike. All clones self-destruct after a lifespan.
        /// </summary>
        /// <param name="owner">NPC performing the attack.</param>
        /// <param name="target">Target to ambush.</param>
        /// <param name="clonePrefabs">Array of possible clone prefabs.</param>
        /// <param name="cloneCount">Number of clones to spawn.</param>
        /// <param name="cloneLifespan">Lifetime in seconds for each clone.</param>
        /// <param name="spawnRadius">Radius around the target to spawn clones.</param>
        /// <param name="realCloneDamage">Damage dealt by the real clone.</param>
        /// <param name="onCloneDestroyed">Callback invoked whenever a clone is destroyed or expires.</param>
        /// <param name="onAllClonesGone">Callback invoked once all clones are gone.</param>
        /// <param name="preventConcurrent">If true, prevents multiple concurrent ambushes by the same owner.</param>
        public static void Perform(BaseNpcCombat owner, CombatTarget target,
            GameObject[] clonePrefabs, int cloneCount, float cloneLifespan,
            float spawnRadius = 1f, int realCloneDamage = 0,
            Action<GameObject> onCloneDestroyed = null,
            Action onAllClonesGone = null,
            bool preventConcurrent = false)
        {
            if (owner == null || target == null || clonePrefabs == null ||
                clonePrefabs.Length == 0 || cloneCount <= 0)
                return;

            if (preventConcurrent && activeManagers.ContainsKey(owner))
                return;

            owner.StartCoroutine(SpawnClones(owner, target, clonePrefabs,
                cloneCount, cloneLifespan, spawnRadius, realCloneDamage,
                onCloneDestroyed, onAllClonesGone, preventConcurrent));
        }

        private static IEnumerator SpawnClones(BaseNpcCombat owner, CombatTarget target,
            GameObject[] clonePrefabs, int cloneCount, float cloneLifespan,
            float spawnRadius, int realCloneDamage,
            Action<GameObject> onCloneDestroyed, Action onAllClonesGone,
            bool preventConcurrent)
        {
            var managerGO = new GameObject("SpectralCloneManager");
            var manager = managerGO.AddComponent<CloneManager>();
            manager.expectedCount = cloneCount;
            manager.onCloneDestroyed = onCloneDestroyed;
            manager.onAllClonesGone = onAllClonesGone;
            manager.owner = owner;
            manager.preventConcurrent = preventConcurrent;
            if (preventConcurrent)
                activeManagers[owner] = manager;

            int realIndex = UnityEngine.Random.Range(0, cloneCount);

            for (int i = 0; i < cloneCount; i++)
            {
                var prefab = clonePrefabs[UnityEngine.Random.Range(0, clonePrefabs.Length)];
                if (prefab == null)
                    continue;

                float angle = i * Mathf.PI * 2f / cloneCount;
                angle += UnityEngine.Random.Range(-0.1f, 0.1f);
                Vector2 spawnPos = (Vector2)target.transform.position +
                    new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spawnRadius;
                var clone = UnityEngine.Object.Instantiate(prefab, spawnPos, Quaternion.identity);
                var controller = clone.AddComponent<SpectralClone>();
                controller.manager = manager;
                controller.lifespan = cloneLifespan;
                controller.isReal = (i == realIndex);
                controller.owner = owner;
                controller.target = target;
                controller.realDamage = realCloneDamage;

                var npcCombatant = clone.GetComponent<NpcCombatant>();
                if (npcCombatant != null)
                {
                    var profileField = typeof(NpcCombatant)
                        .GetField("profile", BindingFlags.NonPublic | BindingFlags.Instance);
                    var profile = npcCombatant.Profile;
                    if (profile != null)
                    {
                        profile = UnityEngine.Object.Instantiate(profile);
                        profileField?.SetValue(npcCombatant, profile);
                        controller.profile = profile;
                        controller.originalAggroRange = profile.AggroRange;
                        profile.AggroRange = float.MaxValue;
                    }
                }
            }

            yield break;
        }

        private class CloneManager : MonoBehaviour
        {
            public int expectedCount;
            public Action<GameObject> onCloneDestroyed;
            public Action onAllClonesGone;
            public BaseNpcCombat owner;
            public bool preventConcurrent;
            private int goneCount;

            public void CloneGone(GameObject clone)
            {
                goneCount++;
                onCloneDestroyed?.Invoke(clone);
                if (goneCount >= expectedCount)
                {
                    onAllClonesGone?.Invoke();
                    if (preventConcurrent && owner != null)
                    {
                        activeManagers.Remove(owner);
                    }
                    Destroy(gameObject);
                }
            }
        }

        private class SpectralClone : MonoBehaviour
        {
            public float lifespan;
            public bool isReal;
            public BaseNpcCombat owner;
            public CombatTarget target;
            public int realDamage;
            public CloneManager manager;
            public NpcCombatProfile profile;
            public float originalAggroRange;

            private void Start()
            {
                if (isReal && target != null && target.IsAlive)
                {
                    target.ApplyDamage(realDamage, DamageType.Magic, owner);
                }
                StartCoroutine(LifeRoutine());
            }

            private IEnumerator LifeRoutine()
            {
                yield return new WaitForSeconds(lifespan);
                Destroy(gameObject);
            }

            private void OnDestroy()
            {
                if (profile != null)
                {
                    profile.AggroRange = originalAggroRange;
                }
                manager?.CloneGone(gameObject);
            }
        }
    }
}

