using System;
using System.Collections.Generic;
using Companions.Conversation;
using NUnit.Framework;

namespace Tests.Companions
{
    /// <summary>
    /// Covers the intent pruning heuristics that disambiguate greeting and compliment overlaps.
    /// </summary>
    public sealed class CompanionIntentDisambiguatorTests
    {
        [Test]
        public void PruneContradictoryIntents_StripsComplimentWhenGreetingLacksPronouns()
        {
            var tokens = new List<string> { "good", "morning", "isla", "im", "back" };
            var uniqueTokens = new HashSet<string>(tokens, StringComparer.Ordinal);
            var matches = new List<CompanionDialogueMatch>
            {
                new CompanionDialogueMatch(CompanionDialogueIntent.Greeting, priority: 0, score: 2.1f),
                new CompanionDialogueMatch(CompanionDialogueIntent.Compliment, priority: 1, score: 2.0f)
            };
            var parseResult = new CompanionDialogueParseResult(tokens, uniqueTokens, matches);

            var pruned = CompanionIntentDisambiguator.PruneContradictoryIntents(parseResult);

            Assert.AreEqual(1, pruned.Matches.Count, "Compliment intent should be removed when greeting lacks praise anchors.");
            Assert.AreEqual(CompanionDialogueIntent.Greeting, pruned.Matches[0].Intent, "Greeting intent should remain available.");
        }

        [Test]
        public void PruneContradictoryIntents_PreservesComplimentWhenPraiseTokensPresent()
        {
            var tokens = new List<string> { "good", "job", "you", "re", "amazing" };
            var uniqueTokens = new HashSet<string>(tokens, StringComparer.Ordinal);
            var matches = new List<CompanionDialogueMatch>
            {
                new CompanionDialogueMatch(CompanionDialogueIntent.Greeting, priority: 0, score: 2.1f),
                new CompanionDialogueMatch(CompanionDialogueIntent.Compliment, priority: 1, score: 2.4f)
            };
            var parseResult = new CompanionDialogueParseResult(tokens, uniqueTokens, matches);

            var pruned = CompanionIntentDisambiguator.PruneContradictoryIntents(parseResult);

            Assert.AreEqual(2, pruned.Matches.Count, "Compliment intent should be retained when praise context is present.");
            CollectionAssert.AreEqual(
                new[] { CompanionDialogueIntent.Greeting, CompanionDialogueIntent.Compliment },
                new[] { pruned.Matches[0].Intent, pruned.Matches[1].Intent },
                "Intent ordering should remain unchanged when no pruning occurs.");
        }
    }
}
