using Skills.Thieving.Data;
using UnityEngine;
using World;

namespace Skills.Thieving.NpcPickpocketDialogue
{
    /// <summary>
    ///     Centralised helper that emits flavour dialogue above the NPC when pickpocket attempts
    ///     succeed or fail. The service looks up <see cref="NpcPickpocketDialogueSet"/> instances
    ///     using the NPC definition identifier and handles the success roll odds internally so
    ///     gameplay code can remain focused on XP, loot and stun logic.
    /// </summary>
    internal static class NpcPickpocketDialogueService
    {
        private const int SuccessDialogueDenominator = 20;

        /// <summary>
        ///     Attempts to emit a pickpocket dialogue line for the supplied definition.
        /// </summary>
        /// <param name="definition">Definition describing the NPC being pickpocketed.</param>
        /// <param name="dialogueAnchor">Transform used to position the floating dialogue.</param>
        /// <param name="success">True when the pickpocket resolved successfully.</param>
        public static void TryPublishDialogue(ThievingNpcDefinition definition, Transform dialogueAnchor, bool success)
        {
            if (definition == null || dialogueAnchor == null)
                return;

            if (!NpcPickpocketDialogueSet.TryGet(definition.Id, out var set))
                return;

            if (success)
            {
                if (!ShouldEmitSuccessLine())
                    return;

                if (!set.TryGetRandomSuccessLine(out string line))
                    return;

                Publish(definition.DisplayName, dialogueAnchor, line);
                return;
            }

            if (!set.TryGetRandomFailureLine(out string failureLine))
                return;

            Publish(definition.DisplayName, dialogueAnchor, failureLine);
        }

        /// <summary>
        ///     Performs the 1-in-20 roll specified for success dialogue.
        /// </summary>
        private static bool ShouldEmitSuccessLine()
        {
            return Random.Range(0, SuccessDialogueDenominator) == 0;
        }

        /// <summary>
        ///     Formats and displays the resolved dialogue line above the NPC.
        /// </summary>
        private static void Publish(string speaker, Transform anchor, string line)
        {
            if (anchor == null || string.IsNullOrWhiteSpace(line))
                return;

            string resolvedSpeaker = string.IsNullOrWhiteSpace(speaker) ? "NPC" : speaker.Trim();
            PopupText.Show($"{resolvedSpeaker}: {line}", anchor);
        }
    }
}
