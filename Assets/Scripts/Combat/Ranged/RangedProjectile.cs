using System.Collections;
using UnityEngine;

namespace Combat.Ranged
{
    /// <summary>
    /// Handles projectile flight for ranged attacks. The behaviour is intentionally light weight so prefab
    /// authors can layer additional visual components on top (trail renderers, particles, etc.).
    /// </summary>
    [DisallowMultipleComponent]
    public class RangedProjectile : MonoBehaviour
    {
        [Tooltip("Seconds before the projectile self-destructs if it cannot find a target.")]
        [SerializeField] private float maxLifetime = 10f;

        private RangedCombatController owner;
        private CombatTarget target;
        private RangedAttackContext context;
        private float speed;
        private Coroutine travelRoutine;

        /// <summary>
        /// Initialises the projectile for travel towards <paramref name="target"/>.
        /// </summary>
        public void Initialise(RangedCombatController controller, CombatTarget target, RangedAttackContext ctx, float projectileSpeed)
        {
            owner = controller;
            this.target = target;
            context = ctx;
            speed = Mathf.Max(0.1f, projectileSpeed);

            if (travelRoutine != null)
                StopCoroutine(travelRoutine);
            travelRoutine = StartCoroutine(TravelRoutine());
        }

        private IEnumerator TravelRoutine()
        {
            float lifetime = 0f;
            while (lifetime < maxLifetime)
            {
                Vector3 destination = ResolveDestination();
                float step = speed * Time.deltaTime;

                // Align the projectile so the sprite always faces the direction of travel, matching
                // the behaviour used by spell projectiles. This keeps arrow sprites (authored to
                // point upward) visually pointing along their flight path regardless of target
                // movement.
                Vector3 toDestination = destination - transform.position;
                if (toDestination.sqrMagnitude > 0.0001f)
                    transform.up = toDestination;

                Vector3 newPosition = Vector3.MoveTowards(transform.position, destination, step);
                bool reachedDestination = newPosition == destination || speed <= 0.001f;

                transform.position = newPosition;
                context.targetPosition = destination;
                lifetime += Time.deltaTime;
                yield return null;

                if (reachedDestination)
                    break;
            }

            travelRoutine = null;
            owner?.HandleProjectileImpact(context, this);
        }

        private Vector3 ResolveDestination()
        {
            if (target != null)
            {
                context.targetPosition = target.transform.position;
                return target.transform.position;
            }

            return context.targetPosition;
        }

        private void OnDisable()
        {
            if (travelRoutine != null)
            {
                StopCoroutine(travelRoutine);
                travelRoutine = null;
            }
        }
    }
}
