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
        /// <param name="impactRadius">Radius for impact damage and burning ground.</param>
        /// <param name="dropHeight">Height above the ground to spawn meteors.</param>
        /// <param name="meteorSpeed">Speed at which meteors fall.</param>
        public static void Perform(BaseNpcCombat owner, CombatTarget target,
            int meteorCount, float spreadRadius, int impactDamage,
            int burnDamagePerTick, float burnDuration,
            GameObject meteorPrefab, GameObject burnPrefab,
            float impactRadius = 1.5f, float dropHeight = 8f, float meteorSpeed = 8f)
        {
            if (owner == null || target == null || meteorCount <= 0)
                return;

            owner.StartCoroutine(SpawnMeteors(owner, target, meteorCount, spreadRadius,
                impactDamage, burnDamagePerTick, burnDuration, meteorPrefab, burnPrefab,
                impactRadius, dropHeight, meteorSpeed));
        }

        private static IEnumerator SpawnMeteors(BaseNpcCombat owner, CombatTarget target,
            int meteorCount, float spreadRadius, int impactDamage,
            int burnDamagePerTick, float burnDuration,
            GameObject meteorPrefab, GameObject burnPrefab, float impactRadius,
            float dropHeight, float meteorSpeed)
        {
            for (int i = 0; i < meteorCount; i++)
            {
                Vector2 groundPos = (Vector2)target.transform.position + Random.insideUnitCircle * spreadRadius;
                Vector2 spawnPos = groundPos + Vector2.up * dropHeight;
                GameObject meteor = null;
                if (meteorPrefab != null)
                    meteor = Object.Instantiate(meteorPrefab, spawnPos, Quaternion.identity);

                owner.StartCoroutine(MeteorFall(meteor, groundPos, impactDamage,
                    burnDamagePerTick, burnDuration, burnPrefab, impactRadius,
                    meteorSpeed, owner));

                yield return new WaitForSeconds(0.1f);
            }
        }

        private static IEnumerator MeteorFall(GameObject meteor, Vector2 groundPos,
            int impactDamage, int burnDamagePerTick, float burnDuration,
            GameObject burnPrefab, float impactRadius, float meteorSpeed,
            BaseNpcCombat owner)
        {
            while (meteor != null && Vector2.Distance(meteor.transform.position, groundPos) > 0.05f)
            {
                meteor.transform.position = Vector2.MoveTowards(meteor.transform.position,
                    groundPos, meteorSpeed * Time.deltaTime);
                yield return null;
            }

            if (meteor != null)
                Object.Destroy(meteor);

            ApplyAreaDamage(groundPos, impactRadius, impactDamage, owner);

            if (burnPrefab != null && burnDuration > 0f && burnDamagePerTick > 0)
            {
                var burnObj = Object.Instantiate(burnPrefab, groundPos, Quaternion.identity);
                var burn = burnObj.AddComponent<BurningGround>();
                burn.damagePerTick = burnDamagePerTick;
                burn.duration = burnDuration;
                burn.radius = impactRadius;
            }
        }

        private static void ApplyAreaDamage(Vector2 center, float radius, int damage,
            BaseNpcCombat owner)
        {
            var hits = Physics2D.OverlapCircleAll(center, radius);
            foreach (var h in hits)
            {
                var tgt = h.GetComponent<CombatTarget>();
                if (tgt != null)
                    tgt.ApplyDamage(damage, DamageType.Magic, owner);
            }
        }

        private class BurningGround : MonoBehaviour
        {
            public int damagePerTick;
            public float duration;
            public float radius;

            private void Start()
            {
                StartCoroutine(BurnRoutine());
            }

            private IEnumerator BurnRoutine()
            {
                float elapsed = 0f;
                var wait = new WaitForSeconds(CombatMath.TICK_SECONDS);
                while (elapsed < duration)
                {
                    var hits = Physics2D.OverlapCircleAll(transform.position, radius);
                    foreach (var h in hits)
                    {
                        var tgt = h.GetComponent<CombatTarget>();
                        if (tgt != null)
                            tgt.ApplyDamage(damagePerTick, DamageType.Magic, this);
                    }
                    elapsed += CombatMath.TICK_SECONDS;
                    yield return wait;
                }

                Object.Destroy(gameObject);
            }
        }
    }
}
