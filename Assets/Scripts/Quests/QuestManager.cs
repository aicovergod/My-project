using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Quests
{
    /// <summary>
    /// Manages the player's quests and notifies listeners when they change.
    /// </summary>
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager Instance { get; private set; }

        [Tooltip("Invoked whenever the quest list or progress changes.")]
        public UnityEvent QuestsUpdated;

        private readonly Dictionary<string, QuestDefinition> available = new Dictionary<string, QuestDefinition>();
        private readonly Dictionary<string, QuestDefinition> active = new Dictionary<string, QuestDefinition>();
        private readonly Dictionary<string, QuestDefinition> completed = new Dictionary<string, QuestDefinition>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (QuestsUpdated == null)
                QuestsUpdated = new UnityEvent();

            // Load any quest definitions placed under Resources/Quests so they
            // appear in the log before being accepted.
            var initialQuests = Resources.LoadAll<QuestDefinition>("Quests");
            foreach (var quest in initialQuests)
                RegisterQuest(quest);
        }

        /// <summary>
        /// Registers a quest as available to the player.
        /// </summary>
        public void RegisterQuest(QuestDefinition quest)
        {
            if (quest == null || available.ContainsKey(quest.QuestID) || active.ContainsKey(quest.QuestID) || completed.ContainsKey(quest.QuestID))
                return;
            available[quest.QuestID] = quest;
            QuestsUpdated.Invoke();
        }

        /// <summary>
        /// Moves an available quest to the active list.
        /// </summary>
        public void AddQuest(string questID)
        {
            if (!available.TryGetValue(questID, out var def))
                return;
            var instance = Instantiate(def);
            // Ensure step states reset
            foreach (var step in instance.Steps)
                step.IsComplete = false;
            active[questID] = instance;
            available.Remove(questID);
            QuestsUpdated.Invoke();
        }

        /// <summary>
        /// Marks a quest step complete.
        /// </summary>
        public void UpdateStep(string questID, string stepID)
        {
            if (!active.TryGetValue(questID, out var quest))
                return;
            var step = quest.Steps.Find(s => s.StepID == stepID);
            if (step == null || step.IsComplete)
                return;
            step.IsComplete = true;
            QuestsUpdated.Invoke();
        }

        /// <summary>
        /// Finalizes a quest and moves it to the completed list.
        /// </summary>
        public void CompleteQuest(string questID)
        {
            if (!active.TryGetValue(questID, out var quest))
                return;
            active.Remove(questID);
            completed[questID] = quest;
            QuestsUpdated.Invoke();
        }

        public IEnumerable<QuestDefinition> GetActiveQuests() => active.Values;
        public IEnumerable<QuestDefinition> GetCompletedQuests() => completed.Values;
        public IEnumerable<QuestDefinition> GetAvailableQuests() => available.Values;

        public bool IsQuestActive(string questID) => active.ContainsKey(questID);
        public bool IsQuestCompleted(string questID) => completed.ContainsKey(questID);

        /// <summary>
        /// Returns true if all steps except possibly a return step are completed.
        /// </summary>
        public bool IsQuestReadyToTurnIn(string questID)
        {
            if (!active.TryGetValue(questID, out var quest))
                return false;
            foreach (var step in quest.Steps)
            {
                if (!step.IsComplete)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Retrieves a quest definition from the active list.
        /// </summary>
        public QuestDefinition GetQuest(string questID)
        {
            active.TryGetValue(questID, out var quest);
            return quest;
        }
    }
}
