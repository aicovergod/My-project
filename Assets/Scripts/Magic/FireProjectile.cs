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
        [Tooltip("Projectile travel speed in world units per second.")]
        public float speed = 8f;

        [Tooltip("Optional impact effect spawned when the projectile collides with its target.")]
        public GameObject hitEffectPrefab;

        [Tooltip("Time the spawned hit effect remains active before fading.")]
        public float hitFadeTime = 0.5f;

        [Tooltip("Sprite displayed for the projectile while travelling.")]
        public Sprite projectileSprite;

        [SerializeField, Tooltip("Failsafe lifetime so orphaned projectiles self-destruct.")]
        private float selfDestructTime = 10f;

        private CombatTarget target;
        private CombatController owner;
        private SpellImpactContext impactContext;
        private bool hasImpactContext;
        private float timer;

        /// <summary>
        /// Configures the projectile to travel toward the supplied target using the provided impact
        /// context. The context persists until impact so damage and status effect resolution matches
        /// instant-cast spells.
        /// </summary>
        public void Initialise(CombatController ownerController, CombatTarget combatTarget, SpellImpactContext context)
        {
            owner = ownerController;
            target = combatTarget;
            impactContext = context;
            hasImpactContext = true;
        }

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
            if (dir.sqrMagnitude > Mathf.Epsilon)
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

            if (hasImpactContext)
                owner?.ApplySpellDamage(target, impactContext);
            else
                Debug.LogWarning("FireProjectile impacted without a valid impact context; no damage applied.", this);

            Destroy(gameObject);
        }
    }
}
