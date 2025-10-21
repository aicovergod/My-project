using System;
using System.Collections.Generic;
using Skills;
using Skills.Mining;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Lightweight in-memory save provider for the companion skill manager. This mirrors the player's
    /// XP persistence without writing to disk so runtime spawned companions start fully configured.
    /// </summary>
    public sealed class CompanionSkillMemorySave : MonoBehaviour, ICombatSkillSave
    {
        /// <summary>In-memory XP cache keyed by skill type.</summary>
        private readonly Dictionary<SkillType, float> xpBySkill = new();

        /// <summary>
        /// Prepares the dictionary with baseline XP values so Hitpoints start at level 10 while other skills
        /// begin at level 1. When no XP table is available a deterministic fallback formula is used.
        /// </summary>
        public void ConfigureBaseline(XpTable xpTable)
        {
            xpBySkill.Clear();
            foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
            {
                int targetLevel = type == SkillType.Hitpoints ? 10 : 1;
                float xp = xpTable != null ? xpTable.GetXpForLevel(targetLevel) : EstimateXpForLevel(targetLevel);
                xpBySkill[type] = xp;
            }
        }

        /// <inheritdoc />
        public float LoadXp(SkillType type)
        {
            return xpBySkill.TryGetValue(type, out var xp) ? xp : 0f;
        }

        /// <inheritdoc />
        public void SaveXp(SkillType type, float xp)
        {
            xpBySkill[type] = Mathf.Max(0f, xp);
        }

        /// <summary>
        /// Generates an OSRS-style XP value when no XP table is available by mirroring the classic formula.
        /// </summary>
        private static float EstimateXpForLevel(int level)
        {
            if (level <= 1)
                return 0f;

            int points = 0;
            for (int i = 2; i <= level; i++)
                points += Mathf.FloorToInt(i + 300f * Mathf.Pow(2f, i / 7f));

            return Mathf.Floor(points / 4f);
        }
    }
}
