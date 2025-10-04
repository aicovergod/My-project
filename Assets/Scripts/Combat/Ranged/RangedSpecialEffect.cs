using UnityEngine;

namespace Combat.Ranged
{
    /// <summary>
    /// Context object forwarded to ranged special effect handlers.
    /// </summary>
    public struct RangedAttackContext
    {
        public CombatController combatController;
        public CombatantStats attacker;
        public CombatTarget target;
        public RangedWeaponData weapon;
        public AmmunitionData ammunition;
        public CombatController.DamageResult damageResult;
        public Vector3 origin;
        public Vector3 targetPosition;
        public bool ammoConsumed;

        /// <summary>
        /// True when the accuracy roll landed and damage should be considered.
        /// </summary>
        public bool Hit => damageResult.hit;
    }

    /// <summary>
    /// Base class for ranged special attacks or ammunition effects. Subclasses can hook into projectile
    /// fire/impact moments without the combat controller needing to know about bespoke behaviours.
    /// </summary>
    public abstract class RangedSpecialEffect : ScriptableObject
    {
        /// <summary>
        /// Invoked immediately before the projectile is spawned so effects can play draw animations or
        /// consume additional resources.
        /// </summary>
        public virtual void OnShotPrepared(RangedAttackContext context) { }

        /// <summary>
        /// Invoked when the projectile is released. Context contains the final accuracy and damage roll.
        /// </summary>
        public virtual void OnProjectileLaunched(RangedAttackContext context) { }

        /// <summary>
        /// Invoked when the projectile lands or is otherwise resolved. The combat controller will have
        /// already applied damage when <see cref="context.damageResult.hit"/> is true.
        /// </summary>
        public virtual void OnImpactResolved(RangedAttackContext context) { }
    }
}
