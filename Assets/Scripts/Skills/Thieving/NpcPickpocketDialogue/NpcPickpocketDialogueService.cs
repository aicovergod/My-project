using Skills.Thieving.Data;
using UI.Chat;
using UnityEngine;

namespace Skills.Thieving.NpcPickpocketDialogue
{
    /// <summary>
    ///     Centralised helper that emits flavour dialogue to the chat service when NPC pickpockets
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
        /// <param name="success">True when the pickpocket resolved successfully.</param>
        public static void TryPublishDialogue(ThievingNpcDefinition definition, bool success)
        {
            if (definition == null)
                return;

            if (!NpcPickpocketDialogueSet.TryGet(definition.Id, out var set))
                return;

            if (success)
            {
                if (!ShouldEmitSuccessLine())
                    return;

                if (!set.TryGetRandomSuccessLine(out string line))
                    return;

                Publish(definition.DisplayName, line);
                return;
            }

            if (!set.TryGetRandomFailureLine(out string failureLine))
                return;

            Publish(definition.DisplayName, failureLine);
        }

        /// <summary>
        ///     Performs the 1-in-20 roll specified for success dialogue.
        /// </summary>
        private static bool ShouldEmitSuccessLine()
        {
            return Random.Range(0, SuccessDialogueDenominator) == 0;
        }

        /// <summary>
        ///     Formats and publishes the resolved dialogue line to the Game channel.
        /// </summary>
        private static void Publish(string speaker, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var chatService = ChatService.Instance;
            if (chatService == null)
                return;

            string resolvedSpeaker = string.IsNullOrWhiteSpace(speaker) ? "NPC" : speaker.Trim();
            chatService.PublishGameMessage($"{resolvedSpeaker}: {line}");
        }
    }
}
