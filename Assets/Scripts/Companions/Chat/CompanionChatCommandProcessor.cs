using System;
using System.Collections.Generic;
using System.Text;
using UI.Chat;
using UnityEngine;

namespace Companions.Chat
{
    /// <summary>
    /// Parses chat lines sent to the companion channel and translates conversational
    /// phrases into actionable companion commands (mining, fishing, woodcutting).
    /// Handles a broad set of phrases, common typos, and slang so the feature feels
    /// forgiving even when the player mistypes the request.
    /// </summary>
    public static class CompanionChatCommandProcessor
    {
        private enum CompanionChatCommandType
        {
            None,
            Mining,
            Fishing,
            Woodcutting,
            Cooking
        }

        private static readonly HashSet<string> ActionTokens = new HashSet<string>(
            new[]
            {
                "start", "begin", "commence", "go", "get", "lets", "let", "please",
                "plz", "pls", "plox", "resume", "continue", "keep", "do", "try",
                "hey", "buddy", "companion", "cmon", "comeon", "hurry"
            });

        private static readonly HashSet<string> MiningVerbs = new HashSet<string>(
            new[]
            {
                "mine", "mining", "minin", "minning", "minen", "minign", "ming",
                "dig", "digging", "diggin", "smash", "smashing", "smashin",
                "smashn", "smashup", "quarry", "quarrying"
            });

        private static readonly HashSet<string> MiningNouns = new HashSet<string>(
            new[]
            {
                "rock", "rocks", "rok", "roks", "rockz", "rokks", "roc", "ore",
                "ores", "orez", "node", "nodes", "vein", "veins", "veinz",
                "orebank"
            });

        private static readonly HashSet<string> FishingVerbs = new HashSet<string>(
            new[]
            {
                "fish", "fishing", "fishin", "fisin", "fising", "fisihng",
                "fishng", "fishign", "fisshing", "angle", "angling", "anglin",
                "angler", "catch", "catching", "katch", "net", "netting",
                "reel", "reeling"
            });

        private static readonly HashSet<string> FishingNouns = new HashSet<string>(
            new[]
            {
                "fish", "fishes", "fishies", "fishy", "spot", "spots", "pool",
                "pools", "hole", "holes", "pond", "river", "harbor", "harbour",
                "dock", "shore"
            });

        private static readonly HashSet<string> WoodcuttingVerbs = new HashSet<string>(
            new[]
            {
                "cut", "cutting", "cuttin", "cutin", "chop", "chopping",
                "choppin", "choping", "hack", "hacking", "axe", "axing",
                "lumber", "logging"
            });

        private static readonly HashSet<string> WoodcuttingNouns = new HashSet<string>(
            new[]
            {
                "tree", "trees", "treez", "log", "logs", "logz", "loggs",
                "wood", "woods", "woodz", "stump", "stumps"
            });

        private static readonly HashSet<string> CookingVerbs = new HashSet<string>(
            new[]
            {
                "cook", "cooking", "cookin", "bake", "baking", "bakin",
                "grill", "grilling", "grillin", "fry", "frying", "fryin",
                "stew", "stewing", "stewin", "prepare", "prepping",
                "mix", "mixing", "mixin", "knead", "simmer", "season",
                "pan", "panfry", "sear"
            });

        private static readonly HashSet<string> CookingNouns = new HashSet<string>(
            new[]
            {
                "meal", "meals", "food", "foods", "dish", "dishes",
                "stew", "stews", "pie", "pies", "cake", "cakes",
                "kitchen", "range", "oven", "fire", "pan", "skillet",
                "grill", "hob"
            });

        private static readonly string[] CookingPhrases =
        {
            "start cooking", "go cook", "cook some food", "cook the meal",
            "prepare a meal", "make some food", "start baking", "start the cooking",
            "cook nearby", "cook something", "make dinner", "fire up the kitchen"
        };

