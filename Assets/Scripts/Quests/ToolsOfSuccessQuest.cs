using System.Collections.Generic;
using UnityEngine;

namespace Quests
{
    /// <summary>
    /// Follow up quest demonstrating additional registration.
    /// </summary>
    [CreateAssetMenu(menuName = "Quests/Tools Of Success")]
    public class ToolsOfSuccessQuest : QuestDefinition
    {
        private void OnEnable()
        {
            QuestID = "ToolsOfSuccess";
            Title = "Tools of Success";
            Description = "Prove your skill by crafting and using improved tools.";
            Rewards = "Small Woodcutting XP\nSmall Fishing XP\n100 Coins";

            if (Steps == null || Steps.Count == 0)
            {
                Steps = new List<QuestStep>
                {
                    new QuestStep { StepID = "CraftTool", StepDescription = "Craft a tool" },
                    new QuestStep { StepID = "UseTool", StepDescription = "Use the tool" },
                    new QuestStep { StepID = "ReturnToRowan", StepDescription = "Return to Elder Rowan" }
                };
            }
        }
    }
}
