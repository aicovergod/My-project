using System;
using System.Collections.Generic;
using Core.Save;
using UI.Chat;
using UnityEngine;
using World;

namespace Player.Ranks
{
    /// <summary>
    /// Scene-persistent service that resolves player account ranks from a designer-maintained database
    /// and exposes helpers for permission checks across gameplay systems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRankService : SceneGatedSingletonBehaviour<PlayerRankService>
    {
        private const string DatabaseResourcePath = "PlayerRanks/DefaultPlayerRankDatabase";

        [SerializeField, Tooltip("Optional override database. When null the service loads the asset from Resources/PlayerRanks.")]
        private PlayerRankDatabase database;

        private readonly Dictionary<string, PlayerRank> usernameLookup = new Dictionary<string, PlayerRank>(StringComparer.Ordinal);

        /// <summary>
        /// Raised whenever the active player's resolved rank changes.
        /// </summary>
        public event Action<PlayerRank> ActivePlayerRankChanged;

        /// <summary>
        /// Cached rank for the currently authenticated account. Defaults to <see cref="PlayerRank.Player"/>.
        /// </summary>
        public PlayerRank ActivePlayerRank { get; private set; } = PlayerRank.Player;

        /// <summary>
        /// Runtime accessor for the loaded rank database.
        /// </summary>
        public PlayerRankDatabase Database => database;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateSingleton);
        }

        private static PlayerRankService CreateSingleton()
        {
            var go = new GameObject(nameof(PlayerRankService));
            return go.AddComponent<PlayerRankService>();
        }

        /// <inheritdoc />
        protected override void OnSingletonAwake()
        {
            base.OnSingletonAwake();

            LoadDatabase();
            RebuildLookup();

            SaveManager.ActiveAccountUsernameChanged += HandleActiveAccountUsernameChanged;
            RefreshActivePlayerRank(true);
        }

        /// <inheritdoc />
        protected override void OnSingletonDestroyed()
        {
            SaveManager.ActiveAccountUsernameChanged -= HandleActiveAccountUsernameChanged;
            base.OnSingletonDestroyed();
        }

        /// <summary>
        /// Resolves the configured rank for the supplied username.
        /// </summary>
        public PlayerRank GetRankForUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return PlayerRank.Player;

            string slug = AccountManager.SanitizeUsername(username);
            if (string.IsNullOrEmpty(slug))
                return PlayerRank.Player;

            return usernameLookup.TryGetValue(slug, out var rank) ? rank : PlayerRank.Player;
        }

        /// <summary>
        /// Checks whether the supplied username satisfies the requested rank requirement.
        /// </summary>
        public bool HasPermission(string username, PlayerRank requiredRank)
        {
            return HasPermission(GetRankForUsername(username), requiredRank);
        }

        /// <summary>
        /// Checks whether an already resolved rank satisfies the requested requirement.
        /// </summary>
        public bool HasPermission(PlayerRank actualRank, PlayerRank requiredRank)
        {
            return actualRank >= requiredRank;
        }

        /// <summary>
        /// Forces the service to reload the lookup tables from the current database reference.
        /// </summary>
        public void RebuildLookup()
        {
            usernameLookup.Clear();

            if (database == null)
                return;

            AppendUsernames(database.SupportUsernames, PlayerRank.Support);
            AppendUsernames(database.ModeratorUsernames, PlayerRank.Moderator);
            AppendUsernames(database.AdminUsernames, PlayerRank.Admin);
            AppendUsernames(database.DeveloperUsernames, PlayerRank.Developer);
        }

        /// <summary>
        /// Allows runtime systems or editor tooling to inject a new database instance.
        /// </summary>
        public void SetDatabase(PlayerRankDatabase newDatabase)
        {
            database = newDatabase;
            if (database == null)
                LoadDatabase();

            RebuildLookup();
            RefreshActivePlayerRank(true);
        }

        private void LoadDatabase()
        {
            if (database != null)
                return;

            database = Resources.Load<PlayerRankDatabase>(DatabaseResourcePath);
            if (database == null)
            {
                Debug.LogWarning($"PlayerRankService: No PlayerRankDatabase found at Resources/{DatabaseResourcePath}. All players will default to the Player rank.");
                database = ScriptableObject.CreateInstance<PlayerRankDatabase>();
            }
        }

        private void AppendUsernames(IReadOnlyList<string> usernames, PlayerRank rank)
        {
            if (usernames == null)
                return;

            for (int i = 0; i < usernames.Count; i++)
            {
                string slug = AccountManager.SanitizeUsername(usernames[i]);
                if (string.IsNullOrEmpty(slug))
                    continue;

                if (usernameLookup.TryGetValue(slug, out var existingRank))
                {
                    if (rank > existingRank)
                        usernameLookup[slug] = rank;
                }
                else
                {
                    usernameLookup.Add(slug, rank);
                }
            }
        }

        private void HandleActiveAccountUsernameChanged(string _)
        {
            RefreshActivePlayerRank(false);
        }

        private void RefreshActivePlayerRank(bool forceNotify)
        {
            string username = SaveManager.ActiveAccountUsername;
            PlayerRank resolvedRank = GetRankForUsername(username);

            bool rankChanged = resolvedRank != ActivePlayerRank;
            ActivePlayerRank = resolvedRank;

            if (rankChanged || forceNotify)
            {
                ActivePlayerRankChanged?.Invoke(resolvedRank);
                PublishRankMessage(username, resolvedRank, forceNotify, rankChanged);
            }
        }

        private void PublishRankMessage(string username, PlayerRank resolvedRank, bool includeWelcome, bool rankChanged)
        {
            var chatService = ChatService.Instance;
            if (chatService == null)
                return;

            if (string.IsNullOrEmpty(username))
            {
                if (includeWelcome)
                    chatService.PublishGameMessage("No account is currently active. Log in to unlock moderator tooling.");
                return;
            }

            string readableRank = resolvedRank.ToString();
            if (includeWelcome)
                chatService.PublishGameMessage($"Welcome back, {username}! Your account rank is {readableRank}.");
            else if (rankChanged)
                chatService.PublishGameMessage($"Account rank updated: {readableRank}.");
        }
    }
}
