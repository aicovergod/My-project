using System.Collections.Generic;
using UnityEngine;
using Quests;

namespace Dialogue
{
    /// <summary>
    /// Dialogue tree for Elder Rowan.
    /// </summary>
    [CreateAssetMenu(menuName = "Dialogue/Elder Rowan")]
    public class ElderRowanDialogueData : DialogueData
    {
        private void OnEnable()
        {
            NpcName = "Elder Rowan";
            if (Nodes != null && Nodes.Count > 0) return;

            Nodes = new List<DialogueNode>();

            // 0: initial offer
            Nodes.Add(new DialogueNode
            {
                Text = "Greetings, traveler. If you want to survive here, you must learn the basics. Chop some logs and mine some ore. Do you accept?",
                Options = new List<DialogueOption>
                {
                    new DialogueOption
                    {
                        Text = "Accept",
                        Action = DialogueAction.StartQuest,
                        QuestID = "ToolsOfSurvival",
                        NextNode = -1,
                        RequiredQuestID = "ToolsOfSurvival",
                        RequiredState = QuestState.NotStarted
                    },
                    new DialogueOption
                    {
                        Text = "Decline",
                        NextNode = -1,
                        RequiredQuestID = "ToolsOfSurvival",
                        RequiredState = QuestState.NotStarted
                    }
                }
            });

            // 1: quest active
            Nodes.Add(new DialogueNode
            {
                Text = "Keep at it, traveler. Chop 3 logs and mine 3 ores, then return to me.",
                Options = new List<DialogueOption>
                {
                    new DialogueOption { Text = "Continue", NextNode = -1, RequiredQuestID = "ToolsOfSurvival", RequiredState = QuestState.Active }
                }
            });

            // 2: quest ready to turn in
            Nodes.Add(new DialogueNode
            {
                Text = "Excellent work! You’ve proven yourself capable.",
                Options = new List<DialogueOption>
                {
                    new DialogueOption
                    {
                        Text = "Thanks",
                        Action = DialogueAction.CompleteQuest,
                        QuestID = "ToolsOfSurvival",
                        NextNode = -1,
                        RequiredQuestID = "ToolsOfSurvival",
                        RequiredState = QuestState.Active
                    }
                }
            });

            // 3: quest completed
            Nodes.Add(new DialogueNode
            {
                Text = "You’ve mastered the basics. Violetstown is safer with you here.",
                Options = new List<DialogueOption>
                {
                    new DialogueOption { Text = "Farewell", NextNode = -1, RequiredQuestID = "ToolsOfSurvival", RequiredState = QuestState.Completed }
                }
            });
        }
    }
}
