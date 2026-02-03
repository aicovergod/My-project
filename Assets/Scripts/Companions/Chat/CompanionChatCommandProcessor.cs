using System;
using System.Collections.Generic;
using System.Text;
using Companions.Conversation;
using Companions;
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

        /// <summary>Global stop commands that should cancel any active companion action when spoken by the player.</summary>
        private static readonly HashSet<string> GlobalStopCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "stop",
            "halt",
            "lets go",
            "let us go",
            "let's go",
            "follow me",
            "follow me back",
            "follow me here",
            "follow",
            "come",
            "come now",
            "come here",
            "come back",
            "come on",
            "come along",
            "come with me",
            "return",
            "return here",
            "return to me",
            "rejoin me",
            "rally"
        };

        /// <summary>Allowed filler tokens that may follow a recognised command without invalidating the intent.</summary>
        private static readonly HashSet<string> AllowedCommandSuffixTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "please",
            "now",
            "thanks",
            "thank",
            "you",
            "buddy",
            "pal",
            "friend",
            "mate",
            "chief",
            "champ",
            "bro",
            "bruv"
        };

        /// <summary>Action-specific stop commands that should only trigger when the companion is performing the mapped activity.</summary>
        private static readonly Dictionary<CompanionActiveAction, HashSet<string>> ActionSpecificStopCommands =
            new Dictionary<CompanionActiveAction, HashSet<string>>
            {
                {
                    CompanionActiveAction.Combat,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "stop combat",
                        "stop the combat",
                        "stop fighting",
                        "stop fight",
                        "stop attacking",
                        "stop attack",
                        "cease attack",
                        "cease fire",
                        "stand down",
                        "disengage",
                        "stop engaging",
                        "break off",
                        "fall back"
                    }
                },
                {
                    CompanionActiveAction.Fishing,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "stop fishing",
                        "stop the fishing",
                        "stop fish",
                        "stop catching",
                        "stop catching fish",
                        "stop casting",
                        "stop angling",
                        "stop net",
                        "stop harpoon"
                    }
                },
                {
                    CompanionActiveAction.Mining,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "stop mining",
                        "stop the mining",
                        "stop mine",
                        "stop smashing rocks",
                        "stop breaking rocks",
                        "stop rock",
                        "stop rocks",
                        "stop pickaxe",
                        "stop swinging pickaxe"
                    }
                },
                {
                    CompanionActiveAction.Woodcutting,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "stop woodcutting",
                        "stop the woodcutting",
                        "stop woodcut",
                        "stop cutting",
                        "stop cut",
                        "stop chopping",
                        "stop chop",
                        "stop chopping trees",
                        "stop cutting trees",
                        "stop logging",
                        "stop felling",
                        "stop wc"
                    }
                },
                {
                    CompanionActiveAction.Cooking,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "stop cooking",
                        "stop the cooking",
                        "stop cook",
                        "stop baking",
                        "stop making food",
                        "stop preparing food",
                        "stop meal",
                        "stop in the kitchen"
                    }
                }
            };

        private static readonly string[] CookingPhrases =
        {
            "start cooking", "go cook", "cook some food", "cook the meal",
            "prepare a meal", "make some food", "start baking", "start the cooking",
            "cook nearby", "cook something", "make dinner", "fire up the kitchen"
        };

        private static readonly HashSet<string> SkillTokens = BuildSkillTokenSet();

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
        /// Determines whether a chat line should cancel the specified companion action.
        /// </summary>
        /// <param name="action">Action the companion is currently performing.</param>
        /// <param name="message">Raw chat payload to evaluate.</param>
        /// <returns>True when the command should cancel the active action.</returns>
        public static bool TryHandleStopCommand(CompanionActiveAction action, string message)
        {
            if (action == CompanionActiveAction.None)
                return false;

            string normalised = NormaliseStopCommand(message);
            if (string.IsNullOrEmpty(normalised))
                return false;

            if (ActionSpecificStopCommands.TryGetValue(action, out var specificCommands) &&
                CommandMatches(normalised, specificCommands))
            {
                return true;
            }

            return CommandMatches(normalised, GlobalStopCommands);
        }

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

            if (CompanionConversationService.IsAwaitingSkillPlanResponse && LooksLikeInclusiveAgreement(tokens))
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

        /// <summary>
        /// Detects inclusive responses such as "let's go fishing" so they can be routed back to the
        /// conversation flow while the companion is waiting for a skill plan confirmation.
        /// </summary>
        private static bool LooksLikeInclusiveAgreement(IReadOnlyList<string> tokens)
        {
            if (tokens == null || tokens.Count == 0)
                return false;

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (!string.Equals(token, "lets", StringComparison.Ordinal) &&
                    !string.Equals(token, "let", StringComparison.Ordinal))
                {
                    continue;
                }

                for (int j = i + 1; j < tokens.Count; j++)
                {
                    string candidate = tokens[j];
                    if (SkillTokens.Contains(candidate))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds a lookup covering all recognised skilling tokens so inclusive phrases can be
        /// detected efficiently at runtime.
        /// </summary>
        private static HashSet<string> BuildSkillTokenSet()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);

            void AddRange(IEnumerable<string> source)
            {
                if (source == null)
                    return;

                foreach (var token in source)
                    set.Add(token);
            }

            AddRange(MiningVerbs);
            AddRange(MiningNouns);
            AddRange(FishingVerbs);
            AddRange(FishingNouns);
            AddRange(WoodcuttingVerbs);
            AddRange(WoodcuttingNouns);
            AddRange(CookingVerbs);
            AddRange(CookingNouns);

            return set;
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

        /// <summary>
        /// Normalises a free-form chat command by removing punctuation, collapsing whitespace, and lower-casing the content.
        /// </summary>
        private static string NormaliseStopCommand(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            bool previousWasSpace = false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (c == '\'' || c == '\u2019')
                    continue;

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
                    if (!previousWasSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// Determines whether the normalised command matches any entry in the supplied set, allowing polite suffixes.
        /// </summary>
        private static bool CommandMatches(string normalised, HashSet<string> commands)
        {
            if (commands == null || commands.Count == 0 || string.IsNullOrEmpty(normalised))
                return false;

            if (commands.Contains(normalised))
                return true;

            foreach (var command in commands)
            {
                if (!normalised.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (normalised.Length == command.Length)
                    return true;

                if (normalised[command.Length] != ' ')
                    continue;

                string suffix = normalised.Substring(command.Length + 1);
                if (IsAllowedSuffix(suffix))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Validates whether the suffix following a command only contains friendly filler tokens ("please", "now", etc.).
        /// </summary>
        private static bool IsAllowedSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return true;

            for (int i = 0; i < tokens.Length; i++)
            {
                if (!AllowedCommandSuffixTokens.Contains(tokens[i]))
                    return false;
            }

            return true;
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

            string playerName = chat.ActiveUsername;

            if (!CompanionManager.HasActiveCompanion)
            {
                chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                    CompanionChatLibrary.GetRandomMiningSummonRequiredLine(playerName));
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
                    chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                        CompanionChatLibrary.GetRandomMiningGenericFailureLine(playerName));
                    break;
            }
        }

        private static void PublishFishingFallback(CompanionFishingCommandResult failureReason)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;

            if (!CompanionManager.HasActiveCompanion)
            {
                chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                    CompanionChatLibrary.GetRandomFishingSummonRequiredLine(playerName));
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
                    chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                        CompanionFishingDialogueLibrary.GetRandomNoSpotsLine());
                    break;
                default:
                    chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                        CompanionChatLibrary.GetRandomFishingGenericFailureLine(playerName));
                    break;
            }
        }

        private static void PublishWoodcuttingFallback(CompanionWoodcuttingCommandResult failureReason)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;

            if (!CompanionManager.HasActiveCompanion)
            {
                chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                    CompanionChatLibrary.GetRandomWoodcuttingSummonRequiredLine(playerName));
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
                    chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                        CompanionChatLibrary.GetRandomWoodcuttingGenericFailureLine(playerName));
                    break;
            }
        }

        private static void PublishCookingFallback(CompanionCookingCommandResult failureReason)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;

            if (!CompanionManager.HasActiveCompanion)
            {
                chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                    CompanionChatLibrary.GetRandomCookingSummonRequiredLine(playerName));
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
                    chat.PublishCompanionMessage(CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                        CompanionChatLibrary.GetRandomCookingGenericFailureLine(playerName));
                    break;
            }
        }
    }
}
