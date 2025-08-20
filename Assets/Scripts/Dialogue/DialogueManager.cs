using UnityEngine;
using Quests;

namespace Dialogue
{
    /// <summary>
    /// Controls dialogue flow and applies option actions.
    /// </summary>
    public class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }

        private DialogueData currentData;
        private int currentIndex;
        private DialogueUI ui;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            ui = gameObject.AddComponent<DialogueUI>();
        }

        /// <summary>
        /// Starts a dialogue sequence.
        /// </summary>
        public void StartDialogue(DialogueData data, int startNode = 0)
        {
            if (data == null || data.Nodes.Count == 0)
                return;
            currentData = data;
            currentIndex = Mathf.Clamp(startNode, 0, data.Nodes.Count - 1);
            ShowNode();
        }

        private void ShowNode()
        {
            var node = currentData.Nodes[currentIndex];
            ui.Show(currentData.NpcName, node, OnOption);
        }

        private void OnOption(int optionIndex)
        {
            var node = currentData.Nodes[currentIndex];
            if (optionIndex < 0 || optionIndex >= node.Options.Count)
                return;
            var opt = node.Options[optionIndex];
            if (!CheckCondition(opt)) return;

            ApplyAction(opt);

            if (opt.NextNode < 0 || opt.NextNode >= currentData.Nodes.Count)
            {
                ui.Hide();
                currentData = null;
            }
            else
            {
                currentIndex = opt.NextNode;
                ShowNode();
            }
        }

        private bool CheckCondition(DialogueOption opt)
        {
            if (string.IsNullOrEmpty(opt.RequiredQuestID) || opt.RequiredState == QuestState.None)
                return true;
            var qm = QuestManager.Instance;
            return opt.RequiredState switch
            {
                QuestState.NotStarted => !qm.IsQuestActive(opt.RequiredQuestID) && !qm.IsQuestCompleted(opt.RequiredQuestID),
                QuestState.Active => qm.IsQuestActive(opt.RequiredQuestID),
                QuestState.Completed => qm.IsQuestCompleted(opt.RequiredQuestID),
                _ => true
            };
        }

        private void ApplyAction(DialogueOption opt)
        {
            var qm = QuestManager.Instance;
            switch (opt.Action)
            {
                case DialogueAction.StartQuest:
                    qm.AddQuest(opt.QuestID);
                    break;
                case DialogueAction.CompleteQuest:
                    qm.CompleteQuest(opt.QuestID);
                    break;
            }
        }
    }
}
