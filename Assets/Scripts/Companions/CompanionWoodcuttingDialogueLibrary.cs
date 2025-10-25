using System.Collections.Generic;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Centralises flavour text for companion-driven woodcutting interactions so chatter can
    /// be refreshed without editing gameplay scripts.
    /// </summary>
    public static class CompanionWoodcuttingDialogueLibrary
    {
        private static readonly string[] InventoryFullLines =
        {
            "My pack's stuffed with logs, can't take more.",
            "No room for another log right now.",
            "I'm full up on wood, need to bank first.",
            "Can't grab that log, my bag's jammed.",
            "Inventory's capped, even the beaver's out of space."
        };

        private static readonly string[] MissingAxeLines =
        {
            "Hand me an axe and I'll get chopping.",
            "No axe, no chop. Help me out?",
            "I'd love to, but I left my axe in the bank.",
            "Axe first, logs second. I'm empty-handed.",
            "Can't swing air—equip me with an axe."
        };

        private static readonly string[] NoTreesLines =
        {
            "Nothing leafy nearby for me to chop.",
            "No trees in range, give me a different spot.",
            "All clear here—no trunks worth swinging at.",
            "I'm not seeing a tree worth chopping right now.",
            "We need to move, there aren't any trees close." 
        };

        private static readonly string[] PlayerBusyLines =
        {
            "You're already on that tree—I'll grab another.",
            "Looks like you've claimed that trunk, I'll wait.",
            "You're chopping it already; I won't crowd you.",
            "I'll stand back while you finish that tree.",
            "You take that one, I'll find the next." 
        };

        private static readonly string[] StuckApologyLines =
        {
            "Got stuck on a root—sorry about that.",
            "Path jammed up, couldn't reach the tree.",
            "These roots tripped me up, I'll try again soon.",
            "Snagged on something, had to stop.",
            "Route blocked—I'll reset and keep at it." 
        };

        private static readonly string[] CooldownLines =
        {
            "Still resting my arms, give me about {0} more minutes, {1}.",
            "Need a breather before I chop again—{0} minutes, {1}.",
            "Let my wrists recover for {0} minutes and I'll swing again, {1}.",
            "Chopping muscles cooling down—try again in {0} minutes, {1}.",
            "Give me {0} minutes to shake off the splinters, {1}."
        };

        private const string InventoryFallback = "My inventory is full of logs.";
        private const string MissingAxeFallback = "I need an axe before I can chop.";
        private const string NoTreesFallback = "No trees nearby to chop.";
        private const string PlayerBusyFallback = "You're already chopping that one.";
        private const string StuckFallback = "Got stuck, sorry about that.";
        private const string CooldownFallback = "Still cooling down from chopping. Give me a bit longer, {1}.";
        private const string LevelRequirementFormat = "I need Woodcutting level {0} for that tree.";

        public static string GetRandomInventoryFullLine()
        {
            return GetRandomLine(InventoryFullLines, InventoryFallback);
        }

        public static string GetRandomMissingAxeLine()
        {
            return GetRandomLine(MissingAxeLines, MissingAxeFallback);
        }

        public static string GetRandomNoTreesLine()
        {
            return GetRandomLine(NoTreesLines, NoTreesFallback);
        }

        public static string GetRandomPlayerBusyLine()
        {
            return GetRandomLine(PlayerBusyLines, PlayerBusyFallback);
        }

        public static string GetRandomStuckApologyLine()
        {
            return GetRandomLine(StuckApologyLines, StuckFallback);
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
