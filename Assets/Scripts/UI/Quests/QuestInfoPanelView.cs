using System;
using System.Text;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Quests
{
    /// <summary>
    /// Handles rendering of the QuestInfo panel, including description, objectives, and rewards placeholders.
    /// </summary>
    public sealed class QuestInfoPanelView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Header text displaying the quest title.")]
        private Text titleLabel;

        [SerializeField]
        [Tooltip("Paragraph describing the quest lore.")]
        private Text descriptionLabel;

        [SerializeField]
        [Tooltip("Text element used to render the objectives list.")]
        private Text objectivesLabel;

        [SerializeField]
        [Tooltip("Text element used to render the rewards list.")]
        private Text rewardsLabel;

        [SerializeField]
        [Tooltip("Button returning the user to the quest list panel.")]
        private Button backButton;

        /// <summary>
        /// Raised when the Back button is pressed so the parent window can swap to the list panel.
        /// </summary>
        public event Action BackRequested;

        private void Awake()
        {
            ApplyFonts();
            if (backButton != null)
                backButton.onClick.AddListener(HandleBackButtonClicked);
        }

        private void OnDestroy()
        {
            if (backButton != null)
                backButton.onClick.RemoveListener(HandleBackButtonClicked);
        }

        /// <summary>
        /// Assigns runtime generated UI elements to the panel and reapplies styling.
        /// </summary>
        /// <param name="title">Label used for the quest title.</param>
        /// <param name="description">Label used for the quest description paragraph.</param>
        /// <param name="objectives">Label used for the objectives list.</param>
        /// <param name="rewards">Label used for the rewards list.</param>
        /// <param name="back">Button that closes the panel and returns to the quest list.</param>
        public void Configure(Text title, Text description, Text objectives, Text rewards, Button back)
        {
            if (backButton != null)
                backButton.onClick.RemoveListener(HandleBackButtonClicked);

            titleLabel = title;
            descriptionLabel = description;
            objectivesLabel = objectives;
            rewardsLabel = rewards;
            backButton = back;

            ApplyFonts();

            if (backButton != null)
                backButton.onClick.AddListener(HandleBackButtonClicked);
        }

        /// <summary>
        /// Populates the panel with the supplied quest data.
        /// </summary>
        public void ShowQuest(QuestListEntryData quest)
        {
            if (quest == null)
            {
                ShowPlaceholder();
                return;
            }

            if (titleLabel != null)
                titleLabel.text = quest.Title;

            if (descriptionLabel != null)
                descriptionLabel.text = string.IsNullOrWhiteSpace(quest.Description)
                    ? "No quest description has been provided yet."
                    : quest.Description;

            if (objectivesLabel != null)
                objectivesLabel.text = BuildObjectivesBlock(quest);

            if (rewardsLabel != null)
                rewardsLabel.text = BuildRewardsBlock(quest);
        }

        /// <summary>
        /// Displays placeholder copy indicating that no quest has been selected.
        /// </summary>
        public void ShowPlaceholder()
        {
            if (titleLabel != null)
                titleLabel.text = "Quest Details";
            if (descriptionLabel != null)
                descriptionLabel.text = "Select a quest from the list to view its lore, objectives, and rewards.";
            if (objectivesLabel != null)
                objectivesLabel.text = "Objectives will appear here once quests are authored.";
            if (rewardsLabel != null)
                rewardsLabel.text = "Rewards will appear here once quests are authored.";
        }

        /// <summary>
        /// Toggles the visibility of the panel.
        /// </summary>
        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        private void ApplyFonts()
        {
            ApplyFont(titleLabel, FontStyle.Bold);
            ApplyFont(descriptionLabel);
            ApplyFont(objectivesLabel);
            ApplyFont(rewardsLabel);
        }

        private static void ApplyFont(Text label, FontStyle fontStyle = FontStyle.Normal)
        {
            if (label == null)
                return;

            LegacyFontProvider.ApplyTo(label);
            label.fontStyle = fontStyle;
            label.color = Color.white;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static string BuildObjectivesBlock(QuestListEntryData quest)
        {
            if (quest.Objectives == null || quest.Objectives.Count == 0)
                return "No objectives recorded yet.";

            var builder = new StringBuilder();
            builder.AppendLine("Objectives:");
            foreach (var objective in quest.Objectives)
            {
                if (objective == null)
                    continue;
                var checkbox = objective.IsComplete ? "[\u2714]" : "[ ]";
                builder.AppendLine($"  {checkbox} {objective.Description}");
            }

            return builder.ToString();
        }

        private static string BuildRewardsBlock(QuestListEntryData quest)
        {
            if (quest.Rewards == null || quest.Rewards.Count == 0)
                return "Rewards have not been configured yet.";

            var builder = new StringBuilder();
            builder.AppendLine("Rewards:");
            foreach (var reward in quest.Rewards)
            {
                if (string.IsNullOrWhiteSpace(reward))
                    continue;
                builder.AppendLine($"  • {reward}");
            }

            return builder.ToString();
        }

        private void HandleBackButtonClicked()
        {
            BackRequested?.Invoke();
        }
    }
}
