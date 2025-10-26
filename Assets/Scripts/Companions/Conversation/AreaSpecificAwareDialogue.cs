using UnityEngine;

namespace Companions.Conversation
{
    /// <summary>
    /// Houses the contextual flavour dialogue that companions deliver when they notice
    /// specific world areas such as banks, caves, or coastal regions. Separating these
    /// lines from <see cref="CompanionChatLibrary"/> keeps the monolithic chat pool
    /// manageable while still exposing helper methods for the awareness system.
    /// </summary>
    internal static class AreaSpecificAwareDialogue
    {
        /// <summary>Lines surfaced when companions recognise they have entered a bank.</summary>
        private static readonly string[] BankAwarenessChatMessages =
        {
            "Oooh, we're at the bank.",
            "Well, hello banker.",
            "Ah, we're at a bank in Viosla."
        };

        /// <summary>Fallback chat used if the bank awareness pool cannot provide a line.</summary>
        private const string BankAwarenessFallbackLine = "Looks like we're at the bank.";

        /// <summary>Lines reserved for bank awareness when the companion inventory is already full.</summary>
        private static readonly string[] BankAwarenessInventoryFullChatMessages =
        {
            "My inventory is full, maybe I should deposit these items."
        };

        /// <summary>Fallback used when the bank full-awareness pool is empty or malformed.</summary>
        private const string BankAwarenessInventoryFullFallbackLine = "My inventory is full, maybe I should deposit these items.";

        /// <summary>Lines triggered when the companion enters a mining cave.</summary>
        private static readonly string[] MiningCaveAwarenessChatMessages =
        {
            "Time for some mining, eh?",
            "I hope there's no ore golems in here.",
            "I do enjoy a bit of mining.",
            "Are we here to mine?"
        };

        /// <summary>Fallback message for mining cave awareness when the pool is unavailable.</summary>
        private const string MiningCaveAwarenessFallbackLine = "Time for some mining, eh?";

        /// <summary>Lines triggered when the companion steps inside a goblin cave.</summary>
        private static readonly string[] GoblinCaveAwarenessChatMessages =
        {
            "I really hate goblins.",
            "Time to kill some goblins, hmm?",
            "Damn green buggers.",
            "Let's kill some goblins."
        };

        /// <summary>Fallback chat for goblin cave awareness when no pool entry is available.</summary>
        private const string GoblinCaveAwarenessFallbackLine = "Time to kill some goblins, hmm?";

        /// <summary>Lines surfaced when the companion reaches the ocean.</summary>
        private static readonly string[] OceanAwarenessChatMessages =
        {
            "Ah, I do love the view of the ocean.",
            "The sweet smell of the salty ocean.",
            "Do you ever wonder what else is out there?",
            "Do you think there's any sea monsters?"
        };

        /// <summary>Fallback line when the ocean awareness pool is empty.</summary>
        private const string OceanAwarenessFallbackLine = "Ah, I do love the view of the ocean.";

        /// <summary>Lines surfaced when the companion notices a graveyard.</summary>
        private static readonly string[] GraveyardAwarenessChatMessages =
        {
            "I hope no ghosts pop out, {playerName}.",
            "Graveyards really are creepy.",
            "I hate graveyards.",
            "I don't want to run into a ghost here.",
            "I hope the dead are resting easy."
        };

        /// <summary>Fallback line when the graveyard awareness pool cannot produce a message.</summary>
        private const string GraveyardAwarenessFallbackLine = "Graveyards really are creepy.";

        /// <summary>
        /// Returns a bank-themed awareness line, optionally including inventory warnings when full.
        /// </summary>
        /// <param name="companionInventoryFull">Whether the companion inventory has reached capacity.</param>
        /// <returns>Randomly selected bank awareness line with graceful fallback handling.</returns>
        public static string GetRandomBankAwarenessLine(bool companionInventoryFull)
        {
            if (companionInventoryFull)
            {
                int fullPool = BankAwarenessInventoryFullChatMessages != null ? BankAwarenessInventoryFullChatMessages.Length : 0;
                int generalPool = BankAwarenessChatMessages != null ? BankAwarenessChatMessages.Length : 0;
                int total = fullPool + generalPool;

                if (total <= 0)
                    return BankAwarenessInventoryFullFallbackLine;

                int index = Random.Range(0, total);
                if (index < fullPool)
                    return GetRandomLine(BankAwarenessInventoryFullChatMessages, BankAwarenessInventoryFullFallbackLine);

                return GetRandomLine(BankAwarenessChatMessages, BankAwarenessFallbackLine);
            }

            return GetRandomLine(BankAwarenessChatMessages, BankAwarenessFallbackLine);
        }

        /// <summary>Returns a flavour line for mining cave awareness.</summary>
        public static string GetRandomMiningCaveAwarenessLine()
        {
            return GetRandomLine(MiningCaveAwarenessChatMessages, MiningCaveAwarenessFallbackLine);
        }

        /// <summary>Returns a flavour line for goblin cave awareness.</summary>
        public static string GetRandomGoblinCaveAwarenessLine()
        {
            return GetRandomLine(GoblinCaveAwarenessChatMessages, GoblinCaveAwarenessFallbackLine);
        }

        /// <summary>Returns a flavour line for ocean awareness.</summary>
        public static string GetRandomOceanAwarenessLine()
        {
            return GetRandomLine(OceanAwarenessChatMessages, OceanAwarenessFallbackLine);
        }

        /// <summary>Returns a graveyard-flavoured awareness line.</summary>
        public static string GetRandomGraveyardAwarenessLine()
        {
            return GetRandomLine(GraveyardAwarenessChatMessages, GraveyardAwarenessFallbackLine);
        }

        /// <summary>
        /// Shared helper that selects a random entry from the supplied pool while handling edge cases.
        /// </summary>
        /// <param name="pool">Array of potential chat lines.</param>
        /// <param name="fallback">Fallback line used when the pool is empty or the selected entry is invalid.</param>
        /// <returns>Randomly selected chat line with guaranteed non-empty text.</returns>
        private static string GetRandomLine(string[] pool, string fallback)
        {
            if (pool == null || pool.Length == 0)
                return fallback;

            int index = Random.Range(0, pool.Length);
            if (index < 0 || index >= pool.Length)
                return fallback;

            string message = pool[index];
            return string.IsNullOrWhiteSpace(message) ? fallback : message;
        }
    }
}
