using Dialogue;
using Quests;

namespace NPC
{
    /// <summary>
    /// Elder Rowan specific behaviour integrating quests and dialogue.
    /// </summary>
    public class ElderRowanNPC : NpcInteractable
    {
        public ElderRowanDialogueData dialogueData;

        public new void Talk()
        {
            var qm = QuestManager.Instance;
            int node = 0;
            if (qm != null)
            {
                if (qm.IsQuestActive("ToolsOfSurvival"))
                {
                    var quest = qm.GetQuest("ToolsOfSurvival");
                    bool logsDone = quest?.Steps.Find(s => s.StepID == "ChopLogs")?.IsComplete == true;
                    bool oresDone = quest?.Steps.Find(s => s.StepID == "MineOres")?.IsComplete == true;
                    bool returnDone = quest?.Steps.Find(s => s.StepID == "ReturnToRowan")?.IsComplete == true;
                    if (logsDone && oresDone && !returnDone)
                        qm.UpdateStep("ToolsOfSurvival", "ReturnToRowan");
                }

                if (qm.IsQuestCompleted("ToolsOfSurvival"))
                    node = 3;
                else if (qm.IsQuestActive("ToolsOfSurvival"))
                    node = qm.IsQuestReadyToTurnIn("ToolsOfSurvival") ? 2 : 1;
            }
            DialogueManager.Instance.StartDialogue(dialogueData, node);
        }
    }
}
