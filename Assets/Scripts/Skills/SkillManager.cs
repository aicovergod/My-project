using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Skills.Mining;
using Core.Save;

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
        [SerializeField] private MonoBehaviour saveProvider; // optional custom save provider

        private readonly Dictionary<SkillType, SkillRecord> skills = new();
        private ICombatSkillSave save;
        private Coroutine saveRoutine;

        private struct SkillRecord
        {
            public float xp;
            public int level;
        }

        private void Awake()
        {
            save = saveProvider as ICombatSkillSave ?? new SaveManagerCombatSkillSave();
            InitialiseSkill(SkillType.Attack);
            InitialiseSkill(SkillType.Strength);
            InitialiseSkill(SkillType.Defence);
        }

        private void OnEnable()
        {
            saveRoutine = StartCoroutine(SaveLoop());
        }

        private void OnDisable()
        {
            if (saveRoutine != null)
                StopCoroutine(saveRoutine);
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private void InitialiseSkill(SkillType type)
        {
            if (skills.ContainsKey(type))
                return;

            float xp = save.LoadXp(type);
            int lvl = xpTable != null ? xpTable.GetLevel(Mathf.FloorToInt(xp)) : 1;
            skills[type] = new SkillRecord { xp = xp, level = lvl };
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

        private IEnumerator SaveLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(10f);
                Save();
            }
        }

        private void Save()
        {
            foreach (var kvp in skills)
                save.SaveXp(kvp.Key, kvp.Value.xp);
        }
    }

    public interface ICombatSkillSave
    {
        float LoadXp(SkillType type);
        void SaveXp(SkillType type, float xp);
    }

    public class SaveManagerCombatSkillSave : ICombatSkillSave
    {
        private static string Key(SkillType type) => $"{type.ToString().ToLowerInvariant()}_xp";

        public float LoadXp(SkillType type)
        {
            return SaveManager.Load<float>(Key(type));
        }

        public void SaveXp(SkillType type, float xp)
        {
            SaveManager.Save(Key(type), xp);
        }
    }
}
