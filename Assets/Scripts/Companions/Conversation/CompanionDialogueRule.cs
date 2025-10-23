using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Companions.Conversation
{
    /// <summary>
    /// Describes the keyword requirements that trigger a specific <see cref="CompanionDialogueIntent"/>.
    /// Each rule contains one or more keyword sets – at least one token from every set must appear in the
    /// analysed message while none of the negative keywords may be present.
    /// </summary>
    [Serializable]
    public sealed class CompanionDialogueRule
    {
        [SerializeField]
        private CompanionDialogueIntent intent;

        [SerializeField, Tooltip("Lower priority values are evaluated first when composing responses.")]
        private int priority = 10;

        [SerializeField, Tooltip("Keyword sets that must be satisfied. At least one token from each set must be present.")]
        private List<KeywordSet> keywordSets = new List<KeywordSet>();

        [SerializeField, Tooltip("Tokens that must NOT be present for this rule to match.")]
        private List<string> negativeKeywords = new List<string>();

        /// <summary>Intent emitted when the rule evaluates to true.</summary>
        public CompanionDialogueIntent Intent => intent;

        /// <summary>Relative priority applied when ordering intents during response composition.</summary>
        public int Priority => priority;

        /// <summary>Collection of keyword sets that must all be satisfied for the rule to match.</summary>
        public IReadOnlyList<KeywordSet> KeywordSets => keywordSets;

        /// <summary>Tokens that prevent the rule from matching when encountered.</summary>
        public IReadOnlyList<string> NegativeKeywords => negativeKeywords;

        /// <summary>
        /// Determines whether the rule matches the provided token set.
        /// </summary>
        /// <param name="tokens">Token set generated from the normalised input text.</param>
        public bool Matches(IReadOnlyCollection<string> tokens)
        {
            if (keywordSets == null || keywordSets.Count == 0)
                return false;

            for (int i = 0; i < keywordSets.Count; i++)
            {
                if (!keywordSets[i].Matches(tokens))
                    return false;
            }

            if (negativeKeywords != null)
            {
                foreach (string raw in negativeKeywords)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    string keyword = raw.Trim().ToLowerInvariant();
                    if (keyword.Length == 0)
                        continue;

                    if (TokenSetContains(tokens, keyword))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Helper used by factories/tests to populate the keyword set list.
        /// </summary>
        /// <param name="keywordGroups">Array of keyword groups where at least one entry per group must be present.</param>
        public void SetKeywordGroups(params IEnumerable<string>[] keywordGroups)
        {
            keywordSets = new List<KeywordSet>(keywordGroups?.Length ?? 0);
            if (keywordGroups == null)
                return;

            for (int i = 0; i < keywordGroups.Length; i++)
                keywordSets.Add(new KeywordSet(keywordGroups[i] ?? Array.Empty<string>()));
        }

        /// <summary>
        /// Factory helper that creates a rule pre-populated with keyword groups and optional negative tokens.
        /// </summary>
        public static CompanionDialogueRule Create(CompanionDialogueIntent intent, int priority, IEnumerable<string>[] keywordGroups, IEnumerable<string> negative = null)
        {
            var rule = new CompanionDialogueRule
            {
                intent = intent,
                priority = priority,
                keywordSets = new List<KeywordSet>()
            };

            if (keywordGroups != null)
            {
                for (int i = 0; i < keywordGroups.Length; i++)
                    rule.keywordSets.Add(new KeywordSet(keywordGroups[i] ?? Array.Empty<string>()));
            }

            if (negative != null)
                rule.negativeKeywords = negative.Select(k => (k ?? string.Empty).Trim().ToLowerInvariant()).ToList();

            return rule;
        }

        private static bool TokenSetContains(IEnumerable<string> tokens, string keyword)
        {
            foreach (string token in tokens)
            {
                if (string.Equals(token, keyword, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Represents a group of keywords where the rule succeeds if at least one keyword is present in the analysed tokens.
        /// </summary>
        [Serializable]
        public sealed class KeywordSet
        {
            [SerializeField]
            private List<string> keywords = new List<string>();

            public KeywordSet()
            {
            }

            public KeywordSet(IEnumerable<string> values)
            {
                keywords = values?.Select(NormaliseToken).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList()
                           ?? new List<string>();
            }

            /// <summary>True when the analysed tokens contain at least one keyword from the set.</summary>
            public bool Matches(IReadOnlyCollection<string> tokens)
            {
                if (keywords == null || keywords.Count == 0)
                    return false;

                foreach (string candidate in keywords)
                {
                    if (TokenSetContains(tokens, candidate))
                        return true;
                }

                return false;
            }

            private static string NormaliseToken(string token)
            {
                if (string.IsNullOrWhiteSpace(token))
                    return string.Empty;

                return token.Trim().ToLowerInvariant();
            }
        }
    }
}
