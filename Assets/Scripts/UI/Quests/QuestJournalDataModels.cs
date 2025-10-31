using System.Collections.Generic;

namespace UI.Quests
{
    /// <summary>
    /// Represents the player-facing state of a quest for journal presentation.
    /// </summary>
    public enum QuestProgressState
    {
        /// <summary>
        /// Quest has not been started or accepted yet.
        /// </summary>
        NotStarted,

        /// <summary>
        /// Quest has been accepted and is currently in progress.
        /// </summary>
        InProgress,

        /// <summary>
        /// Quest has been completed and can no longer progress.
        /// </summary>
        Completed
    }

    /// <summary>
    /// Describes a single quest objective entry as displayed in the quest details panel.
    /// </summary>
    public sealed class QuestObjectiveData
    {
        /// <summary>
        /// Initialises a new objective descriptor with the provided presentation data.
        /// </summary>
        public QuestObjectiveData(string description, bool isComplete)
        {
            Description = description;
            IsComplete = isComplete;
        }

        /// <summary>
        /// Text that should be displayed for the objective.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// True when the objective has been completed by the player.
        /// </summary>
        public bool IsComplete { get; }
    }

    /// <summary>
    /// Holds the data required to present a quest inside the QuestList panel.
    /// </summary>
    public sealed class QuestListEntryData
    {
        /// <summary>
        /// Initialises a new quest list entry descriptor.
        /// </summary>
        public QuestListEntryData(
            string questId,
            string title,
            string description,
            QuestProgressState status,
            IReadOnlyList<QuestObjectiveData> objectives,
            IReadOnlyList<string> rewards)
        {
            QuestId = questId;
            Title = title;
            Description = description;
            Status = status;
            Objectives = objectives;
            Rewards = rewards;
        }

        /// <summary>
        /// Unique identifier for the quest. Mirrors QuestDefinition.QuestID when sourced from the manager.
        /// </summary>
        public string QuestId { get; }

        /// <summary>
        /// Quest title rendered in both the list entry and the quest info window.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Description block shown when inspecting the quest.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Current quest status controlling colour theming in the list.
        /// </summary>
        public QuestProgressState Status { get; }

        /// <summary>
        /// Ordered collection of quest objectives displayed in the details panel.
        /// </summary>
        public IReadOnlyList<QuestObjectiveData> Objectives { get; }

        /// <summary>
        /// Ordered collection of reward strings to display beneath the objectives block.
        /// </summary>
        public IReadOnlyList<string> Rewards { get; }
    }
}
