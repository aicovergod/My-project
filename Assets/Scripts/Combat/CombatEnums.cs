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
        Burn,
        Poison
    }

    /// <summary>
    /// Elemental affiliation of a magic spell.
    /// </summary>
    public enum SpellElement
    {
        Air,
        Water,
        Earth,
        Electric,
        Ice,
        Fire,
        None
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