        private static readonly string[] MiningPhrases =
        {
            "start mining", "start minin", "start smashing", "start smashin",
            "mine them rocks", "mine them rock", "mine some rocks",
            "mine some rock", "mine that rock", "mine that rocks",
            "begin mining", "begin minin", "begin smashing", "begin smashin",
            "go mining", "go mine", "go minin", "go smash rocks",
            "mine those rocks", "mine those rock", "mine these rocks",
            "mine these rock", "mine the rock", "mine the rocks",
            "mine the ore", "mine that ore", "mine some ore",
            "smash that rock", "smash them rocks", "smash those rocks",
            "mine da rocks", "mine da rock", "mine dat rock",
            "mine dat rocks", "mine da ore"
        };

        private static readonly string[] FishingPhrases =
        {
            "start fishing", "fish that spot", "start angling", "catch that fish",
            "begin fish", "begin fishing", "begin angling", "catch the fish",
            "catch da fish", "start fishin", "start fish", "go fishing",
            "go fish", "go fishin", "go angling", "go anglin",
            "catch those fish", "catch them fish", "catch some fish",
            "catch some fishes", "catch these fish", "start catching fish",
            "start reeling", "begin reeling"
        };

        private static readonly string[] WoodcuttingPhrases =
        {
            "cut that tree", "cut them trees", "cut them tree", "cut that trees",
            "start cutting", "begin cutting", "begin chopping", "begin choppin",
            "start chopping", "start choppin", "go chopping",
            "go choppin", "go cutting", "go cuttin", "chop that tree",
            "chop those trees", "chop these trees", "chop the tree",
            "chop the trees", "chop some logs", "cut some logs",
            "chop logs", "cut logs", "fell that tree", "fell those trees"
        };

        /// <summary>
        /// Attempts to process a companion-channel chat message as a skilling command.
        /// </summary>
        /// <param name="sender">Display name of the player issuing the command.</param>
        /// <param name="message">Sanitised chat payload routed to the companion channel.</param>
        /// <returns>True when the message was consumed as a command.</returns>
        public static bool TryProcessChatCommand(string sender, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            string trimmedMessage = message.Trim();
            if (trimmedMessage.Length == 0)
                return false;

            string normalised = NormaliseForMatching(trimmedMessage);
            if (string.IsNullOrEmpty(normalised))
                return false;

            string[] tokens = ExtractTokens(normalised);
            if (tokens.Length == 0)
                return false;

            CompanionChatCommandType commandType = DetectCommandType(normalised, tokens);
            if (commandType == CompanionChatCommandType.None)
                return false;

            var chatService = ChatService.Instance;
            string resolvedSender = ResolveSender(chatService, sender);
            if (chatService != null)
                chatService.PublishCompanionMessage(resolvedSender, trimmedMessage, true);
            else
                Debug.LogWarning("[Companion Chat] ChatService unavailable. Command will execute without chat echo.");

            switch (commandType)
            {
                case CompanionChatCommandType.Mining:
                    if (!CompanionManager.TryCommandMineNearby(out var miningFailure))
                        PublishMiningFallback(miningFailure);
                    break;
                case CompanionChatCommandType.Fishing:
                    if (!CompanionManager.TryCommandFishNearby(out var fishingFailure))
                        PublishFishingFallback(fishingFailure);
                    break;
                case CompanionChatCommandType.Woodcutting:
                    if (!CompanionManager.TryCommandChopNearby(out var woodcutFailure))
                        PublishWoodcuttingFallback(woodcutFailure);
                    break;
                case CompanionChatCommandType.Cooking:
                    if (!CompanionManager.TryCommandCookNearby(out var cookingFailure))
                        PublishCookingFallback(cookingFailure);
                    break;
                default:
                    return false;
            }

            return true;
        }

