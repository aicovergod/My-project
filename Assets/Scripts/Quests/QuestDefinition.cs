using System.Collections.Generic;
using UnityEngine;

namespace Quests
{
    /// <summary>
    /// Data asset describing a quest and its steps.
    /// </summary>
    [CreateAssetMenu(menuName = "Quests/Quest Definition")]
    public class QuestDefinition : ScriptableObject
    {
        [Tooltip("Unique identifier for this quest.")]
        public string QuestID;

        [Tooltip("Displayed title of the quest.")]
        public string Title;

        [Tooltip("Description shown in the quest log.")]
        [TextArea]
        public string Description;

        [Tooltip("Ordered steps that comprise the quest.")]
        public List<QuestStep> Steps = new List<QuestStep>();

        [Tooltip("Text describing rewards granted on completion.")]
        [TextArea]
        public string Rewards;
    }
}
