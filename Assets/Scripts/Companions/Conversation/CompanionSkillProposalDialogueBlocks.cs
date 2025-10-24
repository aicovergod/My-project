namespace Companions.Conversation
{
    /// <summary>
    /// Provides the static dialogue templates used when the companion responds to player-driven skill proposals.
    /// Splitting these blocks into a dedicated class keeps <see cref="CompanionConversationService"/> focused on
    /// orchestration logic while still exposing strongly-typed collections for the response builder.
    /// </summary>
    internal static class CompanionSkillProposalDialogueBlocks
    {
        // The following arrays are intentionally cached to avoid per-message allocations while still supplying
        // flavourful dialogue variations that match the companion's current context.

        private static readonly string[] playerMiningProposalReadyWithPickaxeSegments =
        {
            "Yeah, I’m in. Let’s head out.",
            "Absolutely. A bit of {skillSentence} sounds perfect.",
            "You know what? That actually sounds nice.",
            "Sure thing. Haven’t done any {skillSentence} in a while.",
            "Good idea. Let’s see what we can dig up.",
            "You read my mind. I was getting restless.",
    "I’m down. Let’s make some noise.",
    "Sounds like a plan—lead the way.",
    "Love that idea. Let’s get to it.",
    "That’s my kind of break. Let’s move.",
    "Fine by me. Something relaxing about {activity}.",
    "Couldn’t agree more. Let’s stretch the legs.",
    "Alright then—let’s earn some dust on our boots.",
    "Nice. Haven’t seen a decent vein in a bit.",
    "If we find anything shiny, I’m claiming bragging rights.",
    "Good call. Bit of {activity} never hurt anyone.",
    "Sweet. Let’s go make some sparks fly.",
    "Yeah, why not? Beats standing around.",
    "Alright, let’s go swing at some rocks.",
    "You got it. I’ll follow your lead."
        };

        private static readonly string[] playerSkillProposalReadyGenericSegments =
        {
            "Let's do it—more {skillSentence} sounds perfect right now.",
            "I'm on board. A fresh round of {skillSentence} will hit the spot.",
            "Count me in for {skillSentence}; I'm ready when you are.",
            "Great idea, {playerName}. Let's dive back into {skillSentence}.",
            "Absolutely—I've been itching for more {skillSentence}.",
            "Love that vibe. Let's spend the next while {activity}.",
            "You know it. {skillSentence} is exactly what I was thinking.",
            "Ready and willing. Point me toward the next {skillSentence} spot.",
            "Fantastic suggestion. We can start {activity} right away.",
            "Consider it done; {skillSentence} session coming right up.",
            "I'm game. Let's make this {skillSentence} run memorable.",
            "Heck yes, more {skillSentence} with you is my kind of plan."
        };

        private static readonly string[] playerMiningProposalReadyWithToolFollowUps =
        {
                "You lead, my pickaxe is ready.",
                "I’ll call out any good spots I see.",
    "Let’s try not to get lost this time, yeah?",
    "If the ground starts shaking, I’m blaming you.",
    "We’ll make quick work of this.",
    "I’ll stash whatever we dig up later.",
    "If you see a glint, shout—it’s probably worth it.",
    "Let’s keep an eye out for the good stuff.",
    "Let's hope we don't bump into any ore golems.",
    "I’ll handle the boring chunks, you take the shiny ones.",
    "Don’t worry, I’ll cover you if theres ore golems.",
    "We’ll go steady. No point rushing a good find.",
    "I’ll mark any rich veins for later runs.",
    "Feels like a lucky day for it.",
    "If it gets too quiet, that’s usually a bad sign.",
    "I’ll sort what we gather once we’re done.",
    "Let’s make it a good run."
        };

        private static readonly string[] playerSkillProposalReadyGenericFollowUps =
        {
            "Call out the spot you like and I'll back you up.",
            "I'll log the gains in case we want to brag later.",
            "We can swap tasks mid-run if you need a breather.",
            "I'll keep an eye on stray hostiles while we work.",
            "Let's keep the momentum rolling with quick hops between spots.",
            "We should stash anything rare before anyone else notices.",
            "I'll mark our route so we can repeat it tomorrow.",
            "If you want to split duties, just say the word.",
            "I'll shout if I notice better opportunities nearby.",
            "Let's keep the chatter going—makes the grind faster."
        };

        private static readonly string[] playerSkillProposalDeclineSegments =
        {
            "No thanks, I'd rather not.",
            "I'll pass for now, thanks.",
            "I don't really feel like doing {skillName} right now.",
            "No thank you, {playerName}.",
            "Not in the mood for {skillName} today, sorry.",
            "Think I'll sit this one out.",
            "Nah, I'm good. You go ahead.",
            "Maybe later, just not feeling {skillName} at the moment.",
            "I've had my fill of {skillName} for the day.",
            "I'd rather do something else right now, if that's alright.",
            "You're welcome to, but I'll watch from the sidelines this time.",
            "Not this time, {playerName}. My energy's somewhere else.",
            "I'd love to, but my heart's not in it today.",
            "Appreciate the invite, but I'll give {skillName} a miss.",
            "Eh, not really feeling it. Maybe later."
        };

        private static readonly string[] playerSkillProposalDeclineSuggestionSegments =
        {
            "I wouldn't mind training {suggestedSkillName} though.",
            "How about we train {suggestedSkillName}?",
            "We could train some {suggestedSkillName} if you're up for it.",
            "I feel like training some {suggestedSkillName} though.",
            "Can we not train some {suggestedSkillName}?"
        };

        private static readonly string[] playerSkillProposalMissingToolSegments =
        {
    "Ah, I would, but I don’t have a pickaxe right now.",
    "Tempting, but we left the pickaxe behind, didn’t we?",
    "I'd be up for it, if only we had a pickaxe.",
    "No pickaxe, no mining. Simple as that.",
    "I'd love to, but the pickaxe didn’t make it into the backpack.",
    "Dang, forgot to bring the pickaxe again.",
    "I'd be all for it, but unless you plan for me to mine with my hands...",
    "Hold that thought. We still need a pickaxe.",
    "I’m game once we grab my pickaxe.",
    "Wish I could, but I’m not exactly equipped for mining right now.",
    "I kinda need a pickaxe for that. Minor detail.",
    "If only I had my pickaxe, we’d be golden.",
    "Hate to break it to you, but the pickaxe is MIA.",
    "Can’t exactly mine without a pickaxe, can we?",
    "Give me a proper pickaxe and I’m in.",
    "Maybe after we pick up a new pickaxe.",
    "Let's circle back once we've got the right gear.",
    "As much as I’d like to, I’m running a little light on tools.",
    "You’d think I’d remember to bring a pickaxe by now.",
    "Missing a small thing called a pickaxe. Slight issue.",
    "How about after we fix that whole ‘no pickaxe’ problem?",
    "I left the pickaxe back at camp again, didn’t I?",
    "Sorry, I’m useless without a pickaxe for this one.",
    "No pickaxe, no progress. My bad.",
    "If only enthusiasm could replace a pickaxe...",
    "Let’s not embarrass ourselves, grab a pickaxe first."
        };

        private static readonly string[] playerSkillProposalMissingToolFollowUps =
        {
    "Want me to check the bank later for a spare?",
    "We can swing by a shop and grab one on the way.",
    "If you see a vendor, remind me to pick up a pickaxe.",
    "Could craft one pretty quick if we find the bits for it.",
    "Might be worth checking the bank when we head back.",
    "I’ll keep an eye out for a decent pickaxe.",
    "We can always switch to something else for now.",
    "Next time we pass a forge, I’ll sort it out.",
    "Let’s grab a new one next stop we make.",
    "We could always borrow one, temporarily, of course.",
    "I'll see if anyone nearby’s selling a pickaxe.",
    "Might be a good excuse to visit the workshop anyway.",
    "If I spot one lying around, I’ll call dibs.",
    "Let’s craft a better one this time, yeah?",
    "We’ll find a pickaxe soon enough, don’t sweat it.",
    "Could ask the smithy to patch something together.",
    "We’ll get a fresh pickaxe soon; no rush."
        };

        private static readonly string[] playerSkillProposalAlternateSkillSegments =
        {
            "What if we lean into {alternateDescription} instead? It suits what we've been doing.",
            "I'm short on gear for that, but {alternateName} could be a solid pivot.",
            "Maybe we swap to {alternateDescription}—we're already warmed up for it.",
            "How about {alternateName}? It keeps our streak alive without waiting on supplies.",
            "We could chase {alternateDescription} while we prep for your idea.",
            "Until we're retooled, {alternateName} might be the smarter grind.",
            "I'd vote for {alternateDescription} in the meantime; faster to jump into.",
            "Could we do {alternateName} first? Gives me time to fetch the rest of the gear.",
            "Let's bank this plan and run {alternateDescription} as a warm-up.",
            "Maybe {alternateName} scratches the same itch without the equipment scramble.",
            "If you're cool with it, {alternateDescription} is ready to go right now.",
            "We can circle back after some {alternateName}; it's on my radar anyway."
        };

        private static readonly string[] playerSkillProposalAlternateSkillFollowUps =
        {
            "If that works, I'll schedule a reminder to grab the right tool later.",
            "We can swap back the moment we're re-equipped.",
            "I'll keep logging prospects for your original plan while we pivot.",
            "Give the word when you'd rather return to your idea.",
            "I'll note the cooldown so we don't forget to revisit it.",
            "We'll still chase your plan soon—I promise.",
            "I'll stash our finds so they're ready for the real run later.",
            "Meanwhile I'll send feelers for anyone selling the gear we need.",
            "I'll mark the spot so we can resume without missing a beat.",
            "Let's treat this as prep work; the main event comes once we're geared."
        };

        /// <summary>
        /// Dialogue lines the companion uses when they are prepared to mine alongside the player.
        /// </summary>
        internal static string[] PlayerMiningProposalReadyWithPickaxeSegments => playerMiningProposalReadyWithPickaxeSegments;

        /// <summary>
        /// Generic dialogue used when the companion agrees to a skill session and no mining-specific gear applies.
        /// </summary>
        internal static string[] PlayerSkillProposalReadyGenericSegments => playerSkillProposalReadyGenericSegments;

        /// <summary>
        /// Follow-up lines that add flavour when the mining kit is available.
        /// </summary>
        internal static string[] PlayerMiningProposalReadyWithToolFollowUps => playerMiningProposalReadyWithToolFollowUps;

        /// <summary>
        /// Follow-up lines used after a generic affirmative skill response.
        /// </summary>
        internal static string[] PlayerSkillProposalReadyGenericFollowUps => playerSkillProposalReadyGenericFollowUps;

        /// <summary>
        /// Primary responses used when the companion declines the player's training request.
        /// </summary>
        internal static string[] PlayerSkillProposalDeclineSegments => playerSkillProposalDeclineSegments;

        /// <summary>
        /// Follow-up suggestions the companion may offer after declining.
        /// </summary>
        internal static string[] PlayerSkillProposalDeclineSuggestionSegments => playerSkillProposalDeclineSuggestionSegments;

        /// <summary>
        /// Primary responses that explain the companion is missing the required tool.
        /// </summary>
        internal static string[] PlayerSkillProposalMissingToolSegments => playerSkillProposalMissingToolSegments;

        /// <summary>
        /// Follow-up suggestions when the companion lacks the proper tool.
        /// </summary>
        internal static string[] PlayerSkillProposalMissingToolFollowUps => playerSkillProposalMissingToolFollowUps;

        /// <summary>
        /// Primary responses that steer the player toward an alternate training option.
        /// </summary>
        internal static string[] PlayerSkillProposalAlternateSkillSegments => playerSkillProposalAlternateSkillSegments;

        /// <summary>
        /// Follow-up suggestions that complement the alternate skill proposals.
        /// </summary>
        internal static string[] PlayerSkillProposalAlternateSkillFollowUps => playerSkillProposalAlternateSkillFollowUps;
    }
}
