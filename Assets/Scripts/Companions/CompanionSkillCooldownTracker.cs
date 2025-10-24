using System;
using System.Collections.Generic;
using Core.Save;
using Skills;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Persists per-skill cooldown timers for the companion so command handlers can
    /// throttle repeated requests across scene loads and play sessions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionSkillCooldownTracker : MonoBehaviour, ISaveable
    {
        private const string SaveKey = "companion_skill_cooldowns";

        /// <summary>Runtime cache storing UTC expiry ticks for each tracked skill cooldown.</summary>
        private readonly Dictionary<SkillType, long> cooldownExpiryTicks = new Dictionary<SkillType, long>();

        private void OnEnable()
        {
            SaveManager.Register(this);
        }

        private void OnDisable()
        {
            Save();
            SaveManager.Unregister(this);
        }

        /// <inheritdoc />
        public void Load()
        {
            cooldownExpiryTicks.Clear();

            var snapshot = SaveManager.Load<CooldownSnapshot>(SaveKey);
            if (snapshot?.entries == null || snapshot.entries.Count == 0)
                return;

            DateTime utcNow = DateTime.UtcNow;
            for (int i = 0; i < snapshot.entries.Count; i++)
            {
                var entry = snapshot.entries[i];
                if (entry == null)
                    continue;

                if (!Enum.IsDefined(typeof(SkillType), entry.skill))
                    continue;

                if (entry.expiryUtcTicks <= 0)
                    continue;

                DateTime expiryUtc;
                try
                {
                    expiryUtc = new DateTime(entry.expiryUtcTicks, DateTimeKind.Utc);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                if (expiryUtc <= utcNow)
                    continue;

                cooldownExpiryTicks[entry.skill] = entry.expiryUtcTicks;
            }
        }

        /// <inheritdoc />
        public void Save()
        {
            if (cooldownExpiryTicks.Count == 0)
            {
                SaveManager.Delete(SaveKey);
                return;
            }

            var snapshot = new CooldownSnapshot();
            DateTime utcNow = DateTime.UtcNow;

            foreach (var kvp in cooldownExpiryTicks)
            {
                long ticks = kvp.Value;
                if (ticks <= 0)
                    continue;

                DateTime expiryUtc;
                try
                {
                    expiryUtc = new DateTime(ticks, DateTimeKind.Utc);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                if (expiryUtc <= utcNow)
                    continue;

                snapshot.entries.Add(new CooldownEntry
                {
                    skill = kvp.Key,
                    expiryUtcTicks = ticks
                });
            }

            if (snapshot.entries.Count == 0)
            {
                SaveManager.Delete(SaveKey);
                return;
            }

            SaveManager.Save(SaveKey, snapshot);
        }

        /// <summary>
        /// Starts or refreshes the cooldown for the supplied skill.
        /// </summary>
        public void StartCooldown(SkillType skill, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                ClearCooldown(skill);
                return;
            }

            DateTime expiryUtc = DateTime.UtcNow.Add(duration);
            cooldownExpiryTicks[skill] = expiryUtc.Ticks;
            Save();
        }

        /// <summary>
        /// Attempts to retrieve the remaining time on the supplied skill cooldown.
        /// </summary>
        /// <param name="skill">Skill to inspect.</param>
        /// <param name="remaining">Remaining duration when a cooldown is active.</param>
        /// <returns><c>true</c> when the cooldown is active, otherwise <c>false</c>.</returns>
        public bool TryGetRemaining(SkillType skill, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;

            if (!cooldownExpiryTicks.TryGetValue(skill, out long ticks) || ticks <= 0)
                return false;

            DateTime expiryUtc;
            try
            {
                expiryUtc = new DateTime(ticks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                cooldownExpiryTicks.Remove(skill);
                return false;
            }

            DateTime utcNow = DateTime.UtcNow;
            if (expiryUtc <= utcNow)
            {
                cooldownExpiryTicks.Remove(skill);
                return false;
            }

            remaining = expiryUtc - utcNow;
            return true;
        }

        /// <summary>
        /// Clears the active cooldown for the supplied skill when one exists.
        /// </summary>
        /// <param name="skill">Skill whose cooldown should be removed.</param>
        public void ClearCooldown(SkillType skill)
        {
            if (!cooldownExpiryTicks.Remove(skill))
                return;

            Save();
        }

        [Serializable]
        private sealed class CooldownSnapshot
        {
            public List<CooldownEntry> entries = new List<CooldownEntry>();
        }

        [Serializable]
        private sealed class CooldownEntry
        {
            public SkillType skill;
            public long expiryUtcTicks;
        }
    }
}
