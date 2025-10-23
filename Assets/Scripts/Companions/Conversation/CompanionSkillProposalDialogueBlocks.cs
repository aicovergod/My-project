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
            "I've got the {tool} ready—let's get back to {skillSentence}.",
            "Perfect timing. The {tool} has been itching for more {skillSentence}.",
            "Happy to! The {tool} is sharpened and waiting for us to {activity}.",
            "Yeah, let's swing the {tool} and keep that {skillSentence} streak rolling.",
            "Love that plan. I'll grab the {tool} and we can start {activity} right away.",
            "Consider me in. The {tool} is still warm from earlier {skillSentence} runs.",
            "Absolutely, let's dust off the {tool} and go {activity} together.",
            "Great shout—I've already packed the {tool} for more {skillSentence}.",
            "That's the spirit. The {tool} and I are ready to {activity} again.",
            "Say no more. The {tool} never left my side; {skillSentence} awaits.",
            "Heh, you read my mind. The {tool} is prepped for a fresh round of {skillSentence}.",
            "All right, I'll tighten my grip on the {tool} and lead us into some {skillSentence}."
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
            "Lead the way and I'll keep the {tool} swinging.",
            "I'll stow anything we gather so you can stay light on your feet.",
            "If we hit a juicy vein I'll call it out straight away.",
            "Let's pace it—no sense dulling the {tool} on stray rocks.",
            "I'll watch our surroundings while you line up the next target.",
            "I'll bank anything extra once our packs start to fill.",
            "I'll keep track of the good nodes we find for later runs.",
            "Shout if you spot anything rare; I'll break it open with the {tool}.",
            "I'll double-check the {tool} between swings so we don't lose rhythm.",
            "I'll handle the heavy lifting so you can focus on the big finds."
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

        private static readonly string[] playerSkillProposalMissingToolSegments =
        {
            "I'd love to, but I'm missing {indefiniteTool} right now.",
            "Tempting offer, yet we stashed our {definiteTool}. We'll need to fetch it first.",
            "I'm game for {skillSentence}, though our kit lacks {indefiniteTool} at the moment.",
            "I could, but without {indefiniteTool} we'd just bruise knuckles.",
            "Let's pencil it in once we recover {definiteTool} from storage.",
            "Count me interested—but the last {definiteTool} snapped, remember?",
            "I'd say yes if we had {indefiniteTool}; we're travelling light.",
            "Give me time to replace {definiteTool} and I'm all yours for {skillSentence}.",
            "Can't swing {skillSentence} until we secure {indefiniteTool} again.",
            "If we make a quick stop for {indefiniteTool}, I'm in.",
            "Our pack is empty of {toolPlural}. Let's restock before we commit.",
            "I'm running a little under-geared—no {definiteTool} means no {skillSentence} just yet."
        };

        private static readonly string[] playerSkillProposalMissingToolFollowUps =
        {
            "Want me to check the bank later for a spare?",
            "We could swing by the workshop and pick one up first.",
            "If you spot a vendor, let's grab {indefiniteTool} before we forget.",
            "I'll keep an ear out for drops that match what we need.",
            "Maybe we craft one after this fight? Could be a good project.",
            "Let me mark it on our to-do list so we don't miss the chance.",
            "I'll ping you once I've tracked down {indefiniteTool}.",
            "We can always pivot to something else until we're re-equipped.",
            "I'll empty some space so we can carry a fresh {definiteTool} next time.",
            "Let's talk to the smithy after this and see what they can do."
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
