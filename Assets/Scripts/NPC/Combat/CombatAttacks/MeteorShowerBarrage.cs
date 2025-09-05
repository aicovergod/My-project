using System.Collections;
using UnityEngine;
using Combat;

namespace NPC
{
    /// <summary>
    /// Utility to bombard a target area with meteors dealing immediate
    /// area damage and leaving behind burning ground that damages over time.
    /// Designed for use by NPC combat scripts via timed or triggered calls.
    /// </summary>
    public static class MeteorShowerBarrage
    {
        /// <summary>
        /// Performs the meteor shower barrage at the target's position.
        /// NPC combat scripts should call this method when the attack
        /// is triggered or on a timed interval.
        /// </summary>
        /// <param name="owner">NPC performing the attack.</param>
        /// <param name="target">Target to focus the barrage on.</param>
        /// <param name="meteorCount">Number of meteors to spawn.</param>
        /// <param name="spreadRadius">Random radius around the target for impacts.</param>
        /// <param name="impactDamage">Damage dealt on impact.</param>
        /// <param name="burnDamagePerTick">Damage dealt per tick by the burning ground.</param>
        /// <param name="burnDuration">Duration of the burning ground.</param>
        /// <param name="meteorPrefab">Prefab used for meteor projectiles.</param>
        /// <param name="burnPrefab">Prefab used for the burning ground effect.</param>
        /// <param name="dropHeight">Height above the ground to spawn meteors.</param>
        /// <param name="meteorSpeed">Speed at which meteors fall.</param>
        public static void Perform(BaseNpcCombat owner, CombatTarget target,
            int meteorCount, float spreadRadius, int impactDamage,
            int burnDamagePerTick, float burnDuration,
            GameObject meteorPrefab, GameObject burnPrefab,
            float dropHeight = 8f, float meteorSpeed = 8f)
        {
            if (owner == null || target == null || meteorCount <= 0)
                return;

            owner.StartCoroutine(SpawnMeteors(owner, target, meteorCount, spreadRadius,
                impactDamage, burnDamagePerTick, burnDuration, meteorPrefab, burnPrefab,
                dropHeight, meteorSpeed));
        }

        private static IEnumerator SpawnMeteors(BaseNpcCombat owner, CombatTarget target,
            int meteorCount, float spreadRadius, int impactDamage,
            int burnDamagePerTick, float burnDuration,
            GameObject meteorPrefab, GameObject burnPrefab,
            float dropHeight, float meteorSpeed)
        {
            for (int i = 0; i < meteorCount; i++)
            {
                Vector2 groundPos = (Vector2)target.transform.position + Random.insideUnitCircle * spreadRadius;
                Vector2 spawnPos = groundPos + Vector2.up * dropHeight;

                if (meteorPrefab != null)
                {
                    var meteor = Object.Instantiate(meteorPrefab, spawnPos, Quaternion.identity);
                    var proj = meteor.AddComponent<MeteorProjectile>();
                    proj.target = groundPos;
                    proj.impactDamage = impactDamage;
                    proj.burnDamagePerTick = burnDamagePerTick;
                    proj.burnDuration = burnDuration;
                    proj.burnPrefab = burnPrefab;
                    proj.speed = meteorSpeed;
                    proj.owner = owner;
                }

                yield return new WaitForSeconds(0.1f);
            }
        }
    }
}
