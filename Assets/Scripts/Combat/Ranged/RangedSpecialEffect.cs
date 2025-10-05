using UnityEngine;

namespace Combat.Ranged
{
    /// <summary>
    /// Context object forwarded to ranged special effect handlers.
    /// </summary>
    public struct RangedAttackContext
    {
        public CombatController combatController;
        /// <summary>
        /// Owning ranged combat controller responsible for preparing and releasing the shot.
        /// Enables downstream effects to query helper methods or state during follow-up rolls.
        /// </summary>
        public RangedCombatController rangedController;
        public CombatantStats attacker;
        public CombatTarget target;
        /// <summary>
        /// Unique identifier for the weapon used when the shot was prepared. This is stored
        /// alongside the direct reference so asynchronous stages can re-resolve the correct
        /// weapon definition if the player swaps equipment before the attack resolves.
        /// </summary>
        public string weaponId;
        public RangedWeaponData weapon;
        public AmmunitionData ammunition;
        public CombatController.DamageResult damageResult;
        public Vector3 origin;
        public Vector3 targetPosition;
        public bool ammoConsumed;
        /// <summary>
        /// Final stacked accuracy multiplier applied when the primary shot was resolved.
        /// Splash effects reuse this value so their secondary rolls honour the same modifiers.
        /// </summary>
        public float finalAccuracyMultiplier;
        /// <summary>
        /// Final stacked damage multiplier applied when the primary shot was resolved.
        /// Secondary damage calculations scale from this cached value to remain deterministic.
        /// </summary>
        public float finalDamageMultiplier;

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
