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

        public const float MIN_HIT_CHANCE = 0.25f;

        /// <summary>Maximum distance allowed for melee combat.</summary>
        public const float MELEE_RANGE = 1.5f;

        public static int GetEffectiveAttack(int level, CombatStyle style)
        {
            int bonus = style switch
            {
                CombatStyle.Accurate => 3,
                CombatStyle.Controlled => 1,
                _ => 0
            };
            return level + bonus;
        }

        public static int GetEffectiveStrength(int level, CombatStyle style)
        {
            int bonus = style switch
            {
                CombatStyle.Aggressive => 3,
                CombatStyle.Controlled => 1,
                _ => 0
            };
            return level + bonus;
        }

        public static int GetEffectiveDefence(int level, CombatStyle style)
        {
            int bonus = style switch
            {
                CombatStyle.Defensive => 3,
                CombatStyle.Controlled => 1,
                _ => 0
            };
            return level + bonus;
        }

        public static int GetAttackRoll(int effectiveAttack, int attackBonus)
            => Mathf.FloorToInt(effectiveAttack * (attackBonus + 64));

        public static int GetDefenceRoll(int effectiveDefence, int defenceBonus)
            => Mathf.FloorToInt(effectiveDefence * (defenceBonus + 64));

        public static float ChanceToHit(int attackRoll, int defenceRoll)
        {
            float chance;
            if (attackRoll > defenceRoll)
                chance = 1f - (defenceRoll + 2f) / (2f * (attackRoll + 1f));
            else
                chance = attackRoll / (2f * (defenceRoll + 1f));
            return Mathf.Clamp(chance, MIN_HIT_CHANCE, 1f);
        }

        public static int GetMaxHit(int effectiveStrength, int strengthBonus)
        {
            int maxHit = Mathf.FloorToInt(0.5f + effectiveStrength * (strengthBonus + 64) / 640f);
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
