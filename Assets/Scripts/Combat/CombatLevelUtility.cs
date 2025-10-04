using UnityEngine;
using Skills;

namespace Combat
{
    /// <summary>
    /// Provides helper utilities for determining the player's combat level using
    /// the Old School RuneScape formula. The calculation mirrors the classic
    /// approach of taking a base contribution from defensive stats and adding the
    /// strongest offensive style contribution.
    /// </summary>
    public static class CombatLevelUtility
    {
        /// <summary>Weight applied to the combined Defence and Hitpoints levels.</summary>
        internal const float BaseContributionWeight = 0.25f;

        /// <summary>Weight applied to the highest offensive style contribution.</summary>
        internal const float OffensiveContributionWeight = 0.325f;

        /// <summary>Multiplier used for the magic style before the offensive weight is applied.</summary>
        internal const float MagicStyleMultiplier = 2f;

        /// <summary>Multiplier used for the ranged style before the offensive weight is applied.</summary>
        internal const float RangedStyleMultiplier = 2f;

        /// <summary>
        /// Lightweight breakdown data produced alongside the combat level
        /// calculation. Keeping this structure available means future unit tests
        /// can assert exact contribution values across melee, magic, and ranged
        /// styles without duplicating the combat formula.
        /// </summary>
        public readonly struct CombatLevelBreakdown
        {
            /// <summary>Raw Defence level pulled from the <see cref="SkillManager"/>.</summary>
            public readonly int DefenceLevel;

            /// <summary>Raw Hitpoints level pulled from the <see cref="SkillManager"/>.</summary>
            public readonly int HitpointsLevel;

            /// <summary>Raw Attack level pulled from the <see cref="SkillManager"/>.</summary>
            public readonly int AttackLevel;

            /// <summary>Raw Strength level pulled from the <see cref="SkillManager"/>.</summary>
            public readonly int StrengthLevel;

            /// <summary>Raw Magic level pulled from the <see cref="SkillManager"/>.</summary>
            public readonly int MagicLevel;

            /// <summary>Raw Ranged level pulled from the <see cref="SkillManager"/>.</summary>
            public readonly int RangedLevel;

            /// <summary>Contribution from defensive stats prior to flooring.</summary>
            public readonly float BaseContribution;

            /// <summary>Melee offensive contribution prior to flooring.</summary>
            public readonly float MeleeContribution;

            /// <summary>Magic offensive contribution prior to flooring.</summary>
            public readonly float MagicContribution;

            /// <summary>Ranged offensive contribution prior to flooring.</summary>
            public readonly float RangedContribution;

            /// <summary>Offensive contribution selected for the final total.</summary>
            public readonly float SelectedOffensiveContribution;

            /// <summary>Final combat level after clamping and flooring.</summary>
            public readonly int CombatLevel;

            internal CombatLevelBreakdown(
                int defenceLevel,
                int hitpointsLevel,
                int attackLevel,
                int strengthLevel,
                int magicLevel,
                int rangedLevel,
                float baseContribution,
                float meleeContribution,
                float magicContribution,
                float rangedContribution,
                float selectedOffensiveContribution,
                int combatLevel)
            {
                DefenceLevel = defenceLevel;
                HitpointsLevel = hitpointsLevel;
                AttackLevel = attackLevel;
                StrengthLevel = strengthLevel;
                MagicLevel = magicLevel;
                RangedLevel = rangedLevel;
                BaseContribution = baseContribution;
                MeleeContribution = meleeContribution;
                MagicContribution = magicContribution;
                RangedContribution = rangedContribution;
                SelectedOffensiveContribution = selectedOffensiveContribution;
                CombatLevel = combatLevel;
            }
        }

        /// <summary>
        /// Calculate the combat level for the supplied skills manager. The method
        /// gracefully handles null managers by returning the minimum combat level
        /// of 1 so combat systems relying on this value remain stable.
        /// </summary>
        /// <param name="skills">Skill manager reference used to read current combat levels.</param>
        /// <returns>Combat level rounded down to the nearest integer with a minimum of 1.</returns>
        public static int CalculateCombatLevel(SkillManager skills)
        {
            return CalculateCombatLevelInternal(skills).CombatLevel;
        }

        /// <summary>
        /// Internal helper that performs the combat level calculation and exposes
        /// the intermediate values for validation. Marked internal so edit-mode
        /// tests can assert breakdown values without expanding the public API.
        /// </summary>
        /// <param name="skills">Skill manager instance supplying combat related levels.</param>
        /// <returns>Struct containing the raw levels, contributions and final result.</returns>
        internal static CombatLevelBreakdown CalculateCombatLevelInternal(SkillManager skills)
        {
            if (skills == null)
                return new CombatLevelBreakdown(0, 0, 0, 0, 0, 0, 0f, 0f, 0f, 0f, 0f, 1);

            int defence = Mathf.Max(0, skills.GetLevel(SkillType.Defence));
            int hitpoints = Mathf.Max(0, skills.GetLevel(SkillType.Hitpoints));
            int attack = Mathf.Max(0, skills.GetLevel(SkillType.Attack));
            int strength = Mathf.Max(0, skills.GetLevel(SkillType.Strength));
            int magic = Mathf.Max(0, skills.GetLevel(SkillType.Magic));
            int ranged = Mathf.Max(0, skills.GetLevel(SkillType.Ranged));

            float baseContribution = (defence + hitpoints) * BaseContributionWeight;
            float meleeContribution = (attack + strength) * OffensiveContributionWeight;
            float magicContribution = magic * MagicStyleMultiplier * OffensiveContributionWeight;
            float rangedContribution = ranged * RangedStyleMultiplier * OffensiveContributionWeight;

            float selectedOffensiveContribution = Mathf.Max(meleeContribution, Mathf.Max(magicContribution, rangedContribution));
            float combatLevelValue = baseContribution + selectedOffensiveContribution;
            int combatLevel = Mathf.Max(1, Mathf.FloorToInt(combatLevelValue));

            return new CombatLevelBreakdown(
                defence,
                hitpoints,
                attack,
                strength,
                magic,
                ranged,
                baseContribution,
                meleeContribution,
                magicContribution,
                rangedContribution,
                selectedOffensiveContribution,
                combatLevel);
        }
    }
}
