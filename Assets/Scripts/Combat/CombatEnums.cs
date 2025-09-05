using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Types of damage that can be dealt in combat.
    /// </summary>
    public enum DamageType
    {
        Melee,
        Ranged,
        Magic,
        Burn
    }

    /// <summary>
    /// Melee combat styles affecting effective levels and XP distribution.
    /// </summary>
    public enum CombatStyle
    {
        Accurate,
        Aggressive,
        Defensive,
        Controlled
    }
}
