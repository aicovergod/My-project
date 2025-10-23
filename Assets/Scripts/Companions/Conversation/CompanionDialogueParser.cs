using System;
using System.Collections.Generic;
using System.Linq;

namespace Companions.Conversation
{
    /// <summary>
    /// Parses normalised chat text, applies configured rules, and returns ordered intents so the
    /// conversation service can compose a response.
    /// </summary>
    public sealed class CompanionDialogueParser
    {
        private readonly List<CompanionDialogueRule> rules = new List<CompanionDialogueRule>();

        public CompanionDialogueParser(IEnumerable<CompanionDialogueRule> sourceRules)
        {
            if (sourceRules != null)
                rules.AddRange(sourceRules);
        }

        /// <summary>
        /// Tokenises the supplied text and evaluates all rules. Matching intents are ordered by the
        /// configured priority and the enum value to guarantee deterministic sequencing.
        /// </summary>
        public CompanionDialogueParseResult Parse(string normalisedText)
        {
            if (string.IsNullOrWhiteSpace(normalisedText))
                return CompanionDialogueParseResult.Empty;

            var tokens = Tokenise(normalisedText);
            if (tokens.Count == 0)
                return CompanionDialogueParseResult.Empty;

            var uniqueTokens = new HashSet<string>(tokens, StringComparer.Ordinal);
            var matches = new List<CompanionDialogueMatch>();

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null)
                    continue;

                if (!rule.Matches(uniqueTokens))
                    continue;

                matches.Add(new CompanionDialogueMatch(rule.Intent, rule.Priority));
            }

            if (matches.Count == 0)
                return new CompanionDialogueParseResult(tokens, uniqueTokens, Array.Empty<CompanionDialogueMatch>());

            matches.Sort(CompareMatches);
            return new CompanionDialogueParseResult(tokens, uniqueTokens, matches);
        }

        private static int CompareMatches(CompanionDialogueMatch x, CompanionDialogueMatch y)
        {
            int priorityCompare = x.Priority.CompareTo(y.Priority);
            if (priorityCompare != 0)
                return priorityCompare;

            return x.Intent.CompareTo(y.Intent);
        }

        private static List<string> Tokenise(string text)
        {
            var result = new List<string>();
            var buffer = new List<char>(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    Flush(buffer, result);
                    continue;
                }

                if (!char.IsLetterOrDigit(c))
                {
                    Flush(buffer, result);
                    continue;
                }

                buffer.Add(char.ToLowerInvariant(c));
            }

            Flush(buffer, result);
            return result;
        }

        private static void Flush(List<char> buffer, List<string> tokens)
        {
            if (buffer.Count == 0)
                return;

            string token = new string(buffer.ToArray());
            if (!string.IsNullOrEmpty(token))
                tokens.Add(token);

            buffer.Clear();
        }
    }

    /// <summary>
    /// Represents a single rule match and its ordering priority.
    /// </summary>
    public readonly struct CompanionDialogueMatch
    {
        public CompanionDialogueMatch(CompanionDialogueIntent intent, int priority)
        {
            Intent = intent;
            Priority = priority;
        }

        public CompanionDialogueIntent Intent { get; }

        public int Priority { get; }
    }

    /// <summary>
    /// Immutable representation of a parse result including tokens and ordered matches.
    /// </summary>
    public readonly struct CompanionDialogueParseResult
    {
        public static readonly CompanionDialogueParseResult Empty = new CompanionDialogueParseResult(
            Array.Empty<string>(),
            new HashSet<string>(),
            Array.Empty<CompanionDialogueMatch>());

        public CompanionDialogueParseResult(IReadOnlyList<string> tokens, IReadOnlyCollection<string> uniqueTokens,
            IReadOnlyList<CompanionDialogueMatch> matches)
        {
            Tokens = tokens ?? Array.Empty<string>();
            UniqueTokens = uniqueTokens ?? Array.Empty<string>();
            Matches = matches ?? Array.Empty<CompanionDialogueMatch>();
        }

        /// <summary>Ordered list of tokens extracted from the message.</summary>
        public IReadOnlyList<string> Tokens { get; }

        /// <summary>Unique token set for quick membership checks.</summary>
        public IReadOnlyCollection<string> UniqueTokens { get; }

        /// <summary>Intents that matched the configured rules.</summary>
        public IReadOnlyList<CompanionDialogueMatch> Matches { get; }

        /// <summary>True when no intents were matched.</summary>
        public bool IsEmpty => Matches.Count == 0;
    }
}
