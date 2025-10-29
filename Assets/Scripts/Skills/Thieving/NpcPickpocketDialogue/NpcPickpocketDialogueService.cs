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
        private const string LogPrefix = "[ThievingDialogue]";

        /// <summary>
        ///     When true the service emits detailed debug logs that trace dialogue resolution.
        /// </summary>
        public static bool EnableDebugLogging { get; set; }

        /// <summary>
        ///     Attempts to emit a pickpocket dialogue line for the supplied definition.
        /// </summary>
        /// <param name="definition">Definition describing the NPC being pickpocketed.</param>
        /// <param name="dialogueAnchor">Transform used to position the floating dialogue.</param>
        /// <param name="success">True when the pickpocket resolved successfully.</param>
        public static void TryPublishDialogue(ThievingNpcDefinition definition, Transform dialogueAnchor, bool success)
        {
            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"{LogPrefix} TryPublishDialogue invoked. DefinitionId='{definition?.Id ?? "null"}', DisplayName='{definition?.DisplayName ?? "null"}', Anchor={DescribeAnchor(dialogueAnchor)}, Success={success}.");
            }

            if (definition == null)
            {
                if (EnableDebugLogging)
                    Debug.Log($"{LogPrefix} Aborted because definition was null.");
                return;
            }

            if (dialogueAnchor == null)
            {
                if (EnableDebugLogging)
                    Debug.Log($"{LogPrefix} Aborted because dialogue anchor was null.");
                return;
            }

            if (!NpcPickpocketDialogueSet.TryGet(definition.Id, out var set))
            {
                if (EnableDebugLogging)
                {
                    Debug.Log(
                        $"{LogPrefix} No dialogue set registered for NPC id '{definition.Id}'. Available sets: {NpcPickpocketDialogueSet.RegisteredSetCount}.");
                }
                return;
            }

            if (success)
            {
                if (!ShouldEmitSuccessLine())
                {
                    if (EnableDebugLogging)
                        Debug.Log($"{LogPrefix} Success roll failed. Dialogue suppressed for '{definition.DisplayName}'.");
                    return;

                if (!set.TryGetRandomSuccessLine(out string line))
                {
                    if (EnableDebugLogging)
                        Debug.Log($"{LogPrefix} Dialogue set '{set.GetType().Name}' did not provide a success line.");
                    return;
                }

                Publish(definition.DisplayName, dialogueAnchor, line);
                return;
            }

            if (!set.TryGetRandomFailureLine(out string failureLine))
            {
                if (EnableDebugLogging)
                    Debug.Log($"{LogPrefix} Dialogue set '{set.GetType().Name}' did not provide a failure line.");
                return;
            }

            Publish(definition.DisplayName, dialogueAnchor, failureLine);
        }

        /// <summary>
        ///     Performs the 1-in-20 roll specified for success dialogue.
        /// </summary>
        private static bool ShouldEmitSuccessLine()
        {
            int roll = Random.Range(0, SuccessDialogueDenominator);
            bool emit = roll == 0;
            if (EnableDebugLogging)
            {
                Debug.Log($"{LogPrefix} Success dialogue roll -> value={roll} (emit={emit}).");
            }

            return emit;
        }

        /// <summary>
        ///     Formats and displays the resolved dialogue line above the NPC.
        /// </summary>
        private static void Publish(string speaker, Transform anchor, string line)
        {
            if (anchor == null)
            {
                if (EnableDebugLogging)
                    Debug.Log($"{LogPrefix} Publish aborted because anchor was null. Speaker='{speaker}', Line='{line}'.");
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (EnableDebugLogging)
                    Debug.Log($"{LogPrefix} Publish aborted because line was empty. Speaker='{speaker}'.");
                return;
            }

            string resolvedSpeaker = string.IsNullOrWhiteSpace(speaker) ? "NPC" : speaker.Trim();
            if (EnableDebugLogging)
            {
                Debug.Log(
                    $"{LogPrefix} Publishing dialogue. Speaker='{resolvedSpeaker}', Line='{line}', Anchor={DescribeAnchor(anchor)}.");
            }
            PopupText.Show($"{resolvedSpeaker}: {line}", anchor);
        }

        private static string DescribeAnchor(Transform anchor)
        {
            if (anchor == null)
                return "null";

            Vector3 position = anchor.position;
            return $"{anchor.name} (InstanceID {anchor.GetInstanceID()}, position {position})";
        }
    }
}
