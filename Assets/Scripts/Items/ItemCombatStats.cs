using System;
using System.Collections.Generic;
using Combat;
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
        /// <summary>
        /// Ranged accuracy bonus used when rolling chance to hit.
        /// </summary>
        public int Range;
        /// <summary>
        /// Ranged strength bonus used when calculating maximum hit rolls.
        /// </summary>
        public int RangeStrength;
        public int Magic;
        public int MeleeDefence;
        public int RangeDefence;
        public int MagicDefence;
        /// <summary>
        /// Attack speed expressed in OSRS ticks. Each tick is 0.6 seconds.
        /// </summary>
        public int AttackSpeedTicks;

        [SerializeField]
        [Tooltip("Optional per-style overrides for the weapon's swing speed. Add entries here to diverge from the default Attack Speed Ticks value for specific combat styles.")]
        private List<StyleAttackSpeedOverride> styleAttackSpeedOverrides;

        /// <summary>
        /// Maps a combat style to a custom attack speed tick value.
        /// </summary>
        [Serializable]
        public struct StyleAttackSpeedOverride
        {
            [Tooltip("Combat style that should use the configured tick speed override.")]
            public CombatStyle Style;

            [Tooltip("Swing speed in OSRS ticks for the selected style. Values are clamped to at least one tick.")]
            public int AttackSpeedTicks;

            /// <summary>
            /// Clamp the configured tick rate to at least one.
            /// </summary>
            public void Clamp()
            {
                AttackSpeedTicks = Mathf.Max(1, AttackSpeedTicks);
            }
        }

        /// <summary>
        /// Resolve the effective attack speed in ticks for the provided combat style.
        /// </summary>
        /// <param name="style">Combat style currently being used.</param>
        /// <returns>Tick count clamped to at least one.</returns>
        public int GetAttackSpeedTicks(CombatStyle style)
        {
            int defaultTicks = Mathf.Max(1, AttackSpeedTicks);
            if (styleAttackSpeedOverrides == null || styleAttackSpeedOverrides.Count == 0)
                return defaultTicks;

            for (int i = 0; i < styleAttackSpeedOverrides.Count; i++)
            {
                var entry = styleAttackSpeedOverrides[i];
                if (entry.Style == style)
                    return Mathf.Max(1, entry.AttackSpeedTicks);
            }

            return defaultTicks;
        }

        /// <summary>
        /// Ensure all serialized attack speed values remain valid.
        /// </summary>
        public void ClampAttackSpeedValues()
        {
            AttackSpeedTicks = Mathf.Max(1, AttackSpeedTicks);
            if (styleAttackSpeedOverrides == null)
                return;

            for (int i = 0; i < styleAttackSpeedOverrides.Count; i++)
            {
                var entry = styleAttackSpeedOverrides[i];
                entry.Clamp();
                styleAttackSpeedOverrides[i] = entry;
            }
        }

        /// <summary>
        /// Returns a default set of stats with a 4 tick attack speed.
        /// </summary>
        public static ItemCombatStats Default => new ItemCombatStats
        {
            Attack = 0,
            Strength = 0,
            Range = 0,
            RangeStrength = 0,
            Magic = 0,
            MeleeDefence = 0,
            RangeDefence = 0,
            MagicDefence = 0,
            AttackSpeedTicks = 4
        };
    }
}
