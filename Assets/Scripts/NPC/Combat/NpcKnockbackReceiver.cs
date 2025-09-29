using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Handles directional knockback impulses for NPCs by forwarding displacement
    /// requests to the <see cref="NpcWanderer"/> movement controller.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcWanderer))]
    public sealed class NpcKnockbackReceiver : MonoBehaviour
    {
        [Header("Knockback")]
        [SerializeField, Tooltip("Global toggle so designers can disable knockback without removing the component.")]
        private bool enableKnockback = true;
        [SerializeField, Tooltip("Base displacement applied when a hit lands. This is expressed in world units.")]
        private float baseDistance = 0.75f;
        [SerializeField, Tooltip("Time in seconds for the knockback interpolation to complete.")]
        private float knockbackDuration = 0.3f;
        [SerializeField, Tooltip("When enabled the knockback distance scales with incoming damage to emphasise heavy hits.")]
        private bool scaleWithDamage = true;
        [SerializeField, Tooltip("Additional distance added for each point of damage when scaling is enabled.")]
        private float distancePerDamagePoint = 0.02f;
        [SerializeField, Tooltip("Maximum displacement allowed after scaling so extreme hits do not launch NPCs excessively far.")]
        private float maxScaledDistance = 2.5f;
        [SerializeField, Tooltip("Clamp the resolved knockback position to the wanderer's configured movement bounds.")]
        private bool clampToMovementBounds = true;
        [SerializeField, Tooltip("Curve used to ease the knockback interpolation over time.")]
        private AnimationCurve knockbackCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private NpcWanderer wanderer;
        private NpcCombatant combatant;
        private Rigidbody2D body;

        private void Awake()
        {
            wanderer = GetComponent<NpcWanderer>();
            combatant = GetComponent<NpcCombatant>();
            body = GetComponent<Rigidbody2D>();
        }

        /// <summary>
        /// Applies a knockback impulse away from the provided <paramref name="source"/> transform.
        /// Damage is used to scale the displacement when <see cref="scaleWithDamage"/> is enabled.
        /// </summary>
        /// <param name="source">Transform representing the origin of the hit.</param>
        /// <param name="damage">The final damage amount applied by the attack.</param>
        public void ApplyKnockbackFrom(Transform source, int damage)
        {
            if (!enableKnockback || source == null || wanderer == null)
                return;

            if (combatant != null && !combatant.IsAlive)
                return;

            if (wanderer.IsFrozen)
                return;

            Vector2 currentPosition = body != null ? body.position : (Vector2)transform.position;
            Vector2 direction = currentPosition - (Vector2)source.position;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                // Default to pushing right when the attacker is exactly overlapping the NPC.
                direction = Vector2.right;
            }

            direction.Normalize();
            float distance = Mathf.Max(0f, baseDistance);
            if (scaleWithDamage && damage > 0)
            {
                float scaled = baseDistance + distancePerDamagePoint * damage;
                distance = Mathf.Clamp(scaled, 0f, maxScaledDistance);
            }

            float duration = Mathf.Max(0.01f, knockbackDuration);
            AnimationCurve curveToUse = knockbackCurve != null && knockbackCurve.length > 0
                ? knockbackCurve
                : AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

            Vector2 desiredEnd = currentPosition + direction * distance;
            if (clampToMovementBounds)
                desiredEnd = wanderer.ClampToMovementBounds(desiredEnd);

            wanderer.ApplyKnockback(direction, Vector2.Distance(currentPosition, desiredEnd), duration, clampToMovementBounds, curveToUse);
        }

        /// <summary>
        /// Immediately cancels any active knockback interpolation.
        /// </summary>
        public void CancelKnockback()
        {
            if (wanderer == null)
                return;

            wanderer.CancelKnockback();
        }
    }
}
