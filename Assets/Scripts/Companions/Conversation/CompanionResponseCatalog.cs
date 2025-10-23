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

            RegisterGreetingResponses();
            RegisterStatusQueryResponses();
            RegisterPlayerMoodResponses();
            RegisterGratitudeResponses();
            RegisterFarewellResponses();
            RegisterComplimentResponses();
            RegisterAssistanceResponses();
            RegisterEventAcknowledgementResponses();
        }

        /// <summary>
        /// Populates the baseline greeting templates covering casual hello variants.
        /// </summary>
        private static void RegisterGreetingResponses()
        {
            Register(
                CompanionDialogueIntent.Greeting,
                new ResponseTemplate("Hey there {playerName}, ready to chase glory?", 1f),
                new ResponseTemplate("Well met, {playerName}! {companionName} reporting for duty.", 1f),
                new ResponseTemplate("Greetings {playerName}, the {timeOfDay} air feels lucky today.", 0.9f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("Bright {timeOfDay} to you, {playerName}!|I already swept our camp for stragglers.", 0.85f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("Evening watch is quieter with you around, {playerName}.|Want me to brew something warm for this {timeOfDay}?", 0.75f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("Looks like we're {combatState}.|Just give the word and I'll tighten formation.", 0.8f),
                new ResponseTemplate("I spotted you {recentSkillAction} earlier.|Need me to stash the kit for the next run?", 0.7f, ctx => ctx.HasRecentSkillActions),
                new ResponseTemplate("Morning, {playerName}.|{companionName} already limbered up and checked our packs.", 0.9f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("Hey {playerName}, {companionMood} as ever after {recentEvent}.", 0.75f));
        }

        /// <summary>
        /// Populates the status query templates companions use when checking on the player.
        /// </summary>
        private static void RegisterStatusQueryResponses()
        {
            Register(
                CompanionDialogueIntent.StatusQuery,
                new ResponseTemplate("I'm keeping watch and feeling {companionMood}. How are you holding up?", 1f),
                new ResponseTemplate("Still {companionMood} even after {recentEvent}.|You doing alright, {playerName}?", 0.95f),
                new ResponseTemplate("Staying {companionMood} and {combatState}.|Need me to top off supplies after this?", 0.8f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("I'm {companionMood} as always. Anything exciting on your agenda?", 1f),
                new ResponseTemplate("This {timeOfDay} breeze keeps me focused.|How are you feeling, {playerName}?", 0.85f),
                new ResponseTemplate("Saw you {recentSkillAction} a moment ago.|Want me to log the results while you breathe?", 0.75f, ctx => ctx.HasRecentSkillActions),
                new ResponseTemplate("All quiet here and {combatState}.|Should we stretch the legs with another patrol?", 0.8f),
                new ResponseTemplate("Queue's already stacked with chatter.|Want me to triage it while you stay {combatState}?", 0.7f, ctx => ctx.HasPendingResponses));
        }

        /// <summary>
        /// Registers templates that respond to players reporting their own mood.
        /// </summary>
        private static void RegisterPlayerMoodResponses()
        {
            Register(
                CompanionDialogueIntent.PlayerMoodReport,
                new ResponseTemplate("Good to know you're {playerMood}, {playerName}.|Let's channel that into our next move.", 1f),
                new ResponseTemplate("Thanks for sharing that you're {playerMood}, {playerName}.|I'll keep pace with you.", 1f),
                new ResponseTemplate("I'll keep it in mind that you're {playerMood}.|Want me to note it in the logbook?", 0.9f),
                new ResponseTemplate("Hearing you're {playerMood} after {recentEvent} keeps me grounded.", 0.85f),
                new ResponseTemplate("If you're feeling {playerMood}, maybe this {timeOfDay} air will steady us both.", 0.8f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("While you're {playerMood}, we could follow up on {recentSkillAction}.|Say the word and I'll prep the kit.", 0.75f, ctx => ctx.HasRecentSkillActions),
                new ResponseTemplate("Even while we're {combatState}, I'll match that {playerMood} energy.", 0.8f));
        }

        /// <summary>
        /// Registers the standard gratitude lines companions reply with when thanked.
        /// </summary>
        private static void RegisterGratitudeResponses()
        {
            Register(
                CompanionDialogueIntent.Gratitude,
                new ResponseTemplate("Anytime, {playerName}! {companionName} has your back.", 1f),
                new ResponseTemplate("Happy to help, {playerName}.|Want me to tidy up after that {recentSkillAction}?", 0.85f, ctx => ctx.HasRecentSkillActions),
                new ResponseTemplate("You know I've always got you, {playerName}.|Especially after {recentEvent}.", 0.9f),
                new ResponseTemplate("Always glad to assist during this {timeOfDay}.|Need anything else before I stand down?", 0.8f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("Even while we're {combatState}, I'm grateful to be at your side.", 0.85f),
                new ResponseTemplate("Your thanks keeps morale {companionMood}.|Let's keep moving.", 0.8f));
        }

        /// <summary>
        /// Registers farewell templates companions use when the player signs off.
        /// </summary>
        private static void RegisterFarewellResponses()
        {
            Register(
                CompanionDialogueIntent.Farewell,
                new ResponseTemplate("Safe travels, {playerName}. I'll hold the fort.", 1f),
                new ResponseTemplate("I'll stay sharp while you're away, {playerName}.", 1f),
                new ResponseTemplate("Take care out there, {playerName}. I'll be right here.", 1f),
                new ResponseTemplate("Catch you next {timeOfDay}, {playerName}.|I'll keep the campfire ready.", 0.85f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("I'll review our notes on {recentEvent} while you're gone.|Maybe it'll spark a new idea.", 0.8f),
                new ResponseTemplate("If we're still {combatState}, I'll disengage once you're clear.", 0.75f),
                new ResponseTemplate("I'll pack away the gear from that {recentSkillAction}.|See you soon, {playerName}.", 0.8f, ctx => ctx.HasRecentSkillActions));
        }

        /// <summary>
        /// Registers compliment responses so companions can deflect praise.
        /// </summary>
        private static void RegisterComplimentResponses()
        {
            Register(
                CompanionDialogueIntent.Compliment,
                new ResponseTemplate("You're the one doing the heavy lifting, {playerName}.", 1f),
                new ResponseTemplate("Flattery will get you everywhere, {playerName}.", 1f),
                new ResponseTemplate("Keep this up and you'll outshine the legends, {playerName}.", 0.9f),
                new ResponseTemplate("After {recentEvent}, I'd say we both earned the praise.", 0.85f),
                new ResponseTemplate("If you saw me during that {recentSkillAction}, you know I was just keeping pace with you.", 0.75f, ctx => ctx.HasRecentSkillActions),
                new ResponseTemplate("Compliments hit different in the {timeOfDay} light, huh?", 0.8f, ctx => !ctx.PlayerInCombat),
                new ResponseTemplate("Call me {companionMood}, but you're the one keeping us {combatState}.", 0.8f));
        }

        /// <summary>
        /// Registers assistance responses covering tactical and casual follow-ups.
        /// </summary>
        private static void RegisterAssistanceResponses()
        {
            Register(
                CompanionDialogueIntent.RequestAssistance,
                new ResponseTemplate("On it! Just point me where you need me, {playerName}.", 1f),
                new ResponseTemplate("Consider it handled, {playerName}.", 1f),
                new ResponseTemplate("I'll cover you, {playerName}. Let's get it done.", 1f),
                new ResponseTemplate("I'll swing by right after this.|Anything else you'd like me to prep?", 0.8f, ctx => !ctx.HasPendingResponses),
                new ResponseTemplate("If we're {combatState}, I'll draw fire while you reposition.", 0.85f, ctx => ctx.PlayerInCombat),
                new ResponseTemplate("I'll grab the kit from that {recentSkillAction} run.|Meet me at the staging point.", 0.75f, ctx => ctx.HasRecentSkillActions),
                new ResponseTemplate("Give me a moment to finish logging {recentEvent}.|Then I'm all yours.", 0.7f),
                new ResponseTemplate("Perfect {timeOfDay} for it.|I'll rally the gear and meet you ahead.", 0.8f, ctx => !ctx.PlayerInCombat));
        }

        /// <summary>
        /// Registers acknowledgement responses for referencing recent narrative beats.
        /// </summary>
        private static void RegisterEventAcknowledgementResponses()
        {
            Register(
                CompanionDialogueIntent.AcknowledgeRecentEvent,
                new ResponseTemplate("Hard to forget {recentEvent}. We'll be ready next time.", 1f),
                new ResponseTemplate("Yeah, {recentEvent} was a wild moment.", 1f),
                new ResponseTemplate("{recentEvent} taught us a few tricks. We'll use them soon.", 0.85f),
                new ResponseTemplate("I'm still {companionMood} thinking about {recentEvent}.|Want to debrief now?", 0.8f),
                new ResponseTemplate("After {recentEvent}, keeping us {combatState} feels even more important.", 0.8f),
                new ResponseTemplate("That {timeOfDay} rush during {recentEvent} had my heart racing.", 0.75f),
                new ResponseTemplate("While you were {recentSkillAction} I kept replaying {recentEvent}.|Maybe there's more to learn.", 0.7f, ctx => ctx.HasRecentSkillActions));
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

