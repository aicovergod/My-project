using System.Collections.Generic;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Centralises flavour strings for the companion-directed cooking workflow so controllers can
    /// emit consistent chatter without embedding localisation details in gameplay code.
    /// </summary>
    public static class CompanionCookingDialogueLibrary
    {
        private static readonly string[] InventoryFullLines =
        {
            "Pack’s bursting with stews already.",
            "No more space for pies—need to unload soon.",
            "Can’t whisk another dish in here, I’m stacked full.",
            "Inventory’s stuffed with meals, banking run?",
            "I’m full up on food, boss."
        };

        private static readonly string[] MissingIngredientLines =
        {
            "Need raw ingredients before I can start cooking.",
            "I’m out of uncooked food—grab me something to prep.",
            "Nothing left to cook with, partner.",
            "No raw supplies on me, can’t fire up the pan.",
            "I’d need some fresh ingredients first."
        };

        private static readonly string[] MissingToolLines =
        {
            "I need a proper cooking tool equipped.",
            "No cookware on me—hand me something to cook with.",
            "Give me a skillet or something and I’ll get to it.",
            "Need a cooking tool before I can start.",
            "Empty hands won’t cook much—equip me with gear."
        };

        private static readonly string[] PlayerBusyLines =
        {
            "You’re at that range already—I’ll wait for another.",
            "You’ve got that oven covered, I’ll stand back.",
            "I’ll let you finish there before I jump in.",
            "You’re working that station—give me another.",
            "I’ll cook once you’re free of that range."
        };

        private static readonly string[] StationUnavailableLines =
        {
            "No free ranges around here.",
            "I don’t see a spare cooker nearby.",
            "Kitchen’s quiet—nothing to cook on.",
            "No open fires in range, let’s move.",
            "No cooking spots here right now."
        };

        private static readonly string[] StationOccupiedLines =
        {
            "Someone’s already using that station.",
            "Range is busy—need another one.",
            "That cooker’s tied up at the moment.",
            "Looks crowded there, can’t squeeze in.",
            "That fire’s spoken for."
        };

        private static readonly string[] StuckLines =
        {
            "Got wedged in the kitchen clutter—my bad.",
            "Pan slipped, I’ll reset my footing.",
            "Kitchen jammed me up for a moment.",
            "Snagged on something, give me a second.",
            "Path was blocked—trying a new angle."
        };

        private static readonly string[] CooldownLines =
        {
            "Let me cool the pans for {0} more minutes, {1}.",
            "Still wiping down the kitchen—give me {0} minutes, {1}.",
            "Need {0} minutes to prep the station again, {1}.",
            "Hands are burnt, {1}. {0} minutes and I’m ready.",
            "Give me {0} minutes to restock spices, {1}."
        };

        private const string InventoryFullFallback = "My bag is stuffed with cooked food.";
        private const string MissingIngredientFallback = "I need raw ingredients before I can cook.";
        private const string MissingToolFallback = "I need a cooking tool equipped.";
        private const string PlayerBusyFallback = "You’re already using that station.";
        private const string StationUnavailableFallback = "No open cooking stations nearby.";
        private const string StationOccupiedFallback = "That cooking spot is already taken.";
        private const string StuckFallback = "Got hung up for a moment, sorry.";
        private const string CooldownFallback = "Need a short break before cooking again, {1}.";

        public static string GetRandomInventoryFullLine()
        {
            return GetRandomLine(InventoryFullLines, InventoryFullFallback);
        }

        public static string GetRandomMissingIngredientLine()
        {
            return GetRandomLine(MissingIngredientLines, MissingIngredientFallback);
        }

        public static string GetRandomMissingToolLine()
        {
            return GetRandomLine(MissingToolLines, MissingToolFallback);
        }

        public static string GetRandomPlayerBusyLine()
        {
            return GetRandomLine(PlayerBusyLines, PlayerBusyFallback);
        }

        public static string GetRandomStationUnavailableLine()
        {
            return GetRandomLine(StationUnavailableLines, StationUnavailableFallback);
        }

        public static string GetRandomStationOccupiedLine()
        {
            return GetRandomLine(StationOccupiedLines, StationOccupiedFallback);
        }

        public static string GetRandomStuckLine()
        {
            return GetRandomLine(StuckLines, StuckFallback);
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
                string candidate = pool[index];
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return fallback;
        }
    }
}
