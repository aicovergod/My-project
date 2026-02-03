using System.Collections.Generic;

namespace Companions.Conversation
{
    /// <summary>
    /// Provides curated pools of short-form small-talk snippets that keep the companion
    /// conversation lively when no pressing topic is queued. Separated from the main
    /// response catalog so proactive chatter can draw from bespoke flavour text without
    /// bloating the shared response registry.
    /// </summary>
    internal static class CompanionSmallTalkDialogueBlocks
    {
        internal enum SmallTalkCategory
        {
            TimeOfDay,
            Location,
            Memory
        }

        internal readonly struct SmallTalkEntry
        {
            public SmallTalkEntry(SmallTalkCategory category, string template, float weight = 1f)
            {
                Category = category;
                Template = template;
                Weight = weight;
            }

            public SmallTalkCategory Category { get; }

            public string Template { get; }

            public float Weight { get; }
        }

        private static readonly SmallTalkEntry[] timeOfDayEntries =
        {
            new SmallTalkEntry(SmallTalkCategory.TimeOfDay, "This {timeOfDay} light makes everything look softer."),
            new SmallTalkEntry(SmallTalkCategory.TimeOfDay, "Quiet {timeOfDay} like this makes me glad we took a breather.", 0.95f),
            new SmallTalkEntry(SmallTalkCategory.TimeOfDay, "Feels like the whole world is stretching with this {timeOfDay} breeze.", 0.9f),
            new SmallTalkEntry(SmallTalkCategory.TimeOfDay, "You hear how {timeOfDay} settles the critters down? Nice change.", 0.85f),
            new SmallTalkEntry(SmallTalkCategory.TimeOfDay, "If every {timeOfDay} felt like this, I'd never complain.", 0.85f)
        };

        private static readonly SmallTalkEntry[] locationEntries =
        {
            new SmallTalkEntry(SmallTalkCategory.Location, "{ambientLocation} has its own rhythm. Easy to get lost in it."),
            new SmallTalkEntry(SmallTalkCategory.Location, "Pretty sure {ambientLocation} is growing on me.", 0.95f),
            new SmallTalkEntry(SmallTalkCategory.Location, "You smell that? {ambientLocation} always carries something wild on the wind.", 0.9f),
            new SmallTalkEntry(SmallTalkCategory.Location, "Hard to stay tense when {ambientLocation} looks this calm.", 0.85f),
            new SmallTalkEntry(SmallTalkCategory.Location, "Let's remember this view of {ambientLocation} next time we're knee-deep in trouble.", 0.8f)
        };

        private static readonly SmallTalkEntry[] memoryEntries =
        {
            new SmallTalkEntry(SmallTalkCategory.Memory, "Still laughing about {memorySummary}. Moments like that keep me going."),
            new SmallTalkEntry(SmallTalkCategory.Memory, "I keep replaying {memorySummary}. It was a good call.", 0.95f),
            new SmallTalkEntry(SmallTalkCategory.Memory, "Wild to think {memorySummary} happened just a little while ago.", 0.9f),
            new SmallTalkEntry(SmallTalkCategory.Memory, "Whenever it quiets down, my mind jumps back to {memorySummary}.", 0.85f),
            new SmallTalkEntry(SmallTalkCategory.Memory, "Next time we're tired, remind me about {memorySummary}. Works better than coffee.", 0.8f)
        };

        public static IReadOnlyList<SmallTalkEntry> TimeOfDayEntries => timeOfDayEntries;

        public static IReadOnlyList<SmallTalkEntry> LocationEntries => locationEntries;

        public static IReadOnlyList<SmallTalkEntry> MemoryEntries => memoryEntries;
    }
}
