using EquipmentSystem;
using Skills;

namespace Combat
{
    /// <summary>
    /// Snapshot of a combatant's relevant combat statistics used during calculations.
    /// </summary>
    public sealed class CombatantStats
    {
        public int AttackLevel;
        public int StrengthLevel;
        public int RangedLevel;
        public int DefenceLevel;
        public int MagicLevel;
        public EquipmentAggregator.CombinedStats Equip;
        public CombatStyle Style;
        public DamageType DamageType;

        /// <summary>
        /// Build stats for the player using the provided managers.
        /// </summary>
        public static CombatantStats ForPlayer(SkillManager skills, EquipmentAggregator equip, CombatStyle style, DamageType type)
        {
            var combinedStats = equip != null ? equip.GetCombinedStats() : default;

            if (type == DamageType.Ranged)
            {
                // When the player is using ranged weapons the selected combat style
                // grants hidden equipment-style bonuses that should not appear in the
                // equipment window. Adjust the aggregated stats here so every combat
                // calculation automatically respects the invisible boosts.
                if (style == CombatStyle.Accurate)
                    combinedStats.rangeStrength += 3;
                else if (style == CombatStyle.Longrange)
                {
                    combinedStats.meleeDef += 3;
                    combinedStats.rangeDef += 3;
                    combinedStats.magicDef += 3;
                }
            }

            return new CombatantStats
            {
                AttackLevel = skills != null ? skills.GetLevel(SkillType.Attack) : 1,
                StrengthLevel = skills != null ? skills.GetLevel(SkillType.Strength) : 1,
                DefenceLevel = skills != null ? skills.GetLevel(SkillType.Defence) : 1,
                RangedLevel = skills != null ? skills.GetLevel(SkillType.Ranged) : 1,
                MagicLevel = skills != null ? skills.GetLevel(SkillType.Magic) : 1,
                Equip = combinedStats,
                Style = style,
                DamageType = type
            };
        }

        /// <summary>
        /// Build stats from an <see cref="NpcCombatProfile"/>.
        /// </summary>
        public static CombatantStats ForNpc(NpcCombatProfile profile)
        {
            return new CombatantStats
            {
                AttackLevel = profile != null ? profile.AttackLevel : 1,
                StrengthLevel = profile != null ? profile.StrengthLevel : 1,
                DefenceLevel = profile != null ? profile.DefenceLevel : 1,
                RangedLevel = profile != null ? profile.RangedLevel : 1,
                MagicLevel = profile != null ? profile.MagicLevel : 1,
                Equip = new EquipmentAggregator.CombinedStats
                {
                    attack = 0,
                    strength = 0,
                    range = 0,
                    rangeStrength = 0,
                    magic = 0,
                    meleeDef = profile != null ? profile.MeleeDefence : 0,
                    rangeDef = profile != null ? profile.RangeDefence : 0,
                    magicDef = profile != null ? profile.MagicDefence : 0,
                    attackSpeedTicks = profile != null ? profile.AttackSpeedTicks : 4
                },
                Style = profile != null ? profile.Style : CombatStyle.Accurate,
                DamageType = profile != null ? profile.AttackType : DamageType.Melee
            };
        }
    }
}
