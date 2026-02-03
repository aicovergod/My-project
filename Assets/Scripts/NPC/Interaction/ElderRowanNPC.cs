using Dialogue;

namespace NPC
{
    /// <summary>
    /// Elder Rowan specific behaviour integrating quests and dialogue.
    /// </summary>
    public class ElderRowanNPC : NpcInteractable
    {
        public ElderRowanDialogueData dialogueData;

        public override void Talk()
        {
            // With the quest retired Elder Rowan always opens with the primary greeting node.
            DialogueManager.Instance.StartDialogue(dialogueData, 0);
        }
    }
}
