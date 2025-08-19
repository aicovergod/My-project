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
        public int DefenceLevel;
        public EquipmentAggregator.CombinedStats Equip;
        public CombatStyle Style;
        public DamageType DamageType;

        /// <summary>
        /// Build stats for the player using the provided managers.
        /// </summary>
        public static CombatantStats ForPlayer(SkillManager skills, EquipmentAggregator equip, CombatStyle style, DamageType type)
        {
            return new CombatantStats
            {
                AttackLevel = skills != null ? skills.GetLevel(SkillType.Attack) : 1,
                StrengthLevel = skills != null ? skills.GetLevel(SkillType.Strength) : 1,
                DefenceLevel = skills != null ? skills.GetLevel(SkillType.Defence) : 1,
                Equip = equip != null ? equip.GetCombinedStats() : default,
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
                Equip = new EquipmentAggregator.CombinedStats
                {
                    attack = 0,
                    strength = 0,
                    range = 0,
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
