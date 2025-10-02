using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Designer-friendly toggle that lets specific colliders permit certain damage types to
    /// bypass line-of-sight checks. Useful for mining rocks or interactables that should block
    /// melee swings while still allowing spells or ranged projectiles to pass through.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatLineOfSightBypass : MonoBehaviour
    {
        [SerializeField, Tooltip("When enabled melee attacks treat this collider as transparent, allowing players and NPCs to strike through it. Ideal for rocks that should only be broken with ranged or magic damage.")]
        private bool allowMelee = false;

        [SerializeField, Tooltip("When enabled ranged attacks (arrows, thrown weapons) ignore this collider during line of sight checks. Use this to let ranged combat reach targets behind harvesting nodes without repositioning.")]
        private bool allowRanged = true;

        [SerializeField, Tooltip("When enabled magic spells can pass through this collider. Combine with disabled melee to recreate OSRS-style obstacles that force players to use spells.")]
        private bool allowMagic = true;

        /// <summary>
        /// Returns true when the supplied damage type is allowed to pass through this collider.
        /// </summary>
        public bool AllowsDamageType(DamageType type)
        {
            switch (type)
            {
                case DamageType.Melee:
                    return allowMelee;
                case DamageType.Ranged:
                    return allowRanged;
                case DamageType.Magic:
                    return allowMagic;
                default:
                    // Non-standard damage types (poison, dragonfire, etc.) do not interact with
                    // harvesting obstacles, so treat them as unblocked by default.
                    return true;
            }
        }
    }
}
