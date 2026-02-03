using System;
using System.Collections.Generic;
using Core.Save;
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
        private const string SaveKey = "CompanionSkills";

        /// <summary>In-memory XP cache keyed by skill type.</summary>
        private readonly Dictionary<SkillType, float> xpBySkill = new();

        /// <summary>Cached XP table so baseline lookups remain consistent.</summary>
        private XpTable cachedXpTable;

        /// <summary>Last snapshot loaded from disk so lookups avoid repeated deserialisation.</summary>
        private CompanionSkillSaveData cachedSnapshot;

        /// <summary>
        /// Prepares the dictionary with baseline XP values so Hitpoints start at level 10 while other skills
        /// begin at level 1. When no XP table is available a deterministic fallback formula is used.
        /// </summary>
        public void ConfigureBaseline(XpTable xpTable)
        {
            cachedXpTable = xpTable;
            xpBySkill.Clear();
            foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
            {
                xpBySkill[type] = ResolveBaselineXp(type);
            }

            cachedSnapshot = SaveManager.Load<CompanionSkillSaveData>(SaveKey);
            EnsureSnapshotCollections();

            if (cachedSnapshot != null)
                ApplySnapshot(cachedSnapshot);
        }

        /// <summary>
        /// Ensures the cached snapshot and nested collections are instantiated before writes occur.
        /// </summary>
        private void EnsureSnapshotCollections()
        {
            if (cachedSnapshot == null)
                cachedSnapshot = new CompanionSkillSaveData();

            if (cachedSnapshot.entries == null)
                cachedSnapshot.entries = new List<CompanionSkillEntry>();
        }

        /// <summary>
        /// Applies the persisted snapshot data to the in-memory cache while clamping to baseline XP.
        /// </summary>
        private void ApplySnapshot(CompanionSkillSaveData snapshot)
        {
            if (snapshot?.entries == null)
                return;

            foreach (var entry in snapshot.entries)
            {
                float baseline = ResolveBaselineXp(entry.skill);
                float clamped = Mathf.Max(baseline, Mathf.Max(0f, entry.xp));
                xpBySkill[entry.skill] = clamped;
            }
        }

        /// <inheritdoc />
        public float LoadXp(SkillType type)
        {
            if (!xpBySkill.TryGetValue(type, out var xp))
            {
                // Attempt to restore the entry from the cached snapshot so late-initialised skills
                // still receive their saved XP values.
                ApplySnapshot(EnsureSnapshotLoaded());

                if (!xpBySkill.TryGetValue(type, out xp))
                {
                    xp = ResolveBaselineXp(type);
                    xpBySkill[type] = xp;
                }
            }

            return xp;
        }

        /// <inheritdoc />
        public void SaveXp(SkillType type, float xp)
        {
            float baseline = ResolveBaselineXp(type);
            float clamped = Mathf.Max(baseline, Mathf.Max(0f, xp));
            xpBySkill[type] = clamped;
            PersistSnapshot();
        }

        /// <summary>
        /// Writes the in-memory cache to the save system so companion skills persist across sessions.
        /// </summary>
        private void PersistSnapshot()
        {
            EnsureSnapshotCollections();
            cachedSnapshot.entries.Clear();

            foreach (var kvp in xpBySkill)
            {
                cachedSnapshot.entries.Add(new CompanionSkillEntry
                {
                    skill = kvp.Key,
                    xp = kvp.Value
                });
            }

            SaveManager.Save(SaveKey, cachedSnapshot);
        }

        /// <summary>
        /// Loads the latest snapshot from the save system when one is not already cached.
        /// </summary>
        private CompanionSkillSaveData EnsureSnapshotLoaded()
        {
            if (cachedSnapshot == null)
            {
                cachedSnapshot = SaveManager.Load<CompanionSkillSaveData>(SaveKey);
                EnsureSnapshotCollections();
            }

            return cachedSnapshot;
        }

        /// <summary>
        /// Calculates the baseline XP for a skill, ensuring Hitpoints starts at level 10.
        /// </summary>
        private float ResolveBaselineXp(SkillType type)
        {
            int targetLevel = type == SkillType.Hitpoints ? 10 : 1;
            return cachedXpTable != null ? cachedXpTable.GetXpForLevel(targetLevel) : EstimateXpForLevel(targetLevel);
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

        [Serializable]
        public class CompanionSkillSaveData
        {
            public List<CompanionSkillEntry> entries = new();
        }

        [Serializable]
        public class CompanionSkillEntry
        {
            public SkillType skill;
            public float xp;
        }
    }
}
