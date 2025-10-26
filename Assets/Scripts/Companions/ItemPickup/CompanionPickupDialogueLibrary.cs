/// Feature: Added pickup chatter library for companion inventory interactions.
using System.Collections.Generic;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Provides flavour text for companion pickup interactions so inventory feedback
    /// stays centralised and can be refreshed without touching the controller logic.
    /// </summary>
    public static class CompanionPickupDialogueLibrary
    {
        /// <summary>Chance applied to the pickup success responses.</summary>
        private const float PickupSuccessResponseChance = 0.1f;

        /// <summary>Pool of responses used when the companion inventory is full.</summary>
        private static readonly string[] InventoryFullResponses =
        {
            "My inventory is full, i cant grab that",
            "I cant get that, my inv is full",
            "My inv is full, sorry",
            "My inventory is full, sorry.",
            "I need to bank my items, before i pick anything else up.",
            "My inventory’s full, can’t grab that.",
            "can’t pick that up, my inv’s full.",
            "inv’s full, can’t take it.",
            "no space left in my inv.",
            "nah, my inventory’s packed.",
            "my inv’s full again, can’t grab it.",
            "cant carry any more stuff.",
            "inv’s full, gotta bank first.",
            "no room for that one.",
            "cant hold it, my inv’s stuffed.",
            "my inventory’s full, again.",
            "im full up, can’t grab that.",
            "no slots left, sorry.",
            "cant take it, i’m full.",
            "inv’s full, gotta drop something first.",
            "nah, no space for that.",
            "my inv’s full, can’t even pick up a pebble.",
            "yeah, i’m full. need to bank soon.",
            "my inv’s maxed out, can’t pick that.",
            "full again, i swear this bag shrinks.",
            "cant grab it, no space left.",
            "inv’s full, same old story.",
            "nah, not picking that up, no room.",
            "no space, sorry {playerName}.",
            "im full, cant take any more.",
            "got no room left, need to clear some space.",
            "cant pick that, inv’s packed tight.",
            "my inventory’s full, gotta dump some stuff.",
            "cant even fit a coin in there, full.",
            "yeah, my bag’s full, can’t grab that."
        };

        /// <summary>Pool of responses used after successfully collecting a drop.</summary>
        private static readonly string[] PickupSuccessResponses =
        {
            "got it.",
            "grabbed it.",
            "picked it up.",
            "got the loot.",
            "easy grab.",
            "picked that up for you.",
            "got it, all good.",
            "done, it’s in the bag.",
            "yoink.",
            "snagged it.",
            "picked it up, no problem.",
            "done, grabbed it.",
            "got it, let’s move.",
            "picked it clean.",
            "in the bag.",
            "mine now.",
            "got it sorted.",
            "grabbed it quick.",
            "nice grab.",
            "got it before it disappeared.",
            "done, grabbed that.",
            "easy pick up.",
            "picked that clean off the floor.",
            "snatched it up.",
            "got it, {playerName}.",
            "picked it up nice and quick.",
            "done, added to inv.",
            "grabbed it, easy.",
            "sorted, picked up.",
            "picked it, let’s go."
        };

        /// <summary>
        /// Retrieves a random inventory-full line with the supplied player name inserted
        /// into any placeholders.
        /// </summary>
        /// <param name="playerName">Name of the player issuing the command.</param>
        public static string GetRandomInventoryFullResponse(string playerName)
        {
            return ComposeLine(InventoryFullResponses, playerName);
        }

        /// <summary>
        /// Attempts to retrieve a pickup success response while respecting the
        /// configured probability gate.
        /// </summary>
        /// <param name="playerName">Name of the player receiving the assistance.</param>
        /// <param name="response">Randomly selected response when available.</param>
        /// <returns>True when a response should be delivered.</returns>
        public static bool TryGetPickupSuccessResponse(string playerName, out string response)
        {
            response = string.Empty;

            if (PickupSuccessResponses == null || PickupSuccessResponses.Length == 0)
                return false;

            if (Random.value > PickupSuccessResponseChance)
                return false;

            response = ComposeLine(PickupSuccessResponses, playerName);
            return !string.IsNullOrEmpty(response);
        }

        /// <summary>
        /// Selects a random entry from the supplied pool and applies the name placeholder.
        /// </summary>
        private static string ComposeLine(IReadOnlyList<string> pool, string playerName)
        {
            if (pool == null || pool.Count == 0)
                return string.Empty;

            int index = Random.Range(0, pool.Count);
            string template = pool[index];
            if (string.IsNullOrWhiteSpace(template))
                return string.Empty;

            string resolvedName = ResolvePlayerName(playerName);
            return template.Replace("{playerName}", resolvedName);
        }

        /// <summary>
        /// Normalises the supplied name so dialogue always has a friendly fallback.
        /// </summary>
        private static string ResolvePlayerName(string playerName)
        {
            return string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();
        }
    }
}

