using UnityEngine;
using Status.Poison;

namespace Combat
{
    /// <summary>
    /// Applies poison to a target when a hit is confirmed.
    /// </summary>
    public class OnHitPoisonApplier : MonoBehaviour
    {
        [Tooltip("Poison configuration to apply on hit.")]
        public PoisonConfig poison;

        [Tooltip("Chance that the poison is applied on a successful hit.")]
        [Range(0f, 1f)] public float applyChance = 0.25f;

        [Tooltip("Only apply poison if the hit dealt damage.")]
        public bool requiresDamage = true;

        /// <summary>
        /// Attempt to apply poison to the specified target.
        /// </summary>
        /// <param name="target">Target game object.</param>
        /// <param name="didDealDamage">Whether the hit dealt damage.</param>
        public void TryApply(GameObject target, bool didDealDamage)
        {
            if (poison == null || target == null)
                return;
            if (requiresDamage && !didDealDamage)
                return;
            if (Random.value > applyChance)
                return;
            var controller = target.GetComponent<PoisonController>();
            if (controller != null && !controller.IsImmune)
                controller.ApplyPoison(poison);
        }
    }
}
