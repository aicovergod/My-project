using System.Collections.Generic;
using UnityEngine;
using Skills.Mining;

namespace Skills
{
    /// <summary>
    /// Centralised manager for combat skills such as Attack, Strength and Defence.
    /// Handles XP gain and level calculations using the shared <see cref="XpTable"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class SkillManager : MonoBehaviour
    {
        [SerializeField] private XpTable xpTable;

        private readonly Dictionary<SkillType, SkillRecord> skills = new();

        private struct SkillRecord
        {
            public float xp;
            public int level;
        }

        private void Awake()
        {
            InitialiseSkill(SkillType.Attack);
            InitialiseSkill(SkillType.Strength);
            InitialiseSkill(SkillType.Defence);
        }

        private void InitialiseSkill(SkillType type)
        {
            if (!skills.ContainsKey(type))
                skills[type] = new SkillRecord { xp = 0f, level = 1 };
        }

        /// <summary>
        /// Add XP to the specified skill and return the new level.
        /// </summary>
        public int AddXP(SkillType skill, float amount)
        {
            if (xpTable == null || amount <= 0f)
                return GetLevel(skill);

            var record = skills[skill];
            record.xp += amount;
            record.level = xpTable.GetLevel(Mathf.FloorToInt(record.xp));
            skills[skill] = record;
            return record.level;
        }

        /// <summary>
        /// Get the current level for the given skill.
        /// </summary>
        public int GetLevel(SkillType skill)
        {
            return skills.TryGetValue(skill, out var record) ? record.level : 1;
        }

        /// <summary>
        /// Get the current XP for the given skill.
        /// </summary>
        public float GetXp(SkillType skill)
        {
            return skills.TryGetValue(skill, out var record) ? record.xp : 0f;
        }
    }
}
