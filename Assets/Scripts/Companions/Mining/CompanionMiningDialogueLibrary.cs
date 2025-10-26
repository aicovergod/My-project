using System.Collections.Generic;

namespace Companions
{
    /// <summary>
    /// Centralises flavour text for companion-directed mining interactions so chatter updates
    /// can be performed without editing gameplay scripts.
    /// </summary>
    public static class CompanionMiningDialogueLibrary
    {
        private static readonly string[] MissingPickaxeLines =
        {
            "I need a pickaxe to mine that"
        };

        private static readonly string[] PlayerBusyLines =
        {
            "Looks like you're already mining that rock"
        };

        private static readonly string[] StuckApologyLines =
        {
            "Hey, I got stuck while I was mining, sorry."
        };

        private static readonly string[] LevelRequirementLines =
        {
            "I need Mining level {0} for that rock.",
            "That ore calls for Mining level {0}; I'm not there yet."
        };

        private const string MissingPickaxeFallback = "I need a pickaxe to mine that";
        private const string PlayerBusyFallback = "Looks like you're already mining that rock";
        private const string StuckFallback = "Hey, I got stuck while I was mining, sorry.";
        private const string LevelRequirementFallback = "I don't have the correct mining level for that";

        /// <summary>
        /// Gets a random companion line when no pickaxe is available.
        /// </summary>
        public static string GetRandomMissingPickaxeLine()
        {
            return GetRandomLine(MissingPickaxeLines, MissingPickaxeFallback);
        }

        /// <summary>
        /// Gets a random line when the player is already mining the requested rock.
        /// </summary>
        public static string GetRandomPlayerBusyLine()
        {
            return GetRandomLine(PlayerBusyLines, PlayerBusyFallback);
        }

        /// <summary>
        /// Gets a random apology line when the companion becomes stuck.
        /// </summary>
        public static string GetRandomStuckApologyLine()
        {
            return GetRandomLine(StuckApologyLines, StuckFallback);
        }

        /// <summary>
        /// Gets a random line informing the player of the Mining level requirement.
        /// </summary>
        /// <param name="requiredLevel">Level required to mine the target rock.</param>
        public static string GetLevelRequirementLine(int requiredLevel)
        {
            if (requiredLevel <= 0)
                requiredLevel = 1;

            string template = GetRandomLine(LevelRequirementLines, LevelRequirementFallback);
            if (template.Contains("{0}"))
                return string.Format(template, requiredLevel);

            return template;
        }

        private static string GetRandomLine(IReadOnlyList<string> pool, string fallback)
        {
            if (pool != null && pool.Count > 0)
            {
                int index = CompanionDialogueRandomProvider.SampleIndex(pool.Count);
                string line = pool[index];
                if (!string.IsNullOrWhiteSpace(line))
                    return line;
            }

            return fallback;
        }
    }
}
