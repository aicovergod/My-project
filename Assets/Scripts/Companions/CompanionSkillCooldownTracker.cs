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

        /// <summary>
        /// Reusable buffer that stores skills which should be removed after an enumeration finishes.
        /// </summary>
        private readonly List<SkillType> expiredSkillsBuffer = new List<SkillType>(4);

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

            DateTime utcNow = DateTime.UtcNow;
            if (!TryResolveRemainingDuration(ticks, utcNow, out DateTime expiryUtc, out TimeSpan resolvedRemaining))
            {
                cooldownExpiryTicks.Remove(skill);
                Save();
                return false;
            }

            remaining = resolvedRemaining;
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

        /// <summary>
        /// Clears every tracked cooldown and returns how many active timers were removed.
        /// </summary>
        /// <returns>Total number of timers that were actively counting down.</returns>
        public int ClearAllCooldowns()
        {
            if (cooldownExpiryTicks.Count == 0)
            {
                Save();
                return 0;
            }

            DateTime utcNow = DateTime.UtcNow;
            int clearedCount = 0;

            foreach (var kvp in cooldownExpiryTicks)
            {
                if (TryResolveRemainingDuration(kvp.Value, utcNow, out _, out TimeSpan remaining) && remaining > TimeSpan.Zero)
                    clearedCount++;
            }

            cooldownExpiryTicks.Clear();
            Save();
            return clearedCount;
        }

        /// <summary>
        /// Fills the supplied buffer with snapshots describing each active cooldown.
        /// </summary>
        /// <param name="buffer">Destination list that receives the active cooldown states.</param>
        /// <returns>A read-only view over the populated buffer.</returns>
        public IReadOnlyList<CooldownState> GetActiveCooldowns(List<CooldownState> buffer = null)
        {
            buffer ??= new List<CooldownState>(cooldownExpiryTicks.Count);
            buffer.Clear();

            if (cooldownExpiryTicks.Count == 0)
                return buffer;

            DateTime utcNow = DateTime.UtcNow;
            expiredSkillsBuffer.Clear();

            foreach (var kvp in cooldownExpiryTicks)
            {
                if (!TryResolveRemainingDuration(kvp.Value, utcNow, out DateTime expiryUtc, out TimeSpan remaining))
                {
                    expiredSkillsBuffer.Add(kvp.Key);
                    continue;
                }

                buffer.Add(new CooldownState(kvp.Key, expiryUtc, remaining));
            }

            if (expiredSkillsBuffer.Count > 0)
            {
                for (int i = 0; i < expiredSkillsBuffer.Count; i++)
                    cooldownExpiryTicks.Remove(expiredSkillsBuffer[i]);

                expiredSkillsBuffer.Clear();
                Save();
            }

            buffer.Sort((left, right) => left.ExpiryUtc.CompareTo(right.ExpiryUtc));
            return buffer;
        }

        /// <summary>
        /// Attempts to convert the stored expiry ticks into a remaining duration snapshot.
        /// </summary>
        private static bool TryResolveRemainingDuration(
            long ticks,
            DateTime utcNow,
            out DateTime expiryUtc,
            out TimeSpan remaining)
        {
            expiryUtc = DateTime.MinValue;
            remaining = TimeSpan.Zero;

            if (ticks <= 0)
                return false;

            try
            {
                expiryUtc = new DateTime(ticks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            if (expiryUtc <= utcNow)
                return false;

            remaining = expiryUtc - utcNow;
            return true;
        }

        /// <summary>
        /// Immutable snapshot describing a single active cooldown.
        /// </summary>
        public readonly struct CooldownState
        {
            public CooldownState(SkillType skill, DateTime expiryUtc, TimeSpan remaining)
            {
                Skill = skill;
                ExpiryUtc = expiryUtc;
                Remaining = remaining;
            }

            /// <summary>Skill whose cooldown is currently active.</summary>
            public SkillType Skill { get; }

            /// <summary>UTC timestamp when the cooldown will expire.</summary>
            public DateTime ExpiryUtc { get; }

            /// <summary>Remaining time until the cooldown ends.</summary>
            public TimeSpan Remaining { get; }
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
