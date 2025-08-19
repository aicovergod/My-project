using System;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// Combat related statistics for equippable items.
    /// </summary>
    [Serializable]
    public struct ItemCombatStats
    {
        public int Attack;
        public int Strength;
        public int Range;
        public int Magic;
        public int MeleeDefence;
        public int RangeDefence;
        public int MagicDefence;
        /// <summary>
        /// Attack speed expressed in OSRS ticks. Each tick is 0.6 seconds.
        /// </summary>
        public int AttackSpeedTicks;

        /// <summary>
        /// Returns a default set of stats with a 4 tick attack speed.
        /// </summary>
        public static ItemCombatStats Default => new ItemCombatStats
        {
            Attack = 0,
            Strength = 0,
            Range = 0,
            Magic = 0,
            MeleeDefence = 0,
            RangeDefence = 0,
            MagicDefence = 0,
            AttackSpeedTicks = 4
        };
    }
}
