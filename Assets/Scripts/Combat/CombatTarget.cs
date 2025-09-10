using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Interface for objects that can participate in combat.
    /// </summary>
    public interface CombatTarget
    {
        Transform transform { get; }
        bool IsAlive { get; }
        DamageType PreferredDefenceType { get; }
        int CurrentHP { get; }
        int MaxHP { get; }
        void ApplyDamage(int amount, DamageType type, SpellElement element, object source);
    }
}
