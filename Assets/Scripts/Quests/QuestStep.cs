using System;
using UnityEngine;

namespace Quests
{
    /// <summary>
    /// Represents a single step within a quest.
    /// </summary>
    [Serializable]
    public class QuestStep
    {
        [Tooltip("Unique identifier for this step.")]
        public string StepID;

        [Tooltip("Description displayed to the player.")]
        [TextArea]
        public string StepDescription;

        [Tooltip("True when the step has been completed.")]
        public bool IsComplete;
    }
}
