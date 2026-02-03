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

            var tokenisation = Tokenise(normalisedText);
            if (tokenisation.Tokens.Count == 0)
                return CompanionDialogueParseResult.Empty;

            var tokens = tokenisation.Tokens;
            var uniqueTokens = tokenisation.UniqueTokens;
            var matches = new List<CompanionDialogueMatch>();

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null)
                    continue;

                if (!rule.TryEvaluate(uniqueTokens, normalisedText, out float score))
                    continue;

                matches.Add(new CompanionDialogueMatch(rule.Intent, rule.Priority, score));
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

            int scoreCompare = y.Score.CompareTo(x.Score);
            if (scoreCompare != 0)
                return scoreCompare;

            return x.Intent.CompareTo(y.Intent);
        }

        private static TokenisationResult Tokenise(string text)
        {
            var tokens = new List<string>();
            var uniqueTokens = new HashSet<string>(StringComparer.Ordinal);
            var buffer = new List<char>(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    Flush(buffer, tokens, uniqueTokens);
                    continue;
                }

                if (!char.IsLetterOrDigit(c))
                {
                    Flush(buffer, tokens, uniqueTokens);
                    continue;
                }

                buffer.Add(char.ToLowerInvariant(c));
            }

            Flush(buffer, tokens, uniqueTokens);
            return new TokenisationResult(tokens, uniqueTokens);
        }

        private static void Flush(List<char> buffer, List<string> tokens, HashSet<string> uniqueTokens)
        {
            if (buffer.Count == 0)
                return;

            string token = new string(buffer.ToArray());
            if (!string.IsNullOrEmpty(token))
                AddToken(token, tokens, uniqueTokens);

            buffer.Clear();
        }

        private static void AddToken(string token, List<string> tokens, HashSet<string> uniqueTokens)
        {
            if (string.IsNullOrEmpty(token))
                return;

            tokens.Add(token);
            uniqueTokens.Add(token);

            string stem = StemToken(token);
            if (!string.IsNullOrEmpty(stem))
                uniqueTokens.Add(stem);
        }

        private static string StemToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            string result = token;

            if (result.Length > 4 && result.EndsWith("ing", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 3);
            else if (result.Length > 5 && result.EndsWith("ness", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 4);
            else if (result.Length > 4 && result.EndsWith("ly", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 2);
            else if (result.Length > 3 && result.EndsWith("ed", StringComparison.Ordinal))
                result = result.Substring(0, result.Length - 2);

            if (result.Length < 2 || string.Equals(result, token, StringComparison.Ordinal))
                return string.Empty;

            return result;
        }

        private readonly struct TokenisationResult
        {
            public TokenisationResult(List<string> tokens, HashSet<string> uniqueTokens)
            {
                Tokens = tokens ?? new List<string>();
                UniqueTokens = uniqueTokens ?? new HashSet<string>(StringComparer.Ordinal);
            }

            public List<string> Tokens { get; }

            public HashSet<string> UniqueTokens { get; }
        }
    }

    /// <summary>
    /// Represents a single rule match and its ordering priority.
    /// </summary>
    public readonly struct CompanionDialogueMatch
    {
        public CompanionDialogueMatch(CompanionDialogueIntent intent, int priority, float score)
        {
            Intent = intent;
            Priority = priority;
            Score = score;
        }

        public CompanionDialogueIntent Intent { get; }

        public int Priority { get; }

        public float Score { get; }
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
