using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Implements OSRS-style combat calculations for accuracy, defence and damage.
    /// </summary>
    public static class CombatMath
    {
        /// <summary>Seconds per OSRS tick.</summary>
        public const float TICK_SECONDS = 0.6f;

        /// <summary>Maximum distance allowed for melee combat.</summary>
        public const float MELEE_RANGE = 1.5f;

        /// <summary>
        /// Base constant used in effective level calculations. OSRS adds this
        /// value to a player's level to prevent very low level characters from
        /// having a maximum hit of zero even when a swing lands.
        /// </summary>
        private const int EFFECTIVE_LEVEL_BASE = 8;

        public static int GetEffectiveAttack(int level, CombatStyle style)
        {
            int bonus = style switch
            {
                CombatStyle.Accurate => 3,
                CombatStyle.Controlled => 1,
                _ => 0
            };
            // Include a base constant so that low level characters can still
            // inflict damage. Without it, effective level would be zero and all
            // successful swings would roll zero damage.
            return level + bonus + EFFECTIVE_LEVEL_BASE;
        }

        public static int GetEffectiveStrength(int level, CombatStyle style)
        {
            int bonus = style switch
            {
                CombatStyle.Aggressive => 3,
                CombatStyle.Controlled => 1,
                _ => 0
            };
            // Include the base constant so low level players are able to deal
            // damage. This mirrors the behaviour of OSRS and fixes cases where
            // max hit was stuck at 0 or 1 regardless of strength level.
            return level + bonus + EFFECTIVE_LEVEL_BASE;
        }

        /// <summary>
        /// Mirrors the OSRS ranged effective level calculation. Defensive styles grant +1 and rapid-style
        /// shots (represented by <see cref="CombatStyle.Aggressive"/>) provide no bonus. A constant 8 is
        /// applied to keep low level accuracy consistent with melee and magic. Accurate no longer adds a
        /// hidden +3 here so it can exclusively influence ranged strength rolls.
        /// </summary>
        public static int GetEffectiveRanged(int level, CombatStyle style)
        {
            int bonus = style switch
            {
                CombatStyle.Defensive => 1,
                CombatStyle.Controlled => 1,
                CombatStyle.Longrange => 1,
                _ => 0
            };
            return level + bonus + 8;
        }

        /// <summary>
        /// Effective strength when rolling ranged max hits. Accurate grants +3, defensive and controlled
        /// styles grant +1, while Longrange maintains its +1 damage bonus even though the style now focuses
        /// on defence within <see cref="GetEffectiveDefence"/>. A constant 8 is applied for parity with the
        /// wider OSRS formulas.
        /// </summary>
        public static int GetEffectiveRangedStrength(int level, CombatStyle style)
        {
            int bonus = style switch
            {
                CombatStyle.Accurate => 3,
                CombatStyle.Defensive => 1,
                CombatStyle.Controlled => 1,
                CombatStyle.Longrange => 1,
                _ => 0
            };
            return level + bonus + 8;
        }

        public static int GetEffectiveDefence(int level, CombatStyle style)
        {
            int bonus = style switch
            {
                CombatStyle.Defensive => 3,
                CombatStyle.Controlled => 1,
                CombatStyle.Longrange => 3,
                _ => 0
            };
            // Defence also incorporates the base constant for parity with OSRS
            // calculations.
            return level + bonus + EFFECTIVE_LEVEL_BASE;
        }

        public static int GetAttackRoll(int effectiveAttack, int attackBonus)
        {
            int bonus = Mathf.Max(0, attackBonus) + 64;
            return Mathf.FloorToInt(effectiveAttack * bonus);
        }

        public static int GetDefenceRoll(int effectiveDefence, int defenceBonus)
        {
            int bonus = Mathf.Max(0, defenceBonus) + 64;
            return Mathf.FloorToInt(effectiveDefence * bonus);
        }

        public static float ChanceToHit(int attackRoll, int defenceRoll)
        {
            float chance;
            if (attackRoll > defenceRoll)
                chance = 1f - (defenceRoll + 2f) / (2f * (attackRoll + 1f));
            else
                chance = attackRoll / (2f * (defenceRoll + 1f));
            return chance;
        }

        public static int GetMaxHit(int effectiveStrength, int strengthBonus)
        {
            int bonus = Mathf.Max(0, strengthBonus) + 64;
            int maxHit = Mathf.FloorToInt(0.5f + effectiveStrength * bonus / 640f);
            return maxHit < 0 ? 0 : maxHit;
        }

        /// <summary>
        /// Roll damage between 0 and maxHit inclusive.
        /// </summary>
        public static int RollDamage(int maxHit)
        {
            if (maxHit <= 0)
                return 0;
            return Random.Range(0, maxHit + 1);
        }
    }
}
