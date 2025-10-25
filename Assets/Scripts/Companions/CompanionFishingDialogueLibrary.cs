using System.Collections.Generic;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Centralises flavour text for companion-directed fishing interactions so chatter can
    /// be refreshed without editing gameplay scripts.
    /// </summary>
    public static class CompanionFishingDialogueLibrary
    {
        private static readonly string[] InventoryFullLines =
        {
            "My bucket's packed with fish already.",
            "No more room for fish in my pack.",
            "I'm overflowing with the catch—need to bank soon.",
            "Can't net another fish until I clear some space.",
            "Inventory's stuffed with slippery friends right now."
        };

        private static readonly string[] MissingToolLines =
        {
            "Hand me a proper rod and I'll cast right away.",
            "No fishing gear on me—mind lending one?",
            "I'd love to fish, but my rod's sitting in the bank.",
            "Give me a net or rod and I'll get to work.",
            "Can't fish empty-handed—equip me with some tackle."
        };

        private static readonly string[] MissingBaitLines =
        {
            "Need bait first—these fish aren't biting on wishes.",
            "No bait, no catch. Let's grab some first.",
            "I'm out of bait, can we restock?",
            "Baitless fishing isn't going to work, partner.",
            "We should pick up bait before I cast again."
        };

        private static readonly string[] NoSpotsLines =
        {
            "No fishing spots nearby—just calm water.",
            "Nothing to cast at here, let's move on.",
            "Looks quiet—no fishable spots in range.",
            "I can't see a decent place to cast right now.",
            "No bite-worthy water here, let's try elsewhere."
        };

        private static readonly string[] PlayerBusyLines =
        {
            "You're already on that spot—I'll wait for another.",
            "You've got this one, I'll scout for the next catch.",
            "I'll step back while you reel that in.",
            "You're fishing there already; I'll hold off.",
            "I'll grab the next spot so we don't crowd each other."
        };

        private static readonly string[] StuckApologyLines =
        {
            "Got tangled in the reeds—sorry about that.",
            "Line snagged up. I'll reset and try again.",
            "Path jammed up; I'll shake it off.",
            "Snagged on something, give me a moment.",
            "Route blocked—I'll regroup and try another angle."
        };

        private static readonly string[] CooldownLines =
        {
            "Still drying out my gear—give me {0} more minutes, {1}.",
            "Let me rest my wrists for about {0} minutes, {1}.",
            "Need a short break before the next cast—{0} minutes, {1}.",
            "Line's tangled, I'll sort it out in {0} minutes, {1}.",
            "Give me {0} minutes to prep fresh bait, {1}."
        };

        private const string InventoryFallback = "My inventory is full of fish.";
        private const string MissingToolFallback = "I need a fishing tool before I can cast.";
        private const string MissingBaitFallback = "I need some bait first.";
        private const string NoSpotsFallback = "No fishing spots nearby to try.";
        private const string PlayerBusyFallback = "You're already fishing that spot.";
        private const string StuckFallback = "Got snagged for a moment, sorry about that.";
        private const string CooldownFallback = "Still cooling down from fishing. Give me a bit longer, {1}.";
        private const string ToolRequirementFormat = "I need Fishing level {0} to use {1}.";
        private const string LevelRequirementFormat = "I need Fishing level {0} for this spot.";

        public static string GetRandomInventoryFullLine()
        {
            return GetRandomLine(InventoryFullLines, InventoryFallback);
        }

        public static string GetRandomMissingToolLine()
        {
            return GetRandomLine(MissingToolLines, MissingToolFallback);
        }

        public static string GetRandomMissingBaitLine()
        {
            return GetRandomLine(MissingBaitLines, MissingBaitFallback);
        }

        public static string GetRandomNoSpotsLine()
        {
            return GetRandomLine(NoSpotsLines, NoSpotsFallback);
        }

        public static string GetRandomPlayerBusyLine()
        {
            return GetRandomLine(PlayerBusyLines, PlayerBusyFallback);
        }

        public static string GetRandomStuckApologyLine()
        {
            return GetRandomLine(StuckApologyLines, StuckFallback);
        }

        public static string GetToolLevelRequirementLine(int requiredLevel, string toolName)
        {
            if (requiredLevel <= 0)
                requiredLevel = 1;

            string resolvedName = string.IsNullOrWhiteSpace(toolName) ? "that tool" : toolName.Trim();
            return string.Format(ToolRequirementFormat, requiredLevel, resolvedName);
        }

        public static string GetLevelRequirementLine(int requiredLevel)
        {
            if (requiredLevel <= 0)
                requiredLevel = 1;

            return string.Format(LevelRequirementFormat, requiredLevel);
        }

        public static string GetCooldownLine(string playerName, int minutes)
        {
            if (minutes < 1)
                minutes = 1;

            string safeName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();
            string template = GetRandomLine(CooldownLines, CooldownFallback);
            return string.Format(template, minutes, safeName);
        }

        private static string GetRandomLine(IReadOnlyList<string> pool, string fallback)
        {
            if (pool != null && pool.Count > 0)
            {
                int index = Random.Range(0, pool.Count);
                string line = pool[index];
                if (!string.IsNullOrWhiteSpace(line))
                    return line;
            }

            return fallback;
        }
    }
}
