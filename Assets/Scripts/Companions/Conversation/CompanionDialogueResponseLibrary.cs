using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companions.Conversation
{
    /// <summary>
    /// Stores templated response fragments keyed by <see cref="CompanionDialogueIntent"/> so the conversation
    /// service can assemble natural dialogue lines. Templates may include placeholders like {playerName},
    /// {companionName}, {playerMood}, {companionMood}, and {recentEvent}.
    /// </summary>
    [Serializable]
    public sealed class CompanionDialogueResponseLibrary
    {
        [SerializeField]
        private List<IntentResponseSet> responses = new List<IntentResponseSet>();

        /// <summary>
        /// Retrieves a random template for the requested intent. The helper attempts to avoid repeating the
        /// provided <paramref name="disallowed"/> template when multiple options exist.
        /// </summary>
        public string GetRandomTemplate(CompanionDialogueIntent intent, string disallowed = null)
        {
            if (responses == null || responses.Count == 0)
                return string.Empty;

            for (int i = 0; i < responses.Count; i++)
            {
                var set = responses[i];
                if (set.Intent != intent)
                    continue;

                return set.GetRandom(disallowed);
            }

            return string.Empty;
        }

        /// <summary>
        /// Ensures the library contains sensible defaults for core intents when no custom data has been configured.
        /// </summary>
        public void EnsureDefaults()
        {
            if (responses == null)
                responses = new List<IntentResponseSet>();

            EnsureSetExists(CompanionDialogueIntent.Greeting,
                "Hey there {playerName},",
                "Well met, {playerName}!",
                "Greetings {playerName},");

            EnsureSetExists(CompanionDialogueIntent.StatusQuery,
                "I'm keeping watch and feeling {companionMood}. How are you holding up?",
                "Staying {companionMood} and ready for whatever comes next. How about you?",
                "I'm {companionMood} as always. Anything exciting on your agenda?");

            EnsureSetExists(CompanionDialogueIntent.PlayerMoodReport,
                "Good to know you're {playerMood}, {playerName}.",
                "Thanks for sharing that you're {playerMood}, {playerName}.",
                "I'll keep it in mind that you're {playerMood}.");

            EnsureSetExists(CompanionDialogueIntent.Gratitude,
                "Anytime, {playerName}! {companionName} has your back.",
                "Happy to help, {playerName}.",
                "You know I've always got you, {playerName}.");

            EnsureSetExists(CompanionDialogueIntent.Farewell,
                "Safe travels, {playerName}. I'll hold the fort.",
                "I'll stay sharp while you're away, {playerName}.",
                "Take care out there, {playerName}. I'll be right here.");

            EnsureSetExists(CompanionDialogueIntent.Compliment,
                "You're the one doing the heavy lifting, {playerName}.",
                "Flattery will get you everywhere, {playerName}.");

            EnsureSetExists(CompanionDialogueIntent.RequestAssistance,
                "On it! Just point me where you need me, {playerName}.",
                "Consider it handled, {playerName}.",
                "I'll cover you, {playerName}. Let's get it done.");

            EnsureSetExists(CompanionDialogueIntent.AcknowledgeRecentEvent,
                "Hard to forget {recentEvent}. We'll be ready next time.",
                "Yeah, {recentEvent} was a wild moment.");
        }

        private void EnsureSetExists(CompanionDialogueIntent intent, params string[] templates)
        {
            if (responses.Exists(r => r.Intent == intent))
                return;

            var set = new IntentResponseSet
            {
                Intent = intent,
                Templates = templates ?? Array.Empty<string>()
            };

            responses.Add(set);
        }

        /// <summary>
        /// Represents the collection of templates tied to a specific conversational intent.
        /// </summary>
        [Serializable]
        private sealed class IntentResponseSet
        {
            [SerializeField]
            private CompanionDialogueIntent intent;

            [SerializeField, TextArea]
            private string[] templates = Array.Empty<string>();

            [NonSerialized]
            private int lastUsedIndex = -1;

            public CompanionDialogueIntent Intent
            {
                get => intent;
                set => intent = value;
            }

            public string[] Templates
            {
                get => templates;
                set => templates = value ?? Array.Empty<string>();
            }

            public string GetRandom(string disallowed)
            {
                if (templates == null || templates.Length == 0)
                    return string.Empty;

                if (templates.Length == 1)
                {
                    string single = templates[0] ?? string.Empty;
                    lastUsedIndex = 0;
                    return single.Trim();
                }

                const int MaxAttempts = 4;
                int chosenIndex = -1;
                for (int attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    int candidate = UnityEngine.Random.Range(0, templates.Length);
                    string value = templates[candidate] ?? string.Empty;
                    if (!string.Equals(value, disallowed, StringComparison.Ordinal) && candidate != lastUsedIndex)
                    {
                        chosenIndex = candidate;
                        break;
                    }

                    if (attempt == MaxAttempts - 1)
                        chosenIndex = candidate;
                }

                if (chosenIndex < 0)
                    chosenIndex = 0;

                lastUsedIndex = chosenIndex;
                return (templates[chosenIndex] ?? string.Empty).Trim();
            }
        }
    }
}
