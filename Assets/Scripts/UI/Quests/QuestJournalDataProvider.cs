using System;
using System.Collections.Generic;
using Quests;

namespace UI.Quests
{
    /// <summary>
    /// Provides quest list data to the UI. Concrete implementations can wrap the runtime quest manager
    /// or provide placeholder content for editor previews.
    /// </summary>
    public interface IQuestJournalDataProvider : IDisposable
    {
        /// <summary>
        /// Raised whenever the underlying quest data changes and the UI should refresh.
        /// </summary>
        event Action DataChanged;

        /// <summary>
        /// Retrieves the current quest list snapshot.
        /// </summary>
        IReadOnlyList<QuestListEntryData> GetQuests();
    }

    /// <summary>
    /// Bridges <see cref="QuestManager"/> data into the quest journal UI.
    /// </summary>
    public sealed class QuestManagerDataProvider : IQuestJournalDataProvider
    {
        private readonly QuestManager questManager;

        /// <inheritdoc />
        public event Action DataChanged;

        /// <summary>
        /// Creates a provider bound to the supplied quest manager instance.
        /// </summary>
        /// <param name="manager">Quest manager that publishes runtime quest updates.</param>
        public QuestManagerDataProvider(QuestManager manager)
        {
            questManager = manager;
            if (questManager != null)
                questManager.QuestsUpdated.AddListener(HandleQuestManagerUpdated);
        }

        /// <inheritdoc />
        public IReadOnlyList<QuestListEntryData> GetQuests()
        {
            var results = new List<QuestListEntryData>();
            if (questManager == null)
                return results;

            AppendQuests(results, questManager.GetActiveQuests(), QuestProgressState.InProgress);
            AppendQuests(results, questManager.GetAvailableQuests(), QuestProgressState.NotStarted);
            AppendQuests(results, questManager.GetCompletedQuests(), QuestProgressState.Completed);
            return results;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (questManager != null)
                questManager.QuestsUpdated.RemoveListener(HandleQuestManagerUpdated);
        }

        private void HandleQuestManagerUpdated()
        {
            DataChanged?.Invoke();
        }

        private static void AppendQuests(
            List<QuestListEntryData> buffer,
            IEnumerable<QuestDefinition> quests,
            QuestProgressState status)
        {
            if (quests == null)
                return;

            foreach (var quest in quests)
            {
                if (quest == null)
                    continue;

                var objectives = new List<QuestObjectiveData>();
                if (quest.Steps != null)
                {
                    foreach (var step in quest.Steps)
                    {
                        if (step == null)
                            continue;
                        objectives.Add(new QuestObjectiveData(step.StepDescription, step.IsComplete));
                    }
                }

                var rewards = new List<string>();
                if (!string.IsNullOrWhiteSpace(quest.Rewards))
                    rewards.Add(quest.Rewards);

                buffer.Add(new QuestListEntryData(
                    quest.QuestID,
                    quest.Title,
                    quest.Description,
                    status,
                    objectives,
                    rewards));
            }
        }
    }

    /// <summary>
    /// Supplies placeholder quest data so designers can validate the UI without live quest definitions.
    /// </summary>
    public sealed class PlaceholderQuestDataProvider : IQuestJournalDataProvider
    {
        private readonly List<QuestListEntryData> cachedEntries = new List<QuestListEntryData>();

        /// <summary>
        /// Creates the placeholder list immediately so repeated refreshes reuse the same descriptors.
        /// </summary>
        public PlaceholderQuestDataProvider()
        {
            GeneratePlaceholderData();
        }

        /// <inheritdoc />
        public event Action DataChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public IReadOnlyList<QuestListEntryData> GetQuests()
        {
            return cachedEntries;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            cachedEntries.Clear();
        }

        private void GeneratePlaceholderData()
        {
            cachedEntries.Clear();
            for (int i = 1; i <= 10; i++)
            {
                var status = QuestProgressState.NotStarted;
                if (i % 3 == 0)
                    status = QuestProgressState.Completed;
                else if (i % 2 == 0)
                    status = QuestProgressState.InProgress;

                var objectives = new List<QuestObjectiveData>
                {
                    new QuestObjectiveData($"Speak to the village elder about Quest {i}.", status != QuestProgressState.NotStarted),
                    new QuestObjectiveData($"Collect three relic shards for Quest {i}.", status == QuestProgressState.Completed)
                };

                var rewards = new List<string>
                {
                    "Placeholder reward drops",
                    "Future XP rewards"
                };

                cachedEntries.Add(new QuestListEntryData(
                    $"placeholder_{i}",
                    $"Quest {i}",
                    "A mysterious adventure awaits. Replace this description with quest lore when available.",
                    status,
                    objectives,
                    rewards));
            }
        }
    }
}
