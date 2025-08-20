using System.Collections.Generic;
using UnityEngine;
using Quests;

namespace Dialogue
{
    /// <summary>
    /// Scriptable object holding dialogue nodes for an NPC.
    /// </summary>
    [CreateAssetMenu(menuName = "Dialogue/Dialogue Data")]
    public class DialogueData : ScriptableObject
    {
        public string NpcName;
        public List<DialogueNode> Nodes = new List<DialogueNode>();
    }

    [System.Serializable]
    public class DialogueNode
    {
        [TextArea]
        public string Text;
        public List<DialogueOption> Options = new List<DialogueOption>();
    }

    [System.Serializable]
    public class DialogueOption
    {
        public string Text;
        public int NextNode = -1;
        public DialogueAction Action;
        public string QuestID;
        public string RequiredQuestID;
        public QuestState RequiredState = QuestState.None;
    }

    public enum DialogueAction
    {
        None,
        StartQuest,
        CompleteQuest
    }

    public enum QuestState
    {
        None,
        NotStarted,
        Active,
        Completed
    }
}
