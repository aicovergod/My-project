using System.Collections.Generic;
using UnityEngine;
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

            // Node 0: Greeting and branching topics for world flavour.
            Nodes.Add(new DialogueNode
            {
                Text = "Greetings, traveler. Violetstown has endured many storms, yet we stand resilient.",
                Options = new List<DialogueOption>
                {
                    new DialogueOption
                    {
                        Text = "Tell me about Violetstown.",
                        NextNode = 1
                    },
                    new DialogueOption
                    {
                        Text = "Any advice for survival?",
                        NextNode = 2
                    },
                    new DialogueOption
                    {
                        Text = "Farewell.",
                        NextNode = -1
                    }
                }
            });

            // Node 1: Town lore explanation.
            Nodes.Add(new DialogueNode
            {
                Text = "Our town thrives because every villager shoulders a craft. Woodcutters keep the hearths warm and miners unearth the ores that forge our blades.",
                Options = new List<DialogueOption>
                {
                    new DialogueOption
                    {
                        Text = "Thanks for the history.",
                        NextNode = -1
                    }
                }
            });

            // Node 2: General survival guidance.
            Nodes.Add(new DialogueNode
            {
                Text = "Keep your tools sharp, your rations stocked, and your allies close. Preparation is the true secret to surviving the wilds beyond our walls.",
                Options = new List<DialogueOption>
                {
                    new DialogueOption
                    {
                        Text = "I'll remember that.",
                        NextNode = -1
                    }
                }
            });
        }
    }
}
