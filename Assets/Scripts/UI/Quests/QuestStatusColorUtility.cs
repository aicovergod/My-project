using UnityEngine;

namespace UI.Quests
{
    /// <summary>
    /// Centralises colour mappings for quest progress states so list entries and headers remain consistent.
    /// </summary>
    public static class QuestStatusColorUtility
    {
        private static readonly Color NotStartedColor = new Color32(0xB4, 0x30, 0x30, 0xFF);
        private static readonly Color InProgressColor = new Color32(0xF2, 0xDB, 0x6E, 0xFF);
        private static readonly Color CompletedColor = new Color32(0x4B, 0xB0, 0x5F, 0xFF);

        /// <summary>
        /// Resolves the colour that should be applied to quest list titles for the supplied status.
        /// </summary>
        public static Color ResolveTitleColor(QuestProgressState status)
        {
            switch (status)
            {
                case QuestProgressState.Completed:
                    return CompletedColor;
                case QuestProgressState.InProgress:
                    return InProgressColor;
                default:
                    return NotStartedColor;
            }
        }
    }
}
