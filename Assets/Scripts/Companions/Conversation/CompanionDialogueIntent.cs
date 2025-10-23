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
        /// Player asked how the companion is doing or requested a status update.
        /// </summary>
        StatusQuery = 10,

        /// <summary>
        /// Player shared a personal mood update such as "I'm tired".
        /// </summary>
        PlayerMoodReport = 20,

        /// <summary>
        /// Player thanked the companion for their help.
        /// </summary>
        Gratitude = 30,

        /// <summary>
        /// Player is saying farewell or stepping away.
        /// </summary>
        Farewell = 40,

        /// <summary>
        /// Player complimented the companion or offered positive reinforcement.
        /// </summary>
        Compliment = 50,

        /// <summary>
        /// Player is asking the companion for help with an upcoming task.
        /// </summary>
        RequestAssistance = 60,

        /// <summary>
        /// Player referenced a prior shared event that deserves acknowledgement.
        /// </summary>
        AcknowledgeRecentEvent = 70
    }
}
