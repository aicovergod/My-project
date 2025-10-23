using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Companions.Conversation
{
    /// <summary>
    /// Provides the canonical set of dialogue intent patterns used to create
    /// <see cref="CompanionDialogueRule"/> instances at runtime.
    /// </summary>
    public static class CompanionDialoguePatterns
    {
        private static readonly Regex RequestAssistanceRegex = new Regex(
            "\\b(can|could|will|would|please)\\s+you\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RecentEventRegex = new Regex(
            "\\b(remind|remember)\\b.*\\b(earlier|before|last|previous)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string[] PlayerMoodTokens =
        {
            "tired", "sleepy", "exhausted", "drained", "sad", "angry", "upset", "annoyed", "frustrated",
            "good", "great", "awesome", "okay", "ok", "fine", "happy", "excited", "pumped", "nervous", "worried"
        };

        /// <summary>
        /// Creates the default dialogue profile composed of weighted rules for every supported intent.
        /// </summary>
        public static IReadOnlyList<CompanionDialogueRule> CreateDefaultProfile()
        {
            var patterns = new List<CompanionIntentPattern>
            {
                new CompanionIntentPattern(
                    CompanionDialogueIntent.Greeting,
                    priority: 0,
                    matchThreshold: 1f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "hello", "hi", "hey", "greetings", "yo", "sup", "salutations", "hola" }),
                        new SynonymBucket(new[] { "morning", "afternoon", "evening" }, 0.75f),
                        new SynonymBucket(new[] { "nice", "good" }, 0.5f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("good morning", 1.2f),
                        new MultiWordPhrase("good evening", 1.2f),
                        new MultiWordPhrase("nice to see you", 1.4f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.StatusQuery,
                    priority: 5,
                    matchThreshold: 2.4f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "how", "hows", "howre" }),
                        new SynonymBucket(new[] { "you", "ya" }),
                        new SynonymBucket(new[] { "doing", "feeling", "going", "are" }, 0.9f),
                        new SynonymBucket(new[] { "today", "tonight" }, 0.6f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("how are you", 1.6f),
                        new MultiWordPhrase("how you doing", 1.6f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.PlayerMoodReport,
                    priority: 8,
                    matchThreshold: 2.2f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "im", "iam", "imma", "feeling", "feel" }),
                        new SynonymBucket(PlayerMoodTokens, 1.2f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("i feel", 1.1f),
                        new MultiWordPhrase("feeling kind of", 1.4f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.Gratitude,
                    priority: 12,
                    matchThreshold: 1.6f,
                    synonymBuckets: new[]
                    {
                        // Gratitude keywords should succeed on their own ("thanks!") so the bucket weight
                        // meets the match threshold while leaving secondary context buckets optional.
                        new SynonymBucket(new[] { "thanks", "thank", "appreciate", "cheers" }, 1.6f),
                        new SynonymBucket(new[] { "buddy", "friend", "pal", "partner" }, 0.6f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("thank you", 1.5f),
                        new MultiWordPhrase("thanks for", 1.3f)
                    },
                    negativeKeywords: new[] { "nothing" }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.Farewell,
                    priority: 20,
                    matchThreshold: 1.4f,
                    synonymBuckets: new[]
                    {
                        // Farewells like "bye" should register immediately, so the primary bucket now
                        // satisfies the threshold without requiring additional context tokens.
                        new SynonymBucket(new[] { "bye", "goodbye", "farewell", "later", "cya", "seeya" }, 1.4f),
                        new SynonymBucket(new[] { "soon", "later" }, 0.6f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("see you", 1.5f),
                        new MultiWordPhrase("catch you", 1.5f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.Compliment,
                    priority: 25,
                    matchThreshold: 1.8f,
                    synonymBuckets: new[]
                    {
                        // Single-word praise such as "awesome!" should hit the compliment intent, so the
                        // bucket weight now clears the threshold by itself.
                        new SynonymBucket(new[] { "good", "great", "awesome", "amazing", "nice", "solid", "brilliant" }, 1.8f),
                        new SynonymBucket(new[] { "job", "work", "partner", "friend", "assist" }, 0.8f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("good job", 1.7f),
                        new MultiWordPhrase("great work", 1.7f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.RequestAssistance,
                    priority: 30,
                    matchThreshold: 1.8f,
                    synonymBuckets: new[]
                    {
                        // Urgent single-word cries ("help!") must succeed, so increase the weight to meet
                        // the threshold while still allowing regex/context bonuses to stack naturally.
                        new SynonymBucket(new[] { "help", "assist", "cover", "watch", "support" }, 1.8f),
                        new SynonymBucket(new[] { "need", "could", "can" }, 0.7f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("help me", 1.6f),
                        new MultiWordPhrase("can you help", 1.8f)
                    },
                    regexScoreEvaluator: text => RequestAssistanceRegex.IsMatch(text) ? 1f : 0f),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.AcknowledgeRecentEvent,
                    priority: 35,
                    matchThreshold: 1.6f,
                    synonymBuckets: new[]
                    {
                        // Only the strong recall verbs should clear the match threshold; supporting context
                        // tokens now live in low-weight buckets so they merely amplify an existing recall hit.
                        new SynonymBucket(new[] { "remember", "remind" }, 1.6f),
                        new SynonymBucket(new[] { "about", "that" }, 0.4f),
                        new SynonymBucket(new[] { "earlier", "before", "last", "previous" }, 0.4f),
                        new SynonymBucket(new[] { "fight", "battle", "event", "thing", "moment", "run" }, 0.8f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("that earlier", 1.4f),
                        new MultiWordPhrase("remember that", 1.4f)
                    },
                    regexScoreEvaluator: text => RecentEventRegex.IsMatch(text) ? 0.8f : 0f)
            };

            var rules = new List<CompanionDialogueRule>(patterns.Count);
            for (int i = 0; i < patterns.Count; i++)
            {
                rules.Add(CompanionDialogueRule.FromPattern(patterns[i]));
            }

            return rules;
        }
    }

    /// <summary>
    /// Describes the weighted keyword and phrase data used to construct a <see cref="CompanionDialogueRule"/>.
    /// </summary>
    public readonly struct CompanionIntentPattern
    {
        public CompanionIntentPattern(
            CompanionDialogueIntent intent,
            int priority,
            float matchThreshold,
            IReadOnlyList<SynonymBucket> synonymBuckets,
            IReadOnlyList<MultiWordPhrase> multiWordPhrases,
            Func<string, float> regexScoreEvaluator = null,
            IReadOnlyList<string> negativeKeywords = null)
        {
            Intent = intent;
            Priority = priority;
            MatchThreshold = Mathf.Max(0f, matchThreshold);
            SynonymBuckets = synonymBuckets ?? Array.Empty<SynonymBucket>();
            MultiWordPhrases = multiWordPhrases ?? Array.Empty<MultiWordPhrase>();
            RegexScoreEvaluator = regexScoreEvaluator;
            NegativeKeywords = negativeKeywords ?? Array.Empty<string>();
        }

        public CompanionDialogueIntent Intent { get; }

        public int Priority { get; }

        public float MatchThreshold { get; }

        public IReadOnlyList<SynonymBucket> SynonymBuckets { get; }

        public IReadOnlyList<MultiWordPhrase> MultiWordPhrases { get; }

        public Func<string, float> RegexScoreEvaluator { get; }

        public IReadOnlyList<string> NegativeKeywords { get; }
    }

    /// <summary>
    /// Represents a group of synonym tokens and the score contributed when the player uses any of them.
    /// </summary>
    public readonly struct SynonymBucket
    {
        public SynonymBucket(IEnumerable<string> tokens, float weight = 1f)
        {
            Weight = Mathf.Max(0f, weight);
            Tokens = tokens?.Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Tokens { get; }

        public float Weight { get; }
    }

    /// <summary>
    /// Represents a multi-word phrase that contributes additional score when detected in the chat line.
    /// </summary>
    public readonly struct MultiWordPhrase
    {
        public MultiWordPhrase(string phrase, float weight = 1.5f)
        {
            Phrase = string.IsNullOrWhiteSpace(phrase)
                ? string.Empty
                : phrase.Trim().ToLowerInvariant();
            Weight = Mathf.Max(0f, weight);
        }

        public string Phrase { get; }

        public float Weight { get; }
    }
}