        private static string NormaliseForMatching(string message)
        {
            var builder = new StringBuilder(message.Length);
            bool previousWasSpace = false;

            for (int i = 0; i < message.Length; i++)
            {
                char c = message[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                    previousWasSpace = false;
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (!previousWasSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                }
                else
                {
                    if (c == '\'' || c == '\u2019')
                        continue;

                    if (!previousWasSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                }
            }

            if (builder.Length == 0)
                return string.Empty;

            if (previousWasSpace && builder.Length > 0)
                builder.Length -= 1;

            return builder.ToString();
        }

        private static string[] ExtractTokens(string normalised)
        {
            return normalised.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string ResolveSender(ChatService chatService, string sender)
        {
            if (!string.IsNullOrWhiteSpace(sender))
                return sender.Trim();

            if (chatService != null && !string.IsNullOrWhiteSpace(chatService.ActiveUsername))
                return chatService.ActiveUsername.Trim();

            return "Player";
        }

        private static CompanionChatCommandType DetectCommandType(string normalised, string[] tokens)
        {
            if (MatchesMiningCommand(normalised, tokens))
                return CompanionChatCommandType.Mining;

            if (MatchesFishingCommand(normalised, tokens))
                return CompanionChatCommandType.Fishing;

            if (MatchesWoodcuttingCommand(normalised, tokens))
                return CompanionChatCommandType.Woodcutting;

            if (MatchesCookingCommand(normalised, tokens))
                return CompanionChatCommandType.Cooking;

            return CompanionChatCommandType.None;
        }

        private static bool MatchesMiningCommand(string normalised, IReadOnlyList<string> tokens)
        {
            if (ContainsPhrase(normalised, MiningPhrases))
                return true;

            bool hasVerb = ContainsToken(tokens, MiningVerbs);
            if (!hasVerb)
                return false;

            bool hasAction = ContainsToken(tokens, ActionTokens);
            bool hasNoun = ContainsToken(tokens, MiningNouns);

            if (hasAction || hasNoun)
                return true;

            return tokens.Count == 1; // Single-word imperatives such as "mine!".
        }

        private static bool MatchesFishingCommand(string normalised, IReadOnlyList<string> tokens)
        {
            if (ContainsPhrase(normalised, FishingPhrases))
                return true;

            bool hasVerb = ContainsToken(tokens, FishingVerbs);
            if (!hasVerb)
                return false;

            bool hasAction = ContainsToken(tokens, ActionTokens);
            bool hasNoun = ContainsToken(tokens, FishingNouns);

            if (hasAction || hasNoun)
                return true;

            return tokens.Count == 1; // "Fish!" etc.
        }

        private static bool MatchesWoodcuttingCommand(string normalised, IReadOnlyList<string> tokens)
        {
            if (ContainsPhrase(normalised, WoodcuttingPhrases))
                return true;

            bool hasVerb = ContainsToken(tokens, WoodcuttingVerbs);
            if (!hasVerb)
                return false;

            bool hasAction = ContainsToken(tokens, ActionTokens);
            bool hasNoun = ContainsToken(tokens, WoodcuttingNouns);

            if (hasAction || hasNoun)
                return true;

            return tokens.Count == 1;
        }

        private static bool MatchesCookingCommand(string normalised, IReadOnlyList<string> tokens)
        {
            if (ContainsPhrase(normalised, CookingPhrases))
                return true;

            bool hasVerb = ContainsToken(tokens, CookingVerbs);
            if (!hasVerb)
                return false;

            bool hasAction = ContainsToken(tokens, ActionTokens);
            bool hasNoun = ContainsToken(tokens, CookingNouns);

            if (hasAction || hasNoun)
                return true;

            return tokens.Count == 1;
        }

        private static bool ContainsPhrase(string normalised, IEnumerable<string> phrases)
        {
            foreach (var phrase in phrases)
            {
                if (string.IsNullOrEmpty(phrase))
                    continue;

                string lower = phrase.ToLowerInvariant();
                if (normalised.Equals(lower, StringComparison.Ordinal))
                    return true;

                if (normalised.StartsWith(lower + " ", StringComparison.Ordinal))
                    return true;

                if (normalised.EndsWith(" " + lower, StringComparison.Ordinal))
                    return true;

                if (normalised.Contains(" " + lower + " ", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool ContainsToken(IReadOnlyList<string> tokens, HashSet<string> lookup)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (lookup.Contains(token))
                    return true;

                if (lookup.Contains(TrimTrailingLetter(token)) || lookup.Contains(RemoveTrailingGerund(token)))
                    return true;
            }

            return false;
        }

        private static string TrimTrailingLetter(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 2)
                return token;

            if (token[token.Length - 1] == token[token.Length - 2])
                return token.Substring(0, token.Length - 1);

            return token;
        }

        private static string RemoveTrailingGerund(string token)
        {
            if (string.IsNullOrEmpty(token))
                return token;

            if (token.EndsWith("ing", StringComparison.Ordinal))
                return token.Substring(0, token.Length - 1); // Drop only the 'g' for colloquial spellings.

            return token;
        }

        private static void PublishMiningFallback(CompanionMiningCommandResult failureReason)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            if (!CompanionManager.HasActiveCompanion)
            {
                chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                    "You need to summon me before I can go mining.");
                return;
            }

            switch (failureReason)
            {
                case CompanionMiningCommandResult.InventoryFull:
                case CompanionMiningCommandResult.NoPickaxe:
                case CompanionMiningCommandResult.BlockedByPlayer:
                case CompanionMiningCommandResult.RequirementsNotMet:
                case CompanionMiningCommandResult.Unreachable:
                case CompanionMiningCommandResult.Declined:
                    return; // The mining systems already publish descriptive chat.
                default:
                    chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                        "I can't find any rocks to mine right now.");
                    break;
            }
        }

        private static void PublishFishingFallback(CompanionFishingCommandResult failureReason)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            if (!CompanionManager.HasActiveCompanion)
            {
                chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                    "You need to summon me before I can go fishing.");
                return;
            }

