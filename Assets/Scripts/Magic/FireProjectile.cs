using UnityEngine;
using Combat;

namespace Magic
{
    /// <summary>
    /// Simple projectile that travels toward a target and applies damage on impact.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FireProjectile : MonoBehaviour
    {
        public CombatTarget target;
        public float speed = 8f;
        public int damage;
        public int maxHit;
        public GameObject hitEffectPrefab;
        public float hitFadeTime = 0.5f;
        public Sprite projectileSprite;
        public CombatController owner;
        public CombatStyle style;
        public DamageType damageType = DamageType.Magic;
        [SerializeField] private float selfDestructTime = 10f;
        private float timer;

        private void Awake()
        {
            timer = selfDestructTime;
            var sr = GetComponent<SpriteRenderer>();
            if (projectileSprite != null)
                sr.sprite = projectileSprite;
        }

        private void Update()
        {
            if (target == null || !target.IsAlive)
            {
                Destroy(gameObject);
                return;
            }

            Vector2 dir = (Vector2)(target.transform.position - transform.position);
            transform.up = dir;

            transform.position = Vector2.MoveTowards(transform.position,
                target.transform.position, speed * Time.deltaTime);

            if (Vector2.Distance(transform.position, target.transform.position) <= 0.05f)
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
            if (hitEffectPrefab != null)
            {
                var hitObj = Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
                var effect = hitObj.GetComponent<HitEffect>();
                if (effect != null)
                    effect.Initialize(hitFadeTime);
            }

            owner?.ApplySpellDamage(target, damage);
            Destroy(gameObject);
        }
    }
}
