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
