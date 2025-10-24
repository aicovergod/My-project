using System.Collections.Generic;

namespace Companions.Conversation
{
    /// <summary>
    /// Centralises the companion suggestion dialogue so <see cref="CompanionConversationService"/>
    /// stays focused on orchestration and runtime logic.
    /// </summary>
    public static class CompanionSuggestionDialogueBlocks
    {
        /// <summary>
        /// Templates used when the companion proactively suggests a skill to train.
        /// </summary>
        private static readonly string[] skillSuggestionTemplates =
        {
            "How about we do some {skill}?",
            "I want to do some {skill}.",
            "I want to get my {skill} level up.",
            "I feel like training some {skill}.",
            "I feel like getting my {skill} up."
        };

        /// <summary>
        /// Responses surfaced when the player repeatedly asks for suggestions inside the cooldown window.
        /// </summary>
        private static readonly string[] repeatSuggestionResponses =
        {
            "I've already told you what I want to do.",
            "You've already asked me.",
            "I told you earlier.",
            "I'm not a parrot lol, I told you earlier <emoji=14>.",
            "Have you seriously forgot {playerName} <emoji=18>."
        };

        /// <summary>
        /// Follow-up reminders referencing the previously suggested skill.
        /// </summary>
        private static readonly string[] skillReminderResponses =
        {
            "We talked about training more {skill} earlier.",
            "I already said I want to work on {skill}.",
            "Still keen to push my {skill} level."
        };

        /// <summary>
        /// Follow-up reminders referencing the previously suggested NPC target.
        /// </summary>
        private static readonly string[] npcReminderResponses =
        {
            "We said we'd take down more {npc}.",
            "Pretty sure we were hunting {npc}.",
            "I told you I'm itching to fight more {npc}."
        };

        /// <summary>
        /// Template used when the companion highlights the most recent NPC kill during a suggestion.
        /// </summary>
        private const string npcLatestTemplate = "Maybe we can kill some more {npc}.";

        /// <summary>
        /// Template used when the companion references the broader NPC kill history during a suggestion.
        /// </summary>
        private const string npcRandomTemplate = "I want to kill more {npc}.";

        /// <summary>
        /// Gets the proactive skill suggestion templates.
        /// </summary>
        public static IReadOnlyList<string> SkillSuggestionTemplates => skillSuggestionTemplates;

        /// <summary>
        /// Gets the responses used when the player repeats a suggestion request.
        /// </summary>
        public static IReadOnlyList<string> RepeatSuggestionResponses => repeatSuggestionResponses;

        /// <summary>
        /// Gets the reminder segments for previously suggested skills.
        /// </summary>
        public static IReadOnlyList<string> SkillReminderResponses => skillReminderResponses;

        /// <summary>
        /// Gets the reminder segments for previously suggested NPCs.
        /// </summary>
        public static IReadOnlyList<string> NpcReminderResponses => npcReminderResponses;

        /// <summary>
        /// Gets the template used for the latest NPC kill reminder.
        /// </summary>
        public static string NpcLatestTemplate => npcLatestTemplate;

        /// <summary>
        /// Gets the template used for random NPC history reminders.
        /// </summary>
        public static string NpcRandomTemplate => npcRandomTemplate;
    }
}
