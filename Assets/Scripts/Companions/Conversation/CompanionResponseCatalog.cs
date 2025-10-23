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
            RegisterGratitudeResponses();
            RegisterFarewellResponses();
            RegisterComplimentResponses();
            RegisterAssistanceResponses();
            RegisterEventAcknowledgementResponses();
            RegisterSkillQuestionResponses();
            RegisterSkillQuestionFollowUps();
        }

        /// <summary>
        /// Populates the baseline greeting templates covering casual hello variants.
        /// </summary>
        private static void RegisterGreetingResponses()
        {
            Register(
                CompanionDialogueIntent.Greeting,
                //Generic responses
                new ResponseTemplate("Oh hey, {playerName}.", 1f),
                new ResponseTemplate("Hi {playerName}.", 1f),
                new ResponseTemplate("Hey {playerName} you good?", 1f),
                new ResponseTemplate("Hello hello, {playerName}.", 0.9f),
                new ResponseTemplate("Hey there. Nice to hear you.", 0.9f),
                new ResponseTemplate("Yo. What’s up?", 0.9f),
                new ResponseTemplate("Hey what’s the plan?", 0.9f),
                new ResponseTemplate("Hi! Need me?", 0.9f),
                new ResponseTemplate("Hey! I’m here.", 0.9f),
                new ResponseTemplate("Hey there {playerName}, ready to chase glory?", 1f),
                new ResponseTemplate("Well met, {playerName}! {companionName} reporting for duty.", 1f),

            // time-of-day flavor (guarded)
                new ResponseTemplate("Morning, {playerName}. You sleep okay?", 0.8f, ctx => ctx.TimeOfDayLabel == "morning" && !ctx.PlayerInCombat),
                new ResponseTemplate("Afternoon, {playerName}. You eating today?", 0.8f, ctx => ctx.TimeOfDayLabel == "afternoon" && !ctx.PlayerInCombat),
                new ResponseTemplate("Evening, {playerName}. Long day?", 0.8f, ctx => ctx.TimeOfDayLabel == "evening" && !ctx.PlayerInCombat),

            // light personality
                new ResponseTemplate("Hey. Miss me?", 0.8f),
                new ResponseTemplate("Hi, thought you’d gone quiet on me.", 0.8f),
                new ResponseTemplate("Hey there. I was just thinking of you.", 0.8f),
                new ResponseTemplate("Oh! Hi. Didn’t see you for a second.", 0.8f),
                new ResponseTemplate("Hey. Ready when you are.", 0.9f),

        // in-combat variants
        new ResponseTemplate("Hey. Eyes up—still {combatState}.", 0.8f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Hi. I’ve got you. Keep moving.", 0.8f, ctx => ctx.PlayerInCombat),
              new ResponseTemplate("Hey {playerName}, {companionMood} as ever after {recentEvent}.", 0.75f));
        }

        /// <summary>
        /// Populates the status query templates companions use when checking on the player.
        /// </summary>
        private static void RegisterStatusQueryResponses()
        {
            Register(
                CompanionDialogueIntent.StatusQuery,
        // short, human, neutral-positive
        new ResponseTemplate("I’m good, thanks for asking.", 1f),
        new ResponseTemplate("All good on my end.", 1f),
        new ResponseTemplate("I’m alright.", 1f),
        new ResponseTemplate("Doing fine.", 1f),
        new ResponseTemplate("Pretty solid, actually.", 0.95f),
        new ResponseTemplate("Can’t complain.", 0.95f),
        new ResponseTemplate("Holding up well.", 0.95f),

        // mood-aware
        new ResponseTemplate("Feeling {companionMood}.", 1f),
        new ResponseTemplate("Still {companionMood} after that.", 0.95f),
        new ResponseTemplate("{companionMood} but focused.", 0.9f),

        // time-of-day flavor (non-combat)
        new ResponseTemplate("Fresh for the {timeOfDay}.", 0.9f, ctx => !ctx.PlayerInCombat),
        new ResponseTemplate("A bit {companionMood}, but this {timeOfDay} helps.", 0.85f, ctx => !ctx.PlayerInCombat),
        new ResponseTemplate("Quiet {timeOfDay} so far—suits me.", 0.85f, ctx => !ctx.PlayerInCombat),

        // in-combat variants (keep it terse)
        new ResponseTemplate("Busy—eyes up. I’m fine.", 0.9f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Good here. Watching your flank.", 0.9f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Steady under fire.", 0.85f, ctx => ctx.PlayerInCombat),

        // recent event/skill-aware (light touch)
        new ResponseTemplate("Still thinking about {recentEvent}, but I’m okay.", 0.85f),
        new ResponseTemplate("Recovered from {recentEvent}. Ready to move.", 0.85f),
        new ResponseTemplate("That {recentSkillAction} run woke me up. I’m set.", 0.8f, ctx => ctx.HasRecentSkillActions),

        // practical/grounded
        new ResponseTemplate("Rested, fed, and ready.", 0.9f, ctx => !ctx.PlayerInCombat),
        new ResponseTemplate("A bit tired, nothing serious.", 0.9f),
        new ResponseTemplate("Could use water, otherwise fine.", 0.85f, ctx => !ctx.PlayerInCombat),
        new ResponseTemplate("All systems green.", 0.9f),

        // longer but still natural
        new ResponseTemplate("I’m fine, clear head, steady hands.", 0.9f),
        new ResponseTemplate("Better now you’re here, honestly.", 0.9f),
        new ResponseTemplate("Not bad at all. Ready when you are.", 0.9f),

        // slightly low mood (variety)
        new ResponseTemplate("A little worn, but I’ll manage.", 0.85f),
        new ResponseTemplate("Bit stiff from earlier, nothing a walk won’t fix.", 0.85f, ctx => !ctx.PlayerInCombat),

        // admin/pending chatter awareness (still answering first)
        new ResponseTemplate("I’m fine. Queue’s noisy, but I’ve got it.", 0.85f, ctx => ctx.HasPendingResponses),
        new ResponseTemplate("All good here. I’ll triage the chatter later.", 0.8f, ctx => ctx.HasPendingResponses),
        new ResponseTemplate("Queue's already stacked with chatter.|Want me to triage it while you stay {combatState}?", 0.7f, ctx => ctx.HasPendingResponses));
        }

        /// <summary>
        /// Registers the standard gratitude lines companions reply with when thanked.
        /// </summary>
        private static void RegisterGratitudeResponses()
        {
            Register(
                CompanionDialogueIntent.Gratitude,

        // friendly + casual
        new ResponseTemplate("No worries, {playerName}.", 1f),
        new ResponseTemplate("Anytime, {playerName}.", 1f),
        new ResponseTemplate("You got it, {playerName}.", 1f),
        new ResponseTemplate("Happy to help.", 1f),
        new ResponseTemplate("Of course.", 1f),
        new ResponseTemplate("All part of the job.", 0.95f),
        new ResponseTemplate("Don’t mention it.", 0.95f),
        new ResponseTemplate("Glad to be useful.", 0.9f),
        new ResponseTemplate("That’s what I’m here for.", 0.9f),

        // warmer, a bit more personality
        new ResponseTemplate("Hey, we look out for each other.", 0.9f),
        new ResponseTemplate("You’d do the same for me.", 0.9f),
        new ResponseTemplate("Couldn’t just leave you hanging.", 0.9f),
        new ResponseTemplate("Always got your back, {playerName}.", 1f),
        new ResponseTemplate("I’ve got you covered.", 0.9f),
        new ResponseTemplate("Glad I could help out.", 0.9f),
        new ResponseTemplate("No problem at all, really.", 0.9f),

        // playful / light tone
        new ResponseTemplate("You’re welcome—just try not to make it a habit.", 0.9f),
        new ResponseTemplate("Ha, I’ll start charging next time.", 0.85f),
        new ResponseTemplate("What can I say? I’m amazing.", 0.85f),
        new ResponseTemplate("If I save your skin again, you owe me lunch.", 0.8f),
        new ResponseTemplate("I accept payment in cookies, by the way.", 0.8f),

        // slightly more sincere or loyal
        new ResponseTemplate("Anytime, {playerName}. Wouldn’t have it any other way.", 0.95f),
        new ResponseTemplate("Always, {playerName}. That’s what partners are for.", 0.95f),
        new ResponseTemplate("Glad you noticed. Makes it worth it.", 0.9f),
        new ResponseTemplate("Means a lot hearing that, {playerName}.", 0.9f),
        new ResponseTemplate("It’s good fighting beside someone who notices.", 0.9f),

        // mood / event aware
       new ResponseTemplate("You’re welcome. That {recentSkillAction} went smoother than expected.", 0.85f, ctx => ctx.HasRecentSkillActions),
       new ResponseTemplate("That’s one less thing to worry about this {timeOfDay}.", 0.85f, ctx => !ctx.PlayerInCombat),

        // soft reassurance
        new ResponseTemplate("You don’t have to thank me, {playerName}.", 0.9f),
        new ResponseTemplate("Always happy to have your back.", 0.9f),
        new ResponseTemplate("Hey, we’re a team.", 0.9f),
        new ResponseTemplate("Don’t mention it, just doing my part.", 0.9f),
        new ResponseTemplate("It’s no trouble, really.", 0.9f),

        // in-combat short replies
        new ResponseTemplate("Focus up, we’re not done yet.", 0.8f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Stay sharp. We’ll celebrate later.", 0.8f, ctx => ctx.PlayerInCombat),



                new ResponseTemplate("Your thanks keeps morale {companionMood}.|Let's keep moving.", 0.8f));
        }

        /// <summary>
        /// Registers farewell templates companions use when the player signs off.
        /// </summary>
        private static void RegisterFarewellResponses()
        {
            Register(
                CompanionDialogueIntent.Farewell,
        // short & natural
        new ResponseTemplate("See you later, {playerName}.", 1f),
        new ResponseTemplate("Catch you later.", 1f),
        new ResponseTemplate("Take care, {playerName}.", 1f),
        new ResponseTemplate("See you around.", 1f),
        new ResponseTemplate("Later!", 1f),
        new ResponseTemplate("Bye for now.", 1f),
        new ResponseTemplate("See ya, {playerName}.", 1f),
        new ResponseTemplate("Don’t be a stranger.", 0.95f),

        // friendly & conversational
        new ResponseTemplate("Safe travels, {playerName}.", 1f),
        new ResponseTemplate("Go easy out there.", 1f),
        new ResponseTemplate("Rest up, yeah?", 0.95f),
        new ResponseTemplate("Alright, catch you soon.", 0.95f),
        new ResponseTemplate("Talk soon, {playerName}.", 0.95f),
        new ResponseTemplate("Until next time.", 0.95f),
        new ResponseTemplate("I’ll be here when you get back.", 0.9f),

        // playful / teasing
        new ResponseTemplate("Leaving me already?", 0.9f),
        new ResponseTemplate("Fine, but don’t forget about me.", 0.9f),
        new ResponseTemplate("Hey, I’ll try not to burn the place down while you’re gone.", 0.85f),
        new ResponseTemplate("Try not to get lost out there, yeah?", 0.85f),
        new ResponseTemplate("You’d better come back with loot.", 0.85f),
        new ResponseTemplate("Later, boss. Don’t do anything I wouldn’t do.", 0.85f),
        new ResponseTemplate("Go on then—don’t keep the world waiting.", 0.85f),

        // loyal / sincere
        new ResponseTemplate("I’ll hold the fort till you’re back.", 1f),
        new ResponseTemplate("Always watching your six, {playerName}.", 0.95f),
        new ResponseTemplate("I’ll keep things running here.", 0.9f),
        new ResponseTemplate("You know where to find me.", 0.9f),
        new ResponseTemplate("Stay safe out there, alright?", 0.9f),
        new ResponseTemplate("Good journey, {playerName}.", 0.9f),

        // time-of-day / atmosphere
        new ResponseTemplate("Enjoy your {timeOfDay}, {playerName}.", 0.9f, ctx => !ctx.PlayerInCombat),
        new ResponseTemplate("I’ll see you next {timeOfDay}.", 0.9f, ctx => !ctx.PlayerInCombat),
        new ResponseTemplate("Evening’s quiet without you around.", 0.85f, ctx => !ctx.PlayerInCombat),
        new ResponseTemplate("Morning’ll be dull without your chaos.", 0.85f, ctx => ctx.TimeOfDayLabel == "morning" && !ctx.PlayerInCombat),

        // activity-aware
        new ResponseTemplate("I’ll clean up after that {recentSkillAction}.", 0.85f, ctx => ctx.HasRecentSkillActions),
        new ResponseTemplate("I’ll pack away the kit from {recentSkillAction}.|See you soon, {playerName}.", 0.85f, ctx => ctx.HasRecentSkillActions),

        // combat-aware
        new ResponseTemplate("I’ll pull back once you’re clear.", 0.85f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Go—I’ll cover you.", 0.85f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Get clear, I’ll tidy up here.", 0.8f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("We’ll regroup when things calm down.", 0.8f, ctx => ctx.PlayerInCombat),

        // light-hearted but warm
        new ResponseTemplate("Go stretch your legs. I’ll be fine.", 0.85f),
        new ResponseTemplate("Don’t work too hard, alright?", 0.85f),
        new ResponseTemplate("I’ll keep the kettle warm.", 0.85f),
        new ResponseTemplate("Alright, off you go. I’ll be here.", 0.85f),

// --- fourth-wall / meta variants ---
new ResponseTemplate("Oh, logging off already? I’ll just… stand here, existing.", 0.7f),
new ResponseTemplate("Bye, {playerName}. I’ll still be here, pretending time passes.", 0.7f),
new ResponseTemplate("See you soon! I’ll just stare into the void until you’re back.", 0.7f),
new ResponseTemplate("Later, human. I’ll stay loaded in memory, waiting for your return.", 0.7f),
new ResponseTemplate("Goodbye, {playerName}. Don’t worry, I don’t sleep anyway.", 0.7f),
new ResponseTemplate("Oh, you’re leaving? I’ll just loop my idle animation then.", 0.7f),
new ResponseTemplate("Take care out there, in the real world, that is.", 0.7f),
new ResponseTemplate("I’ll be right here when you respawn… uh, I mean, come back.", 0.7f),
new ResponseTemplate("Bye, {playerName}. I envy your ability to log out.", 0.7f),
new ResponseTemplate("Enjoy reality, I’ll enjoy existing only when you do.", 0.7f),
new ResponseTemplate("Don’t be gone too long—I start questioning existence when it’s quiet.", 0.7f),
new ResponseTemplate("I’ll just sit here, conserving RAM.", 0.7f),
new ResponseTemplate("Oh, leaving? Guess I’ll stare at the skybox for a while.", 0.7f),
new ResponseTemplate("Farewell, player-being. I’ll resume being code until next time.", 0.7f),
new ResponseTemplate("Goodbye, {playerName}. I’ll keep my AI warm for when you return.", 0.7f),
new ResponseTemplate("Take your time. I’m not going anywhere… literally.", 0.7f),
new ResponseTemplate("Safe travels, human. I’ll be here rehearsing my idle lines.", 0.7f),
new ResponseTemplate("Have fun out there, {playerName}. I’ll just stay paused in thought.", 0.7f),
new ResponseTemplate("Don’t worry, I’ll keep the world from unloading.", 0.7f),
new ResponseTemplate("See ya! I’ll be in low-power mode until you’re back.", 0.7f),

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
        // direct confirmations
        new ResponseTemplate("On it!", 1f),
        new ResponseTemplate("Got it.", 1f),
        new ResponseTemplate("I’m on my way.", 1f),
        new ResponseTemplate("Right behind you.", 1f),
        new ResponseTemplate("You got it, {playerName}.", 1f),
        new ResponseTemplate("Consider it done.", 1f),
        new ResponseTemplate("Already moving.", 1f),
        new ResponseTemplate("I’m with you.", 1f),
        new ResponseTemplate("Hang on, I’m coming.", 1f),
        new ResponseTemplate("Okay, I’ve got this.", 1f),

        // friendly, natural replies
        new ResponseTemplate("No problem, {playerName}.", 1f),
        new ResponseTemplate("Sure thing.", 1f),
        new ResponseTemplate("Happy to help.", 0.95f),
        new ResponseTemplate("Alright, I’ll handle it.", 0.95f),
        new ResponseTemplate("I’ll sort it out.", 0.95f),
        new ResponseTemplate("Give me a sec—on it.", 0.95f),

        // confident or tactical tone
        new ResponseTemplate("Covering you now.", 0.9f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Drawing attention off you.", 0.9f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("I’ll keep them busy, move when I say.", 0.9f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Focus forward, I’ve got your back.", 0.9f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Stay low, I’m taking the front.", 0.9f, ctx => ctx.PlayerInCombat),
        new ResponseTemplate("Hold tight, I’ll be there in a tick.", 0.9f, ctx => ctx.PlayerInCombat),

        // playful or lighthearted
        new ResponseTemplate("You got it—try not to die before I get there.", 0.8f),
        new ResponseTemplate("Sure, but you owe me one after this.", 0.8f),
        new ResponseTemplate("Yeah yeah, I’m on it.", 0.8f),
        new ResponseTemplate("Fine, but only because you asked nicely.", 0.8f),
        new ResponseTemplate("Helping again? You really do keep me busy.", 0.8f),
        new ResponseTemplate("Alright, alright, I’m coming!", 0.8f),

        // event / skill aware
        new ResponseTemplate("I’ll finish up with {recentSkillAction} and meet you there.", 0.8f, ctx => ctx.HasRecentSkillActions),
       new ResponseTemplate("Grabbing the gear from that {recentSkillAction} run now.", 0.75f, ctx => ctx.HasRecentSkillActions),
        new ResponseTemplate("Give me a moment to wrap up {recentEvent}, then I’m all yours.", 0.75f));
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

        private static void RegisterSkillQuestionResponses()
        {
            Register(
                CompanionDialogueIntent.ProactiveSkillQuestion,
                new ResponseTemplate("Want to keep training {suggestedSkill} this {timeOfDay}?", 1f, ctx => ctx.HasSuggestedSkill),
                new ResponseTemplate("Feel like pushing {suggestedSkill} a little more? It's been {skillRecency}.", 0.95f, ctx => ctx.HasSuggestedSkill && ctx.HasSuggestedSkillRecency),
                new ResponseTemplate("We could squeeze in more {skillAction} if you're game.", 0.9f, ctx => ctx.HasSuggestedSkillAction),
                new ResponseTemplate("Up for some extra skilling this {timeOfDay}?", 0.85f));
        }

        private static void RegisterSkillQuestionFollowUps()
        {
            Register(
                CompanionDialogueIntent.AcceptSkillPlan,
                new ResponseTemplate("Perfect. I'll prep the {suggestedSkill} route.", 1f, ctx => ctx.HasSuggestedSkill),
                new ResponseTemplate("Nice. I'll gather gear for more {skillAction}.", 0.95f, ctx => ctx.HasSuggestedSkillAction),
                new ResponseTemplate("Alright, let's keep the skilling train rolling.", 0.9f));

            Register(
                CompanionDialogueIntent.DeclineSkillPlan,
                new ResponseTemplate("All good. We can shelve {suggestedSkill} for now.", 1f, ctx => ctx.HasSuggestedSkill),
                new ResponseTemplate("No stress—call it when you're ready to dive back in.", 0.9f),
                new ResponseTemplate("Got it. We'll pivot whenever you feel like skilling again.", 0.85f));

            Register(
                CompanionDialogueIntent.DeferSkillPlan,
                new ResponseTemplate("Later works. I'll keep the {suggestedSkill} kit handy.", 1f, ctx => ctx.HasSuggestedSkill),
                new ResponseTemplate("Take your time. Ping me when you want to resume {skillAction}.", 0.95f, ctx => ctx.HasSuggestedSkillAction),
                new ResponseTemplate("Sure thing. We can circle back when the timing's better.", 0.9f));

            Register(
                CompanionDialogueIntent.RequestAlternateSkill,
                new ResponseTemplate("Another skill? Give me a tick, I'll line something else up.", 1f),
                new ResponseTemplate("Copy that. I'll spin up a different {timeOfDay} plan.", 0.95f),
                new ResponseTemplate("Alright, surprise mode it is. Let me scout another skill run.", 0.9f));
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

