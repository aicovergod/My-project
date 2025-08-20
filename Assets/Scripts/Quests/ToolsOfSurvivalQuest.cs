using System.Collections.Generic;
using UnityEngine;

namespace Quests
{
    /// <summary>
    /// Preconfigured quest used as a starting example.
    /// </summary>
    [CreateAssetMenu(menuName = "Quests/Tools Of Survival")]
    public class ToolsOfSurvivalQuest : QuestDefinition
    {
        private void OnEnable()
        {
            QuestID = "ToolsOfSurvival";
            Title = "Tools of Survival";
            Description = "Elder Rowan has tasked you with gathering basic resources.";
            Rewards = "Small Woodcutting XP\nSmall Mining XP\n100 Coins";

            if (Steps == null || Steps.Count == 0)
            {
                Steps = new List<QuestStep>
                {
                    new QuestStep { StepID = "ChopLogs", StepDescription = "Chop 3 logs" },
                    new QuestStep { StepID = "MineOres", StepDescription = "Mine 3 ores" },
                    new QuestStep { StepID = "ReturnToRowan", StepDescription = "Return to Elder Rowan" }
                };
            }
        }
    }
}
