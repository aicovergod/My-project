using System.Collections.Generic;
using UnityEngine;
using Combat;

namespace NPC
{
    /// <summary>
    /// Handles meteor movement toward a target position and applies impact effects.
    /// </summary>
    public class MeteorProjectile : MonoBehaviour
    {
        public Vector2 target;
        public int impactDamage;
        public int burnDamagePerTick;
        public float burnDuration;
        public GameObject burnPrefab;
        public float speed = 8f;
        public BaseNpcCombat owner;
        [SerializeField] private float selfDestructTime = 10f;
        [SerializeField]
        [Tooltip("Radius around the impact point used to apply the meteor's damage.")]
        private float impactRadius = 1.5f;
        private float timer;

        private void Awake()
        {
            timer = selfDestructTime;
        }

        private void Update()
        {
            transform.position = Vector2.MoveTowards(transform.position, target, speed * Time.deltaTime);

            if (Vector2.Distance(transform.position, target) <= 0.05f)
            {
                Impact();
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
                Destroy(gameObject);
        }

        private void Impact()
        {
            // Apply the meteor's hit damage before spawning persistent ground flames so initial impact still hurts targets.
            if (impactDamage > 0)
            {
                var hits = Physics2D.OverlapCircleAll(target, impactRadius);
                if (hits.Length > 0)
                {
                    var processedTargets = new HashSet<CombatTarget>();
                    var source = (object)owner ?? this;
                    var ownerTarget = owner != null ? owner.GetComponent<CombatTarget>() : null;

                    foreach (var hit in hits)
                    {
                        var combatTarget = hit.GetComponent<CombatTarget>() ?? hit.GetComponentInParent<CombatTarget>();
                        if (combatTarget == null || processedTargets.Contains(combatTarget) || !combatTarget.IsAlive)
                            continue;
                        if (ownerTarget != null && combatTarget == ownerTarget)
                            continue;

                        combatTarget.ApplyDamage(impactDamage, DamageType.Magic, SpellElement.Fire, source);
                        processedTargets.Add(combatTarget);
                    }
                }
            }

            SpawnBurningGround();
            Destroy(gameObject);
        }

        private void SpawnBurningGround()
        {
            if (burnPrefab != null && burnDuration > 0f && burnDamagePerTick > 0)
            {
                var burnObj = Instantiate(burnPrefab, target, Quaternion.identity);
                var flame = burnObj.GetComponent<GroundFlame>();
                if (flame != null)
                {
                    flame.damagePerTick = burnDamagePerTick;
                    flame.duration = burnDuration;
                }
            }
        }
    }
}
