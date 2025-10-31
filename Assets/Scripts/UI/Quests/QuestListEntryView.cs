using System;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Quests
{
    /// <summary>
    /// Controls a quest entry button inside the QuestList panel.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class QuestListEntryView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Label used to display the quest title.")]
        private Text titleLabel;

        private Button cachedButton;
        private QuestListEntryData cachedData;
        private Action<QuestListEntryData> clickCallback;

        private void Awake()
        {
            cachedButton = GetComponent<Button>();
            if (titleLabel == null)
                titleLabel = GetComponentInChildren<Text>();

            ApplyLabelStyling();
        }

        /// <summary>
        /// Assigns the label generated at runtime so the entry can apply styling and refresh text.
        /// </summary>
        public void AssignTitleLabel(Text label)
        {
            titleLabel = label;
            ApplyLabelStyling();
            ApplyData();
        }

        /// <summary>
        /// Configures the entry with quest data and click behaviour.
        /// </summary>
        public void Initialise(QuestListEntryData data, Action<QuestListEntryData> onClicked)
        {
            cachedData = data;
            clickCallback = onClicked;
            ApplyData();

            if (cachedButton != null)
            {
                cachedButton.onClick.RemoveListener(HandleButtonClicked);
                cachedButton.onClick.AddListener(HandleButtonClicked);
            }
        }

        /// <summary>
        /// Updates the entry visuals when the quest state changes.
        /// </summary>
        public void Refresh(QuestListEntryData data)
        {
            cachedData = data;
            ApplyData();
        }

        /// <summary>
        /// Clears callbacks when the entry is destroyed to avoid leaking listeners.
        /// </summary>
        private void OnDestroy()
        {
            if (cachedButton != null)
                cachedButton.onClick.RemoveListener(HandleButtonClicked);
        }

        private void ApplyLabelStyling()
        {
            if (titleLabel == null)
                return;

            LegacyFontProvider.ApplyTo(titleLabel);
            titleLabel.alignment = TextAnchor.MiddleLeft;
            titleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleLabel.verticalOverflow = VerticalWrapMode.Truncate;
            titleLabel.raycastTarget = false;
        }

        private void ApplyData()
        {
            if (titleLabel == null || cachedData == null)
                return;

            titleLabel.text = cachedData.Title;
            titleLabel.color = QuestStatusColorUtility.ResolveTitleColor(cachedData.Status);
        }

        private void HandleButtonClicked()
        {
            clickCallback?.Invoke(cachedData);
        }
    }
}
