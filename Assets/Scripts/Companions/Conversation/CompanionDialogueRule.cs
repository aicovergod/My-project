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

        [SerializeField, Tooltip("Keyword buckets that contribute score when at least one synonym is present.")]
        private List<KeywordSet> keywordSets = new List<KeywordSet>();

        [SerializeField, Tooltip("Optional multi-word phrases that add to the rule score when matched.")]
        private List<PhrasePattern> phrasePatterns = new List<PhrasePattern>();

        [SerializeField, Tooltip("Tokens that must NOT be present for this rule to match.")]
        private List<string> negativeKeywords = new List<string>();

        [SerializeField, Tooltip("Minimum score required before the rule reports a match.")]
        private float matchThreshold = 1f;

        [NonSerialized]
        private Func<string, float> regexScoreEvaluator;

        /// <summary>Intent emitted when the rule evaluates to true.</summary>
        public CompanionDialogueIntent Intent => intent;

        /// <summary>Relative priority applied when ordering intents during response composition.</summary>
        public int Priority => priority;

        /// <summary>Collection of keyword sets that contribute score to the rule.</summary>
        public IReadOnlyList<KeywordSet> KeywordSets => keywordSets;

        /// <summary>Tokens that prevent the rule from matching when encountered.</summary>
        public IReadOnlyList<string> NegativeKeywords => negativeKeywords;

        /// <summary>Optional regex driven evaluator assigned by code-based profiles.</summary>
        public Func<string, float> RegexScoreEvaluator
        {
            get => regexScoreEvaluator;
            set => regexScoreEvaluator = value;
        }

        /// <summary>Score required for the rule to register a match.</summary>
        public float MatchThreshold => matchThreshold;

        /// <summary>Overrides the score threshold used when evaluating the rule.</summary>
        public void OverrideMatchThreshold(float threshold)
        {
            matchThreshold = Mathf.Max(0f, threshold);
        }

        /// <summary>
        /// Determines whether the rule matches the provided token set and returns the calculated score.
        /// </summary>
        /// <param name="tokens">Token set generated from the normalised input text.</param>
        /// <param name="normalisedText">Normalised chat text used for multi-word phrase evaluation.</param>
        /// <param name="score">Total score accumulated by the rule.</param>
        public bool TryEvaluate(IReadOnlyCollection<string> tokens, string normalisedText, out float score)
        {
            score = 0f;

            float totalScore = 0f;

            if (keywordSets != null)
            {
                for (int i = 0; i < keywordSets.Count; i++)
                {
                    totalScore += keywordSets[i].CalculateScore(tokens);
                }
            }

            if (phrasePatterns != null && !string.IsNullOrEmpty(normalisedText))
            {
                for (int i = 0; i < phrasePatterns.Count; i++)
                    totalScore += phrasePatterns[i].CalculateScore(normalisedText);
            }

            if (regexScoreEvaluator != null && !string.IsNullOrEmpty(normalisedText))
                totalScore += Mathf.Max(0f, regexScoreEvaluator(normalisedText));

            if (negativeKeywords != null && tokens != null)
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

            score = totalScore;
            return totalScore >= Mathf.Max(0f, matchThreshold);
        }

        /// <summary>
        /// Legacy helper retained for existing unit tests. Uses the rule score with a blank text payload.
        /// </summary>
        public bool Matches(IReadOnlyCollection<string> tokens)
        {
            return TryEvaluate(tokens, string.Empty, out _);
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

            matchThreshold = keywordSets.Count;
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

            rule.matchThreshold = Math.Max(0f, rule.keywordSets.Count);

            return rule;
        }

        /// <summary>
        /// Factory helper used by <see cref="CompanionDialoguePatterns"/> when building runtime profiles.
        /// </summary>
        public static CompanionDialogueRule FromPattern(CompanionIntentPattern pattern)
        {
            var rule = new CompanionDialogueRule
            {
                intent = pattern.Intent,
                priority = pattern.Priority,
                keywordSets = new List<KeywordSet>(pattern.SynonymBuckets.Count),
                phrasePatterns = new List<PhrasePattern>(pattern.MultiWordPhrases.Count),
                negativeKeywords = pattern.NegativeKeywords.Select(k => (k ?? string.Empty).Trim().ToLowerInvariant()).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
                matchThreshold = Mathf.Max(0f, pattern.MatchThreshold)
            };

            for (int i = 0; i < pattern.SynonymBuckets.Count; i++)
            {
                var bucket = pattern.SynonymBuckets[i];
                rule.keywordSets.Add(new KeywordSet(bucket.Tokens, bucket.Weight));
            }

            for (int i = 0; i < pattern.MultiWordPhrases.Count; i++)
            {
                var phrase = pattern.MultiWordPhrases[i];
                rule.phrasePatterns.Add(new PhrasePattern(phrase.Phrase, phrase.Weight));
            }

            rule.regexScoreEvaluator = pattern.RegexScoreEvaluator;
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

            [SerializeField]
            private float weight = 1f;

            public KeywordSet()
            {
            }

            public KeywordSet(IEnumerable<string> values)
                : this(values, 1f)
            {
            }

            public KeywordSet(IEnumerable<string> values, float weight)
            {
                keywords = values?.Select(NormaliseToken).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList()
                           ?? new List<string>();
                this.weight = Mathf.Max(0f, weight);
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

            /// <summary>Calculates the score contributed by this bucket.</summary>
            public float CalculateScore(IReadOnlyCollection<string> tokens)
            {
                return Matches(tokens) ? Mathf.Max(0f, weight) : 0f;
            }

            private static string NormaliseToken(string token)
            {
                if (string.IsNullOrWhiteSpace(token))
                    return string.Empty;

                return token.Trim().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Represents a multi-word phrase that contributes score when present in the analysed string.
        /// </summary>
        [Serializable]
        public sealed class PhrasePattern
        {
            [SerializeField]
            private string phrase = string.Empty;

            [SerializeField]
            private float weight = 1.5f;

            public PhrasePattern()
            {
            }

            public PhrasePattern(string phrase, float weight)
            {
                this.phrase = string.IsNullOrWhiteSpace(phrase) ? string.Empty : phrase.Trim().ToLowerInvariant();
                this.weight = Mathf.Max(0f, weight);
            }

            public float CalculateScore(string text)
            {
                if (string.IsNullOrEmpty(phrase) || string.IsNullOrEmpty(text))
                    return 0f;

                return text.IndexOf(phrase, StringComparison.Ordinal) >= 0 ? Mathf.Max(0f, weight) : 0f;
            }
        }
    }
}