            switch (failureReason)
            {
                case CompanionFishingCommandResult.InventoryFull:
                case CompanionFishingCommandResult.NoTool:
                case CompanionFishingCommandResult.NoBait:
                case CompanionFishingCommandResult.BlockedByPlayer:
                case CompanionFishingCommandResult.RequirementsNotMet:
                case CompanionFishingCommandResult.Declined:
                case CompanionFishingCommandResult.AlreadyFishing:
                    return;
                case CompanionFishingCommandResult.Unreachable:
                    chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                        CompanionFishingDialogueLibrary.GetRandomNoSpotsLine());
                    break;
                default:
                    chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                        "I can't find a good fishing spot right now.");
                    break;
            }
        }

        private static void PublishWoodcuttingFallback(CompanionWoodcuttingCommandResult failureReason)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            if (!CompanionManager.HasActiveCompanion)
            {
                chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                    "You need to summon me before I can start chopping.");
                return;
            }

            switch (failureReason)
            {
                case CompanionWoodcuttingCommandResult.InventoryFull:
                case CompanionWoodcuttingCommandResult.NoAxe:
                case CompanionWoodcuttingCommandResult.BlockedByPlayer:
                case CompanionWoodcuttingCommandResult.RequirementsNotMet:
                case CompanionWoodcuttingCommandResult.Declined:
                case CompanionWoodcuttingCommandResult.AlreadyChopping:
                    return;
                default:
                    chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                        CompanionWoodcuttingDialogueLibrary.GetRandomNoTreesLine());
                    break;
            }
        }

        private static void PublishCookingFallback(CompanionCookingCommandResult failureReason)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            if (!CompanionManager.HasActiveCompanion)
            {
                chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                    "You need to summon me before I can start cooking.");
                return;
            }

            switch (failureReason)
            {
                case CompanionCookingCommandResult.InventoryFull:
                case CompanionCookingCommandResult.MissingIngredients:
                case CompanionCookingCommandResult.MissingTool:
                case CompanionCookingCommandResult.PlayerBusy:
                case CompanionCookingCommandResult.StationUnavailable:
                case CompanionCookingCommandResult.StationOccupied:
                case CompanionCookingCommandResult.Declined:
                    return; // The cooking systems already published context-specific chatter.
                default:
                    chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(),
                        "I can't find a free range to cook on right now.");
                    break;
            }
        }
    }
}
