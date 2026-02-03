using System;
using System.Collections.Generic;
using System.Linq;

namespace UI.Quests
{
    /// <summary>
    /// Coordinates quest data providers and raises UI-friendly events when the quest list or selected quest changes.
    /// </summary>
    public sealed class QuestJournalPresenter : IDisposable
    {
        private readonly List<QuestListEntryData> cachedEntries = new List<QuestListEntryData>();
        private IQuestJournalDataProvider dataProvider;
        private QuestListEntryData selectedQuest;

        /// <summary>
        /// Raised whenever the quest list has been refreshed. Subscribers receive a read-only view of the cached entries.
        /// </summary>
        public event Action<IReadOnlyList<QuestListEntryData>> QuestListChanged;

        /// <summary>
        /// Raised whenever the currently selected quest changes.
        /// </summary>
        public event Action<QuestListEntryData> SelectedQuestChanged;

        /// <summary>
        /// Returns a read-only snapshot of the cached quest entries.
        /// </summary>
        public IReadOnlyList<QuestListEntryData> CachedEntries => cachedEntries;

        /// <summary>
        /// Switches to the provided data source and refreshes the quest list immediately.
        /// </summary>
        public void SetDataProvider(IQuestJournalDataProvider provider)
        {
            if (ReferenceEquals(dataProvider, provider))
                return;

            if (dataProvider != null)
            {
                dataProvider.DataChanged -= HandleProviderDataChanged;
                dataProvider.Dispose();
            }

            dataProvider = provider;

            if (dataProvider != null)
                dataProvider.DataChanged += HandleProviderDataChanged;

            Refresh();
        }

        /// <summary>
        /// Forces a refresh from the active provider.
        /// </summary>
        public void Refresh()
        {
            cachedEntries.Clear();

            if (dataProvider != null)
            {
                var latestEntries = dataProvider.GetQuests();
                if (latestEntries != null)
                    cachedEntries.AddRange(latestEntries);
            }

            QuestListChanged?.Invoke(cachedEntries);
            if (selectedQuest != null)
                ReselectCurrentQuest();
        }

        /// <summary>
        /// Selects the quest matching the supplied identifier.
        /// </summary>
        public void SelectQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                selectedQuest = null;
                SelectedQuestChanged?.Invoke(null);
                return;
            }

            var quest = cachedEntries.FirstOrDefault(q => string.Equals(q.QuestId, questId, StringComparison.Ordinal));
            if (quest == null)
                return;

            selectedQuest = quest;
            SelectedQuestChanged?.Invoke(selectedQuest);
        }

        /// <summary>
        /// Selects the provided quest and notifies listeners.
        /// </summary>
        public void SelectQuest(QuestListEntryData quest)
        {
            selectedQuest = quest;
            SelectedQuestChanged?.Invoke(selectedQuest);
        }

        /// <summary>
        /// Clears the current selection and notifies listeners.
        /// </summary>
        public void ClearSelection()
        {
            selectedQuest = null;
            SelectedQuestChanged?.Invoke(null);
        }

        /// <summary>
        /// Releases the active provider when the presenter is disposed.
        /// </summary>
        public void Dispose()
        {
            if (dataProvider != null)
            {
                dataProvider.DataChanged -= HandleProviderDataChanged;
                dataProvider.Dispose();
                dataProvider = null;
            }
        }

        private void HandleProviderDataChanged()
        {
            Refresh();
        }

        private void ReselectCurrentQuest()
        {
            if (selectedQuest == null)
            {
                SelectedQuestChanged?.Invoke(null);
                return;
            }

            var quest = cachedEntries.FirstOrDefault(q => string.Equals(q.QuestId, selectedQuest.QuestId, StringComparison.Ordinal));
            if (quest == null)
            {
                selectedQuest = null;
                SelectedQuestChanged?.Invoke(null);
            }
            else
            {
                selectedQuest = quest;
                SelectedQuestChanged?.Invoke(selectedQuest);
            }
        }
    }
}
