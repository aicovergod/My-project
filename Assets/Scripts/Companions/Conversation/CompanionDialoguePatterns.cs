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
        private static readonly Regex SuggestionQuestionRegex = new Regex(
            "\\bwhat\\s+(do|would)\\s+(you|ya)\\s+(want|wanna|like)\\s+(to\\s+)?(train|do)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex SuggestionReminderRegex = new Regex(
            "\\b(remind|reminder|again|forgot|forget)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            "\\b(you|ya|u)\\b[^?]*\\?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StatusQueryShorthandRegex = new Regex(
            "\\b(hru|hbu|wbu|howru|supu|supya|supyou|howya)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RecentEventRegex = new Regex(
            "\\b(remind|remember|recall)\\b.*\\b(earlier|before|last|previous|yesterday|today)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex SkillKeywordRegex = new Regex(
            "\\b(mine|mining|miner|mines|wood|woodcut|woodcutting|wc|lumber|chop|logs|fish|fishing|fishin|rod|harpoon|cook|cooking|cookin|chef|firemaking|firemake|fire|burn|fm|smith|smithing|smelt|craft|crafting|magic|mage|wizard|wiz|sorc|range|ranged|rng|archer|archery|attack|atk|strength|str|defence|defense|def|hp|hitpoint|hitpoints|lifepoint|lifepoints|health|vitality|beast|beasts|beastmaster|pet|pets)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex SkillLevelQueryRegex = new Regex(
            "\\b(what|whats|what's|waht|wats|wut|wat)\\s*(is|s|'s)?\\s*(ya|your|ur|you|yours?)\\b.*\\b(level|levels|lvl|lvls|levl|levll)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex SkillLevelCompactRegex = new Regex(
            "\\b(whats|what's|waht|wats)(ya|ur|your)([a-z]{2,})(level|lvl|lv|levl|levll)\\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string[] SkillKeywordTokens =
        {
            "mine", "mining", "miner", "mines", "wood", "woodcut", "woodcutting", "wc", "lumber", "chop", "logs", "fish", "fishing", "fishin", "rod", "harpoon", "cook", "cooking", "cookin", "chef", "firemaking", "firemake", "fire", "burn", "fm", "smith", "smithing", "smelt", "craft", "crafting", "magic", "mage", "wizard", "wiz", "sorc", "range", "ranged", "rng", "archer", "archery", "attack", "atk", "strength", "str", "defence", "defense", "def", "hp", "hitpoint", "hitpoints", "lifepoint", "lifepoints", "health", "vitality", "beast", "beasts", "beastmaster", "pet", "pets"
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
                        // Common casual greetings
                        new MultiWordPhrase("good morning", 1.2f),
                        new MultiWordPhrase("good evening", 1.2f),
                        new MultiWordPhrase("good afternoon", 1.2f),
                        new MultiWordPhrase("nice to see you", 1.4f),
                        new MultiWordPhrase("hey there", 1.3f),
                        new MultiWordPhrase("yo there", 1.2f),
                        new MultiWordPhrase("howdy friend", 1.3f),
                        new MultiWordPhrase("good morning", 1.2f),
                        new MultiWordPhrase("hello there", 1.4f),
                        new MultiWordPhrase("hey mate", 1.3f),
                        new MultiWordPhrase("hey man", 1.3f),
                        new MultiWordPhrase("hey bro", 1.3f),
                        new MultiWordPhrase("hey buddy", 1.3f),
                        new MultiWordPhrase("hey dude", 1.3f),
                        new MultiWordPhrase("hey pal", 1.3f),
                        new MultiWordPhrase("hey friend", 1.3f),
                        new MultiWordPhrase("yo mate", 1.2f),
                        new MultiWordPhrase("yo man", 1.2f),
                        new MultiWordPhrase("yo bro", 1.2f),
                        new MultiWordPhrase("yo dude", 1.2f),
                        new MultiWordPhrase("yo friend", 1.2f),
                        new MultiWordPhrase("hiya mate", 1.2f),
                        new MultiWordPhrase("hiya there", 1.2f),
                        new MultiWordPhrase("hiya friend", 1.2f),
                        new MultiWordPhrase("hi there", 1.2f),
                        new MultiWordPhrase("hi mate", 1.2f),
                        new MultiWordPhrase("hi friend", 1.2f),
                        new MultiWordPhrase("hi buddy", 1.2f),
                        new MultiWordPhrase("hi pal", 1.2f),

                        // Slang & friendly variations
                        new MultiWordPhrase("yo yo", 1.1f),
                        new MultiWordPhrase("sup man", 1.2f),
                        new MultiWordPhrase("sup bro", 1.2f),
                        new MultiWordPhrase("sup dude", 1.2f),
                        new MultiWordPhrase("wassup man", 1.2f),
                        new MultiWordPhrase("wassup bro", 1.2f),
                        new MultiWordPhrase("greetings traveler", 1.4f),
                        new MultiWordPhrase("greetings friend", 1.3f),
                        new MultiWordPhrase("nice seeing you", 1.3f),
                        new MultiWordPhrase("good to see you", 1.3f),
                        new MultiWordPhrase("lovely to see you", 1.3f),
                        new MultiWordPhrase("pleased to see you", 1.3f),

                        // Typo / shorthand variants
                        new MultiWordPhrase("good mornin", 1.2f),
                        new MultiWordPhrase("mornin mate", 1.2f),
                        new MultiWordPhrase("mornin there", 1.2f),
                        new MultiWordPhrase("good evenin", 1.2f),
                        new MultiWordPhrase("yo there mate", 1.2f),
                        new MultiWordPhrase("hey ya", 1.2f),
                        new MultiWordPhrase("hiya ya", 1.2f),
                        new MultiWordPhrase("yo ya", 1.1f),
                        new MultiWordPhrase("alright mate", 1.3f),   // UK-style greeting
                        new MultiWordPhrase("alright pal", 1.3f),
                        new MultiWordPhrase("you alright", 1.3f),
                        new MultiWordPhrase("you good yeah", 1.3f),
                        new MultiWordPhrase("safe bro", 1.2f),       // UK “safe” = hey/okay
                        new MultiWordPhrase("safe man", 1.2f),
                        new MultiWordPhrase("safe mate", 1.2f),
                        new MultiWordPhrase("yo g", 1.1f),
                        new MultiWordPhrase("yo boss", 1.2f),
                        new MultiWordPhrase("yo chief", 1.2f)

                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.StatusQuery,
                    priority: 5,
                    matchThreshold: 2.2f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "how", "hows", "how's", "howre", "how're", "howve", "how've", "what's", "whats", "sup" }, 1.05f),
                        new SynonymBucket(new[] { "you", "ya", "yall", "y'all", "ya'll", "u", "ur" }, 0.95f),
                        new SynonymBucket(new[] { "doing", "feeling", "going", "holding", "hangin", "hanging", "goin", "goin'", "doin", "doin'", "are", "been", "r" }, 0.85f),
                        new SynonymBucket(new[] { "ok", "okay", "alright", "good", "chilling", "chillin" }, 0.65f),
                        new SynonymBucket(new[] { "today", "tonight", "lately", "there" }, 0.5f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("how are you", 1.6f),
                        new MultiWordPhrase("how you doing", 1.6f),
                        new MultiWordPhrase("how's it going", 1.8f),
                        new MultiWordPhrase("how've you been", 1.8f),
                        new MultiWordPhrase("how are we doing", 1.8f),
                        new MultiWordPhrase("how r we doing", 1.8f),
                        new MultiWordPhrase("hows it hanging", 1.8f),
                        new MultiWordPhrase("what's up", 1.7f),
                        new MultiWordPhrase("hows it hanging", 1.7f),
                        new MultiWordPhrase("hows it hangin", 1.7f),
                        new MultiWordPhrase("sup dawg", 1.7f),
                        new MultiWordPhrase("hows u", 1.7f),
                        new MultiWordPhrase("how ya holding", 1.6f),
                        new MultiWordPhrase("how ya doin", 1.8f),
                        new MultiWordPhrase("how ya doin'", 1.8f),
                        new MultiWordPhrase("how ya doin today", 2f),
                        new MultiWordPhrase("how u doin", 1.8f),
                        new MultiWordPhrase("how u doing", 1.7f),
                        new MultiWordPhrase("how r u", 1.8f),
                        new MultiWordPhrase("how r ya", 1.8f),
                        new MultiWordPhrase("you doing ok", 1.5f),
                        new MultiWordPhrase("you doing okay", 1.5f),
                        new MultiWordPhrase("you good", 1.5f),
                        new MultiWordPhrase("everything alright", 1.6f),
                        new MultiWordPhrase("how are you doing", 1.9f),
                        new MultiWordPhrase("how are ya", 1.8f),
                        // Additions for StatusQuery multiWordPhrases
                        new MultiWordPhrase("how are you doing", 1.9f),
                        new MultiWordPhrase("how are ya", 1.8f),
                        new MultiWordPhrase("how are u", 1.8f),
                        new MultiWordPhrase("how you feeling", 1.9f),
                        new MultiWordPhrase("how do you feel", 1.8f),
                        new MultiWordPhrase("how you feeling today", 2.0f),
                        new MultiWordPhrase("how you holding up", 1.9f),
                        new MultiWordPhrase("how are things", 1.8f),
                        new MultiWordPhrase("hows things", 1.8f),
                        new MultiWordPhrase("how's things", 1.8f),
                        new MultiWordPhrase("how's everything", 1.8f),
                        new MultiWordPhrase("how's everything going", 1.9f),
                        new MultiWordPhrase("how's your day", 1.8f),
                        new MultiWordPhrase("how's your day going", 2.0f),
                        new MultiWordPhrase("how's your day been", 1.9f),
                        new MultiWordPhrase("hows your day", 1.8f),
                        new MultiWordPhrase("hows ur day", 1.8f),
                        new MultiWordPhrase("how's life", 1.8f),
                        new MultiWordPhrase("hows life", 1.8f),
                        new MultiWordPhrase("how's life treating you", 2.0f),
                        new MultiWordPhrase("life treating you okay", 1.8f),
                        new MultiWordPhrase("life treating you alright", 1.8f),
                        new MultiWordPhrase("you alright", 1.7f),
                        new MultiWordPhrase("you all right", 1.7f),
                        new MultiWordPhrase("ya alright", 1.7f),
                        new MultiWordPhrase("u alright", 1.7f),
                        new MultiWordPhrase("you ok", 1.7f),
                        new MultiWordPhrase("you okay", 1.7f),
                        new MultiWordPhrase("u ok", 1.7f),
                        new MultiWordPhrase("u okay", 1.7f),
                        new MultiWordPhrase("you good", 1.6f),
                        new MultiWordPhrase("ya good", 1.6f),
                        new MultiWordPhrase("u good", 1.6f),
                        new MultiWordPhrase("you doing ok", 1.6f),
                        new MultiWordPhrase("you doing okay", 1.6f),
                        new MultiWordPhrase("doing okay", 1.5f),
                        new MultiWordPhrase("doing alright", 1.5f),
                        new MultiWordPhrase("are you okay", 1.7f),
                        new MultiWordPhrase("are you ok", 1.7f),
                        new MultiWordPhrase("are you good", 1.6f),
                        new MultiWordPhrase("holding up okay", 1.7f),
                        new MultiWordPhrase("holding up alright", 1.7f),
                        new MultiWordPhrase("how goes it", 1.8f),
                        new MultiWordPhrase("how goes", 1.6f),
                        new MultiWordPhrase("how’s it going today", 2.0f),
                        new MultiWordPhrase("hows it going today", 2.0f),
                        new MultiWordPhrase("how’s it going mate", 1.9f),
                        new MultiWordPhrase("what about you", 1.5f),
                        new MultiWordPhrase("how are ya doing", 1.9f),
                        new MultiWordPhrase("how r you", 1.7f),
                        new MultiWordPhrase("how r ya", 1.7f),
                        new MultiWordPhrase("how r u doing", 1.9f),
                        new MultiWordPhrase("how u feeling", 1.8f),
                        new MultiWordPhrase("how u doing today", 1.9f),
                        new MultiWordPhrase("how u holding up", 1.8f),
                        new MultiWordPhrase("you okay there", 1.7f),
                        new MultiWordPhrase("you alright there", 1.7f),
                        new MultiWordPhrase("you good yeah", 1.6f),
                        new MultiWordPhrase("you ok yeah", 1.6f),
                        new MultiWordPhrase("all good with you", 1.6f),
                        new MultiWordPhrase("everything good with you", 1.6f),
                        new MultiWordPhrase("everything alright with you", 1.6f),
                        new MultiWordPhrase("what you saying", 1.6f),          // UK casual check-in
                        new MultiWordPhrase("what you sayin", 1.6f),
                        new MultiWordPhrase("what are you saying", 1.6f),
                        new MultiWordPhrase("what are you up to", 1.5f),
                        new MultiWordPhrase("what you up to", 1.5f),
                        new MultiWordPhrase("what’s good with you", 1.6f),
                        new MultiWordPhrase("what’s new with you", 1.6f),
                        new MultiWordPhrase("how’ve you been lately", 1.9f),
                        new MultiWordPhrase("how you been lately", 1.9f),
                        new MultiWordPhrase("how you been", 1.8f),
                        new MultiWordPhrase("how have you been", 1.9f),
                        new MultiWordPhrase("you been okay", 1.6f),
                        new MultiWordPhrase("you been alright", 1.6f),
                        new MultiWordPhrase("feeling alright", 1.6f),
                        new MultiWordPhrase("feeling okay", 1.6f),
                        new MultiWordPhrase("feeling any better", 1.6f),
                        new MultiWordPhrase("how are we feeling", 1.8f),
                        new MultiWordPhrase("how are we doing today", 1.9f),
                        new MultiWordPhrase("how r we doing today", 1.9f),
                        new MultiWordPhrase("how are you today", 1.9f),
                        new MultiWordPhrase("how you doing today", 1.9f),
                        new MultiWordPhrase("how are you this morning", 1.9f),
                        new MultiWordPhrase("how are you this evening", 1.9f),
                        new MultiWordPhrase("how are you tonight", 1.9f),
                        new MultiWordPhrase("you doing okay today", 1.7f),
                        new MultiWordPhrase("you doing ok today", 1.7f),
                        new MultiWordPhrase("we good", 1.5f),
                        new MultiWordPhrase("all okay", 1.5f),
                        new MultiWordPhrase("all alright", 1.5f),
                        new MultiWordPhrase("everything calm", 1.5f),        // UK “calm” meaning okay
                        new MultiWordPhrase("you calm", 1.5f),
                        new MultiWordPhrase("you bless", 1.5f),              // UK slang “you okay”
                        new MultiWordPhrase("you safe yeah", 1.5f),          // UK “you safe?”
                        new MultiWordPhrase("you sweet yeah", 1.5f),         // UK slang
                        new MultiWordPhrase("what’s cracking", 1.6f),
                        new MultiWordPhrase("what’s popping", 1.6f),
                        new MultiWordPhrase("what’s happening with you", 1.6f)

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
                        if (StatusQueryShorthandRegex.IsMatch(text))
                            score = Mathf.Max(score, 2.25f);
                        return score;
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
                        // High-intensity praise words should succeed on their own, so they retain a weight
                        // that clears the compliment threshold without additional context tokens.
                        new SynonymBucket(new[] { "great", "awesome", "amazing", "brilliant", "fantastic", "stellar", "dope", "slick" }, 1.8f),
                        // Softer praise must now pair with a contextual noun bucket to meet the threshold,
                        // preventing greetings like "good morning" from misfiring as compliments.
                        new SynonymBucket(new[] { "good", "nice", "solid" }, 0.95f),
                        new SynonymBucket(new[] { "job", "work", "partner", "friend", "assist", "move", "play" }, 0.9f),
                        new SynonymBucket(new[] { "legend", "goat", "rockstar", "lifesaver" }, 0.9f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("good job", 1.75f),
                        new MultiWordPhrase("great work", 1.7f),
                        new MultiWordPhrase("awesome job", 1.75f),
                        new MultiWordPhrase("nice work", 1.7f),
                        new MultiWordPhrase("you're amazing", 1.85f),
                        new MultiWordPhrase("you're the best", 1.9f)
                    },
                    negativeKeywords: new[] { "morning", "afternoon", "evening", "night" }),

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
                    CompanionDialogueIntent.PlayerSkillProposal,
                    priority: 34,
                    matchThreshold: 1.9f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "do", "are", "shall", "would", "want" }, 0.6f),
                        new SynonymBucket(new[] { "you", "ya", "u" }, 0.5f),
                        new SynonymBucket(new[] { "want", "wanna", "fancy", "keen", "up", "game", "feel" }, 0.9f),
                        new SynonymBucket(SkillKeywordTokens, 0.9f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("do you want to", 1.5f),
                        new MultiWordPhrase("do ya want to", 1.4f),
                        new MultiWordPhrase("do you wanna", 1.5f),
                        new MultiWordPhrase("are you up for", 1.6f),
                        new MultiWordPhrase("up for some", 1.4f),
                        new MultiWordPhrase("fancy some", 1.3f),
                        new MultiWordPhrase("fancy doing", 1.3f),
                        new MultiWordPhrase("feel like", 1.2f),
                        new MultiWordPhrase("keen to", 1.2f),
                        new MultiWordPhrase("want to go", 1.2f),
                        new MultiWordPhrase("want to do", 1.2f),
                        new MultiWordPhrase("shall we", 1.3f)
                    },
                    regexScoreEvaluator: text =>
                    {
                        float score = 0f;
                        if (!string.IsNullOrEmpty(text) && text.IndexOf('?', StringComparison.Ordinal) >= 0)
                            score += 0.4f;
                        if (!string.IsNullOrEmpty(text) && text.IndexOf("you up for", StringComparison.Ordinal) >= 0)
                            score += 0.6f;
                        return score;
                    }),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.AcceptSkillPlan,
                    priority: 35,
                    matchThreshold: 1.7f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "yes", "yeah", "yep", "sure", "ok", "okay", "yup", "down", "definitely", "sounds" }, 1.1f),
                        new SynonymBucket(new[] { "lets", "let's", "keep", "continue", "more" }, 0.6f),
                        new SynonymBucket(SkillKeywordTokens, 0.7f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("yeah let's", 1.6f),
                        new MultiWordPhrase("let's keep", 1.5f),
                        new MultiWordPhrase("let's do it", 1.5f),
                        new MultiWordPhrase("i'm in", 1.6f),
                        new MultiWordPhrase("count me in", 1.6f),
                        new MultiWordPhrase("sounds good", 1.4f)
                    },
                    regexScoreEvaluator: text => SkillKeywordRegex.IsMatch(text) ? 0.5f : 0f),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.DeclineSkillPlan,
                    priority: 36,
                    matchThreshold: 1.6f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "no", "nah", "nope", "pass", "skip" }, 1.2f),
                        new SynonymBucket(new[] { "dont", "don't", "rather", "instead", "not" }, 0.6f),
                        new SynonymBucket(SkillKeywordTokens, 0.5f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("hard pass", 1.5f),
                        new MultiWordPhrase("i'd rather not", 1.6f),
                        new MultiWordPhrase("not interested", 1.5f),
                        new MultiWordPhrase("no thanks", 1.4f)
                    },
                    regexScoreEvaluator: text => SkillKeywordRegex.IsMatch(text) ? 0.4f : 0f),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.DeferSkillPlan,
                    priority: 37,
                    matchThreshold: 1.6f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "later", "after", "another", "bit", "moment" }, 0.7f),
                        new SynonymBucket(new[] { "not", "now", "right", "currently" }, 0.6f),
                        new SynonymBucket(new[] { "maybe", "soon" }, 0.5f),
                        new SynonymBucket(SkillKeywordTokens, 0.4f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("not now", 1.6f),
                        new MultiWordPhrase("maybe later", 1.6f),
                        new MultiWordPhrase("later on", 1.5f),
                        new MultiWordPhrase("give me a minute", 1.5f),
                        new MultiWordPhrase("after this", 1.3f)
                    },
                    regexScoreEvaluator: text => SkillKeywordRegex.IsMatch(text) ? 0.3f : 0f),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.CompanionSuggestionRequest,
                    priority: 32,
                    matchThreshold: 1.6f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "what", "whats", "what's" }, 1f),
                        new SynonymBucket(new[] { "do", "doing" }, 0.6f),
                        new SynonymBucket(new[] { "want", "wanna", "like" }, 0.8f),
                        new SynonymBucket(new[] { "train", "training", "do", "doing", "activity", "plan" }, 0.6f),
                        new SynonymBucket(new[] { "you", "ya" }, 0.8f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("what do you want to train", 2.2f),
                        new MultiWordPhrase("what do you wanna train", 2.2f),
                        new MultiWordPhrase("what do you want to do", 2.2f),
                        new MultiWordPhrase("what do you wanna do", 2.2f),
                        new MultiWordPhrase("what would you like to do", 2.2f),
                        new MultiWordPhrase("what should we do", 1.8f)
                    },
                    regexScoreEvaluator: text => SuggestionQuestionRegex.IsMatch(text) ? 0.4f : 0f),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.CompanionSuggestionReminder,
                    priority: 33,
                    matchThreshold: 1.3f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "remind", "reminder", "again", "forgot", "forget" }, 1.1f),
                        new SynonymBucket(new[] { "me", "us" }, 0.7f),
                        new SynonymBucket(new[] { "tell", "say" }, 0.6f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("remind me", 1.8f),
                        new MultiWordPhrase("tell me again", 1.8f),
                        new MultiWordPhrase("i forgot", 1.6f),
                        new MultiWordPhrase("remind me again", 1.9f),
                        new MultiWordPhrase("tell us again", 1.8f),
                        new MultiWordPhrase("what was it again", 1.9f),
                        new MultiWordPhrase("what did you want to do again", 2.0f),
                        new MultiWordPhrase("what did you wanna do again", 2.0f)
                    },
                    regexScoreEvaluator: text => SuggestionReminderRegex.IsMatch(text) ? 0.4f : 0f),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.RequestAlternateSkill,
                    priority: 38,
                    matchThreshold: 1.6f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "another", "different", "else", "alternate", "surprise" }, 1f),
                        new SynonymBucket(new[] { "skill", "option", "plan" }, 0.6f),
                        new SynonymBucket(SkillKeywordTokens, 0.5f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("something else", 1.6f),
                        new MultiWordPhrase("another skill", 1.7f),
                        new MultiWordPhrase("pick something", 1.4f),
                        new MultiWordPhrase("surprise me", 1.7f),
                        new MultiWordPhrase("choose for me", 1.6f)
                    },
                    regexScoreEvaluator: text => SkillKeywordRegex.IsMatch(text) ? 0.4f : 0f),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.AcknowledgeRecentEvent,
                    priority: 40,
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
                    regexScoreEvaluator: text => RecentEventRegex.IsMatch(text) ? 0.8f : 0f),

                new CompanionIntentPattern(
                    CompanionDialogueIntent.SkillLevelQuery,
                    priority: 8,
                    matchThreshold: 2.1f,
                    synonymBuckets: new[]
                    {
                        new SynonymBucket(new[] { "what", "whats", "what's", "waht", "wats", "wut", "wat" }, 1.1f),
                        new SynonymBucket(new[] { "your", "ya", "yaa", "ur", "you" }, 0.95f),
                        new SynonymBucket(SkillKeywordTokens, 0.9f),
                        new SynonymBucket(new[] { "level", "levels", "lvl", "lvls", "levl", "levll" }, 1.2f)
                    },
                    multiWordPhrases: new[]
                    {
                        new MultiWordPhrase("whats your mining level", 2.6f),
                        new MultiWordPhrase("whats your woodcutting level", 2.6f),
                        new MultiWordPhrase("whats your wc level", 2.4f),
                        new MultiWordPhrase("whats your fishing level", 2.6f),
                        new MultiWordPhrase("whats your cooking level", 2.5f),
                        new MultiWordPhrase("whats your firemaking level", 2.5f),
                        new MultiWordPhrase("whats your fm level", 2.4f),
                        new MultiWordPhrase("whats your magic level", 2.4f),
                        new MultiWordPhrase("whats your mage level", 2.4f),
                        new MultiWordPhrase("whats your ranged level", 2.4f),
                        new MultiWordPhrase("whats your range level", 2.4f),
                        new MultiWordPhrase("whats your attack level", 2.5f),
                        new MultiWordPhrase("whats your atk level", 2.5f),
                        new MultiWordPhrase("whats your strength level", 2.5f),
                        new MultiWordPhrase("whats your str level", 2.4f),
                        new MultiWordPhrase("whats your defence level", 2.5f),
                        new MultiWordPhrase("whats your def level", 2.4f),
                        new MultiWordPhrase("whats your hitpoints level", 2.6f),
                        new MultiWordPhrase("whats your hp level", 2.6f),
                        new MultiWordPhrase("whats your health level", 2.4f),
                        new MultiWordPhrase("whats your beastmaster level", 2.4f),
                        new MultiWordPhrase("whats your pet level", 2.3f),
                        new MultiWordPhrase("what level is your mining", 2.2f),
                        new MultiWordPhrase("what level is your magic", 2.2f),
                        new MultiWordPhrase("what level is your hp", 2.2f)
                    },
                    regexScoreEvaluator: text =>
                    {
                        float score = 0f;
                        if (SkillLevelQueryRegex.IsMatch(text))
                            score += 1.4f;
                        if (SkillLevelCompactRegex.IsMatch(text))
                            score = Mathf.Max(score, 2.4f);
                        return score;
                    })
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
