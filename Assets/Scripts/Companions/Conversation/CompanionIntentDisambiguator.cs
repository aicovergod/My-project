using System;
using System.Collections.Generic;

namespace Companions.Conversation
{
    /// <summary>
    /// Provides intent pruning helpers that disambiguate overlapping conversational matches
    /// prior to the companion composing a response.
    /// </summary>
    internal static class CompanionIntentDisambiguator
    {
        /// <summary>
        /// Tokens that signal the player is addressing the companion directly.
        /// </summary>
        private static readonly HashSet<string> SecondPersonTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "you",
            "your",
            "yours",
            "yourself",
            "yourselves",
            "youre",
            "ur",
            "u",
            "ya"
        };

        /// <summary>
        /// Tokens that anchor praise statements even when pronouns are omitted.
        /// </summary>
        private static readonly HashSet<string> PraiseAnchorTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "job",
            "work",
            "effort",
            "skills",
            "skill",
            "performance",
            "help",
            "assist",
            "support"
        };

        /// <summary>
        /// Removes conflicting compliments when a greeting lacks second-person context so that the
        /// companion does not misinterpret generic salutations as praise.
        /// </summary>
        /// <param name="parseResult">Parse result returned by <see cref="CompanionDialogueParser"/>.</param>
        internal static CompanionDialogueParseResult PruneContradictoryIntents(CompanionDialogueParseResult parseResult)
        {
            if (parseResult.IsEmpty)
                return parseResult;

            bool hasGreeting = false;
            bool hasCompliment = false;

            for (int i = 0; i < parseResult.Matches.Count; i++)
            {
                var intent = parseResult.Matches[i].Intent;
                if (intent == CompanionDialogueIntent.Greeting)
                    hasGreeting = true;
                else if (intent == CompanionDialogueIntent.Compliment)
                    hasCompliment = true;

                if (hasGreeting && hasCompliment)
                    break;
            }

            if (!hasGreeting || !hasCompliment)
                return parseResult;

            if (ContainsSecondPersonOrPraise(parseResult.Tokens))
                return parseResult;

            var filteredMatches = new List<CompanionDialogueMatch>(parseResult.Matches.Count);
            for (int i = 0; i < parseResult.Matches.Count; i++)
            {
                var match = parseResult.Matches[i];
                if (match.Intent == CompanionDialogueIntent.Compliment)
                    continue;

                filteredMatches.Add(match);
            }

            if (filteredMatches.Count == parseResult.Matches.Count)
                return parseResult;

            return new CompanionDialogueParseResult(parseResult.Tokens, parseResult.UniqueTokens, filteredMatches);
        }

        private static bool ContainsSecondPersonOrPraise(IReadOnlyList<string> tokens)
        {
            if (tokens == null || tokens.Count == 0)
                return false;

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrEmpty(token))
                    continue;

                if (SecondPersonTokens.Contains(token))
                    return true;

                if (PraiseAnchorTokens.Contains(token))
                    return true;
            }

            return false;
        }
    }
}
