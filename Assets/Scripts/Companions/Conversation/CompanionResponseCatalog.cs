using System;
using System.Collections.Generic;

namespace Companions.Conversation
{
    /// <summary>
    /// Central repository of companion dialogue response templates. Catalog entries are queried by
    /// <see cref="CompanionDialogueIntent"/> and can optionally specify guard predicates that inspect
    /// the ambient <see cref="CompanionResponseContext"/> before being considered.
    /// </summary>
    public static class CompanionResponseCatalog
    {
        private static readonly Dictionary<CompanionDialogueIntent, List<ResponseTemplate>> Catalog = new();
        private static bool defaultsBuilt;

        /// <summary>
        /// Ensures the catalog contains the baseline response templates used across the project.
        /// </summary>
        public static void EnsureDefaults()
        {
            if (defaultsBuilt)
                return;

            defaultsBuilt = true;

            Register(
                CompanionDialogueIntent.Greeting,
                new ResponseTemplate("Hey there {playerName},", 1f),
                new ResponseTemplate("Well met, {playerName}!", 1f),
                new ResponseTemplate("Greetings {playerName},", 1f),
                new ResponseTemplate("Bright {timeOfDay} to you, {playerName}!", 0.85f, ctx => !ctx.PlayerInCombat));

            Register(
                CompanionDialogueIntent.StatusQuery,
                new ResponseTemplate("I'm keeping watch and feeling {companionMood}. How are you holding up?", 1f),
                new ResponseTemplate("Staying {companionMood} and ready for whatever comes next. How about you?", 1f),
                new ResponseTemplate("I'm {companionMood} as always. Anything exciting on your agenda?", 1f),
                new ResponseTemplate("All quiet here and {combatState}.|Need me to check our supplies after this?", 0.75f, ctx => !ctx.PlayerInCombat));

            Register(
                CompanionDialogueIntent.PlayerMoodReport,
                new ResponseTemplate("Good to know you're {playerMood}, {playerName}.", 1f),
                new ResponseTemplate("Thanks for sharing that you're {playerMood}, {playerName}.", 1f),
                new ResponseTemplate("I'll keep it in mind that you're {playerMood}.", 1f));

            Register(
                CompanionDialogueIntent.Gratitude,
                new ResponseTemplate("Anytime, {playerName}! {companionName} has your back.", 1f),
                new ResponseTemplate("Happy to help, {playerName}.", 1f),
                new ResponseTemplate("You know I've always got you, {playerName}.", 1f));

            Register(
                CompanionDialogueIntent.Farewell,
                new ResponseTemplate("Safe travels, {playerName}. I'll hold the fort.", 1f),
                new ResponseTemplate("I'll stay sharp while you're away, {playerName}.", 1f),
                new ResponseTemplate("Take care out there, {playerName}. I'll be right here.", 1f));

            Register(
                CompanionDialogueIntent.Compliment,
                new ResponseTemplate("You're the one doing the heavy lifting, {playerName}.", 1f),
                new ResponseTemplate("Flattery will get you everywhere, {playerName}.", 1f),
                new ResponseTemplate("Keep this up and you'll outshine the legends, {playerName}.", 0.9f));

            Register(
                CompanionDialogueIntent.RequestAssistance,
                new ResponseTemplate("On it! Just point me where you need me, {playerName}.", 1f),
                new ResponseTemplate("Consider it handled, {playerName}.", 1f),
                new ResponseTemplate("I'll cover you, {playerName}. Let's get it done.", 1f),
                new ResponseTemplate("I'll swing by right after this.|Anything else you'd like me to prep?", 0.8f, ctx => !ctx.HasPendingResponses));

            Register(
                CompanionDialogueIntent.AcknowledgeRecentEvent,
                new ResponseTemplate("Hard to forget {recentEvent}. We'll be ready next time.", 1f),
                new ResponseTemplate("Yeah, {recentEvent} was a wild moment.", 1f),
                new ResponseTemplate("{recentEvent} taught us a few tricks. We'll use them soon.", 0.85f));
        }

        /// <summary>
        /// Returns the templates registered for the requested intent. When no entries exist an empty
        /// read-only list is returned so callers can iterate safely.
        /// </summary>
        public static IReadOnlyList<ResponseTemplate> GetTemplates(CompanionDialogueIntent intent)
        {
            EnsureDefaults();
            return Catalog.TryGetValue(intent, out var list)
                ? list
                : Array.Empty<ResponseTemplate>();
        }

        /// <summary>
        /// Registers one or more templates for the supplied intent, appending them to the catalog.
        /// </summary>
        private static void Register(CompanionDialogueIntent intent, params ResponseTemplate[] templates)
        {
            if (!Catalog.TryGetValue(intent, out var list))
            {
                list = new List<ResponseTemplate>();
                Catalog[intent] = list;
            }

            if (templates == null)
                return;

            for (int i = 0; i < templates.Length; i++)
            {
                var template = templates[i];
                if (string.IsNullOrWhiteSpace(template.Text))
                    continue;

                list.Add(template);
            }
        }

        /// <summary>
        /// Immutable response definition containing the template text, weight, and optional guard.
        /// </summary>
        public readonly struct ResponseTemplate
        {
            public ResponseTemplate(string text, float weight, Func<CompanionResponseContext, bool> guard = null)
            {
                Text = text ?? string.Empty;
                Weight = weight;
                Guard = guard;
            }

            /// <summary>Raw template text, optionally containing '|' delimited follow-up prompts.</summary>
            public string Text { get; }

            /// <summary>Relative selection weight. Values &lt;= 0 default to a weight of 1 during selection.</summary>
            public float Weight { get; }

            /// <summary>Optional predicate inspected before including the template in a selection pool.</summary>
            public Func<CompanionResponseContext, bool> Guard { get; }
        }
    }
}

