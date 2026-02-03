using System;

namespace Companions.Conversation
{
    /// <summary>
    /// Enumerates the conversational intents a companion can recognise when parsing player chat.
    /// Intents are ordered by increasing priority so lower values are processed first when composing responses.
    /// </summary>
    public enum CompanionDialogueIntent
    {
        /// <summary>
        /// Player opened the exchange with a friendly greeting.
        /// </summary>
        Greeting = 0,

        /// <summary>
        /// Lightweight chatter that keeps the conversation warm between major topics.
        /// </summary>
        SmallTalk = 5,

        /// <summary>
        /// Player asked how the companion is doing or requested a status update.
        /// </summary>
        StatusQuery = 10,

        /// <summary>
        /// Player asked about the companion's level in a specific skill.
        /// </summary>
        SkillLevelQuery = 15,

        /// <summary>
        /// Player thanked the companion for their help.
        /// </summary>
        Gratitude = 20,

        /// <summary>
        /// Player is saying farewell or stepping away.
        /// </summary>
        Farewell = 30,

        /// <summary>
        /// Player complimented the companion or offered positive reinforcement.
        /// </summary>
        Compliment = 40,

        /// <summary>
        /// Player is asking the companion for help with an upcoming task.
        /// </summary>
        RequestAssistance = 50,

        /// <summary>
        /// Player proposed tackling a specific skill activity together.
        /// </summary>
        PlayerSkillProposal = 55,

        /// <summary>
        /// Player referenced a prior shared event that deserves acknowledgement.
        /// </summary>
        AcknowledgeRecentEvent = 60,

        /// <summary>
        /// Companion is proactively prompting the player about a recent skill activity.
        /// </summary>
        ProactiveSkillQuestion = 65,

        /// <summary>
        /// Player accepted the companion's suggested skill plan.
        /// </summary>
        AcceptSkillPlan = 70,

        /// <summary>
        /// Player declined the companion's suggested skill plan.
        /// </summary>
        DeclineSkillPlan = 80,

        /// <summary>
        /// Player deferred the suggested skill plan to a later time.
        /// </summary>
        DeferSkillPlan = 90,

        /// <summary>
        /// Player requested a different skill suggestion from the companion.
        /// </summary>
        RequestAlternateSkill = 100,

        /// <summary>
        /// Player asked the companion what they would like to train or do next.
        /// </summary>
        CompanionSuggestionRequest = 110,

        /// <summary>
        /// Player asked the companion to repeat their last activity suggestion.
        /// </summary>
        CompanionSuggestionReminder = 120,

        /// <summary>
        /// Player apologised to the companion, either after a reminder or without prompting.
        /// </summary>
        PlayerApology = 130
    }
}
