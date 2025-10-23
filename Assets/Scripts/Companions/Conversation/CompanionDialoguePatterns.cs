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
            "\\b(can|could|will|would|please|mind)\\s+(you|ya)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RequestAssistanceHandRegex = new Regex(
            "\\b(give|lend)\\s+me\\s+(a\\s+)?hand\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StatusQueryContractionRegex = new Regex(
            "\\bhow['’]?(s|re)\\s+(it|everything|life|ya|you|things)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StatusQueryYouGoodRegex = new Regex(
            "\\b(you|ya)\\s*(ok|okay|alright|good)\\s*\\?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StatusQueryWhatsUpRegex = new Regex(
            "\\bwhat['’]?s\\s+up\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StatusQueryQuestionSuffixRegex = new Regex(
            "\\b(you|ya)\\b[^?]*\\?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RecentEventRegex = new Regex(
            "\\b(remind|remember|recall)\\b.*\\b(earlier|before|last|previous|yesterday|today)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string[] PlayerMoodTokens =
        {
            "tired", "sleepy", "exhausted", "drained", "burnt", "burned", "burntout", "sad", "blue", "down",
            "angry", "upset", "annoyed", "frustrated", "stressed", "anxious", "overwhelmed", "confused", "worried",
            "good", "great", "awesome", "amazing", "fantastic", "wonderful", "okay", "ok", "fine", "happy",
            "excited", "pumped", "stoked", "chill", "relaxed", "content", "energised", "energized"
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
                        new SynonymBucket(new[] { "hello", "hi", "hey", "heya", "hiya", "greetings", "yo", "sup", "salutations", "hola", "howdy", "ahoy" }),
                        new SynonymBucket(new[] { "morning", "afternoon", "evening", "night" }, 0.75f),
                        new SynonymBucket(new[] { "nice", "good", "lovely", "bright" }, 0.5f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("good morning", 1.2f),
                        new MultiWordPhrase("good evening", 1.2f),
                        new MultiWordPhrase("good afternoon", 1.2f),
                        new MultiWordPhrase("nice to see you", 1.4f),
                        new MultiWordPhrase("hey there", 1.3f),
                        new MultiWordPhrase("yo there", 1.2f),
                        new MultiWordPhrase("howdy friend", 1.3f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.StatusQuery,
                    priority: 5,
                    matchThreshold: 2.2f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "how", "hows", "how's", "howre", "how're", "howve", "how've", "what's", "whats", "sup" }, 1.1f),
                        new SynonymBucket(new[] { "you", "ya", "yall" }),
                        new SynonymBucket(new[] { "doing", "feeling", "going", "holding", "hangin", "hanging", "are", "been" }, 0.9f),
                        new SynonymBucket(new[] { "ok", "okay", "alright", "good", "chilling", "chillin" }, 0.7f),
                        new SynonymBucket(new[] { "today", "tonight", "lately", "there" }, 0.5f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("how are you", 1.6f),
                        new MultiWordPhrase("how you doing", 1.6f),
                        new MultiWordPhrase("how's it going", 1.8f),
                        new MultiWordPhrase("how've you been", 1.8f),
                        new MultiWordPhrase("what's up", 1.7f),
                        new MultiWordPhrase("how ya holding", 1.6f),
                        new MultiWordPhrase("you doing ok", 1.5f),
                        new MultiWordPhrase("you doing okay", 1.5f),
                        new MultiWordPhrase("you good", 1.5f),
                        new MultiWordPhrase("everything alright", 1.6f)
                    },
                    regexScoreEvaluator: text =>
                    {
                        float score = 0f;
                        if (StatusQueryContractionRegex.IsMatch(text))
                            score += 0.9f;
                        if (StatusQueryYouGoodRegex.IsMatch(text))
                            score += 0.8f;
                        if (StatusQueryWhatsUpRegex.IsMatch(text))
                            score += 0.6f;
                        if (StatusQueryQuestionSuffixRegex.IsMatch(text))
                            score += 0.5f;
                        return score;
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.PlayerMoodReport,
                    priority: 8,
                    matchThreshold: 2.2f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "im", "i'm", "iam", "ive", "i've", "imma", "feeling", "feel", "feelin" }),
                        new SynonymBucket(PlayerMoodTokens, 1.2f),
                        new SynonymBucket(new[] { "kinda", "sorta", "really", "super", "so" }, 0.6f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("i feel", 1.1f),
                        new MultiWordPhrase("i'm feeling", 1.4f),
                        new MultiWordPhrase("feeling kind of", 1.4f),
                        new MultiWordPhrase("feeling kinda", 1.4f),
                        new MultiWordPhrase("i feel like", 1.5f),
                        new MultiWordPhrase("i'm good", 1.5f),
                        new MultiWordPhrase("i'm so", 1.2f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.Gratitude,
                    priority: 12,
                    matchThreshold: 1.6f,
                    synonymBuckets: new[]
                    {
                        // Gratitude keywords should succeed on their own ("thanks!") so the bucket weight
                        // meets the match threshold while leaving secondary context buckets optional.
                        new SynonymBucket(new[] { "thanks", "thank", "appreciate", "appreciated", "cheers", "thx", "ty" }, 1.6f),
                        new SynonymBucket(new[] { "buddy", "friend", "pal", "partner", "legend", "champ" }, 0.6f),
                        new SynonymBucket(new[] { "much", "really" }, 0.5f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("thank you", 1.5f),
                        new MultiWordPhrase("thanks for", 1.3f),
                        new MultiWordPhrase("thanks a bunch", 1.5f),
                        new MultiWordPhrase("thanks a ton", 1.5f),
                        new MultiWordPhrase("really appreciate it", 1.6f),
                        new MultiWordPhrase("much appreciated", 1.6f)
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
                        new SynonymBucket(new[] { "bye", "goodbye", "farewell", "later", "laters", "laterz", "cya", "seeya", "ciao", "peace" }, 1.4f),
                        new SynonymBucket(new[] { "soon", "later", "out" }, 0.6f),
                        new SynonymBucket(new[] { "gotta", "gonna", "im", "i'm" }, 0.5f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("see you", 1.5f),
                        new MultiWordPhrase("catch you", 1.5f),
                        new MultiWordPhrase("see ya", 1.4f),
                        new MultiWordPhrase("catch ya later", 1.6f),
                        new MultiWordPhrase("gotta go", 1.6f),
                        new MultiWordPhrase("i'm off", 1.4f),
                        new MultiWordPhrase("peace out", 1.5f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.Compliment,
                    priority: 25,
                    matchThreshold: 1.8f,
                    synonymBuckets: new[]
                    {
                        // Single-word praise such as "awesome!" should hit the compliment intent, so the
                        // bucket weight now clears the threshold by itself.
                        new SynonymBucket(new[] { "good", "great", "awesome", "amazing", "nice", "solid", "brilliant", "fantastic", "stellar", "dope", "slick" }, 1.8f),
                        new SynonymBucket(new[] { "job", "work", "partner", "friend", "assist", "move", "play" }, 0.8f),
                        new SynonymBucket(new[] { "legend", "goat", "rockstar", "lifesaver" }, 0.9f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("good job", 1.7f),
                        new MultiWordPhrase("great work", 1.7f),
                        new MultiWordPhrase("awesome job", 1.7f),
                        new MultiWordPhrase("nice work", 1.6f),
                        new MultiWordPhrase("you're amazing", 1.8f),
                        new MultiWordPhrase("you're the best", 1.9f)
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.RequestAssistance,
                    priority: 30,
                    matchThreshold: 1.8f,
                    synonymBuckets: new[]
                    {
                        // Urgent single-word cries ("help!") must succeed, so increase the weight to meet
                        // the threshold while still allowing regex/context bonuses to stack naturally.
                        new SynonymBucket(new[] { "help", "assist", "cover", "watch", "support", "backup", "aid" }, 1.8f),
                        new SynonymBucket(new[] { "need", "could", "can", "ya", "you", "lend", "mind" }, 0.7f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("help me", 1.6f),
                        new MultiWordPhrase("can you help", 1.8f),
                        new MultiWordPhrase("can ya help", 1.8f),
                        new MultiWordPhrase("could you help", 1.8f),
                        new MultiWordPhrase("could ya help", 1.8f),
                        new MultiWordPhrase("need a hand", 1.6f),
                        new MultiWordPhrase("give me a hand", 1.7f),
                        new MultiWordPhrase("watch my back", 1.5f),
                        new MultiWordPhrase("cover me", 1.4f)
                    },
                    regexScoreEvaluator: text =>
                    {
                        float score = 0f;
                        if (RequestAssistanceRegex.IsMatch(text))
                            score += 1f;
                        if (RequestAssistanceHandRegex.IsMatch(text))
                            score += 0.8f;
                        return score;
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.AcknowledgeRecentEvent,
                    priority: 35,
                    matchThreshold: 1.6f,
                    synonymBuckets: new[]
                    {
                        // Only the strong recall verbs should clear the match threshold; supporting context
                        // tokens now live in low-weight buckets so they merely amplify an existing recall hit.
                        new SynonymBucket(new[] { "remember", "remind", "recall" }, 1.6f),
                        new SynonymBucket(new[] { "about", "that", "when", "where" }, 0.4f),
                        new SynonymBucket(new[] { "earlier", "before", "last", "previous", "yesterday", "today" }, 0.4f),
                        new SynonymBucket(new[] { "fight", "battle", "event", "thing", "moment", "run", "quest", "mission" }, 0.8f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("that earlier", 1.4f),
                        new MultiWordPhrase("remember that", 1.4f),
                        new MultiWordPhrase("remember when", 1.6f),
                        new MultiWordPhrase("remember when we", 1.7f),
                        new MultiWordPhrase("that last fight", 1.6f),
                        new MultiWordPhrase("earlier today", 1.4f),
                        new MultiWordPhrase("last time we", 1.6f)
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
