using System;
using UnityEngine;

namespace Beastmaster
{
    /// <summary>
    /// Scriptable configuration for Beastmaster pet merging durations and cooldowns.
    /// </summary>
    [CreateAssetMenu(fileName = "MergeConfig", menuName = "Beastmaster/Merge Config", order = 0)]
    public class MergeConfig : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public int minLevel;
            public int durationMinutes;
            public int cooldownMinutes;
        }

        [SerializeField]
        private Entry[] entries = new Entry[]
        {
            new Entry { minLevel = 50, durationMinutes = 5, cooldownMinutes = 15 },
            new Entry { minLevel = 60, durationMinutes = 10, cooldownMinutes = 14 },
            new Entry { minLevel = 70, durationMinutes = 15, cooldownMinutes = 13 },
            new Entry { minLevel = 80, durationMinutes = 20, cooldownMinutes = 12 },
            new Entry { minLevel = 90, durationMinutes = 25, cooldownMinutes = 10 },
            new Entry { minLevel = 99, durationMinutes = 30, cooldownMinutes = 0 }
        };

        /// <summary>
        /// Get merge duration and cooldown for a given Beastmaster level.
        /// </summary>
        public bool TryGetMergeParams(int beastmasterLevel, out TimeSpan duration, out TimeSpan cooldown, out bool locked)
        {
            locked = beastmasterLevel < 50;
            duration = TimeSpan.Zero;
            cooldown = TimeSpan.Zero;
            Entry? chosen = null;
            foreach (var e in entries)
            {
                if (beastmasterLevel >= e.minLevel)
                    chosen = e;
            }
            if (!chosen.HasValue)
                return false;
            duration = TimeSpan.FromMinutes(chosen.Value.durationMinutes);
            cooldown = TimeSpan.FromMinutes(chosen.Value.cooldownMinutes);
            return true;
        }
    }
}

