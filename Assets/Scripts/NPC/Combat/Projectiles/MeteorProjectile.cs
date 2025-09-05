using System.Collections;
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
        public float impactRadius = 1.5f;
        public float speed = 8f;
        public BaseNpcCombat owner;
        [SerializeField] private float selfDestructTime = 10f;
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
            ApplyAreaDamage();
            SpawnBurningGround();
            Destroy(gameObject);
        }

        private void ApplyAreaDamage()
        {
            var hits = Physics2D.OverlapCircleAll(target, impactRadius);
            foreach (var h in hits)
            {
                var tgt = h.GetComponent<CombatTarget>();
                if (tgt != null)
                    tgt.ApplyDamage(impactDamage, DamageType.Magic, owner);
            }
        }

        private void SpawnBurningGround()
        {
            if (burnPrefab != null && burnDuration > 0f && burnDamagePerTick > 0)
            {
                var burnObj = Instantiate(burnPrefab, target, Quaternion.identity);
                var burn = burnObj.AddComponent<BurningGround>();
                burn.damagePerTick = burnDamagePerTick;
                burn.duration = burnDuration;
                burn.radius = impactRadius;
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
                            tgt.ApplyDamage(damagePerTick, DamageType.Burn, this);
                    }
                    elapsed += CombatMath.TICK_SECONDS;
                    yield return wait;
                }

                Destroy(gameObject);
            }
        }
    }
}
