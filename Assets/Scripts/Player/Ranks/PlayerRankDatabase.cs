using System.Collections.Generic;
using UnityEngine;

namespace Player.Ranks
{
    /// <summary>
    /// ScriptableObject container that maps raw usernames to their configured moderation ranks.
    /// Designers can edit the asset inside <c>Resources/PlayerRanks</c> to grant or revoke
    /// elevated permissions without pushing a code update.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerRankDatabase", menuName = "Player/Ranks/Player Rank Database")]
    public sealed class PlayerRankDatabase : ScriptableObject
    {
        [SerializeField, Tooltip("Usernames granted the Support moderation tier.")]
        private List<string> supportUsernames = new List<string>();

        [SerializeField, Tooltip("Usernames granted the Moderator tier.")]
        private List<string> moderatorUsernames = new List<string>();

        [SerializeField, Tooltip("Usernames granted the Admin tier.")]
        private List<string> adminUsernames = new List<string>();

        [SerializeField, Tooltip("Usernames granted the Developer tier.")]
        private List<string> developerUsernames = new List<string>();

        /// <summary>
        /// Usernames configured for the Support tier.
        /// </summary>
        public IReadOnlyList<string> SupportUsernames => supportUsernames;

        /// <summary>
        /// Usernames configured for the Moderator tier.
        /// </summary>
        public IReadOnlyList<string> ModeratorUsernames => moderatorUsernames;

        /// <summary>
        /// Usernames configured for the Admin tier.
        /// </summary>
        public IReadOnlyList<string> AdminUsernames => adminUsernames;

        /// <summary>
        /// Usernames configured for the Developer tier.
        /// </summary>
        public IReadOnlyList<string> DeveloperUsernames => developerUsernames;

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only helper that ensures the internal lists are instantiated when the asset is created.
        /// </summary>
        private void OnValidate()
        {
            supportUsernames ??= new List<string>();
            moderatorUsernames ??= new List<string>();
            adminUsernames ??= new List<string>();
            developerUsernames ??= new List<string>();
        }
#endif
    }
}
