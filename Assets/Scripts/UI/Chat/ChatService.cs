using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Core.Save;
using UnityEngine;
using World;

namespace UI.Chat
{
    /// <summary>
    /// Persistent chat backend responsible for storing history and broadcasting
    /// message events to runtime HUD controllers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChatService : SceneGatedSingletonBehaviour<ChatService>
    {
        private const int DefaultHistoryLimit = 200;
        private static readonly ChatChannel[] ChannelValues = ChatChannelUtility.GetOrderedChannels();
        private static readonly Regex WhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);

        [SerializeField, Tooltip("Maximum number of messages cached per channel."), Min(1)]
        private int historyLimit = DefaultHistoryLimit;

        private readonly Dictionary<ChatChannel, List<ChatMessage>> histories = new Dictionary<ChatChannel, List<ChatMessage>>();
        private readonly object syncRoot = new object();

        private string cachedActiveUsername = string.Empty;

        /// <summary>
        /// Raised when a new message is published to any channel.
        /// </summary>
        public event Action<ChatMessage> MessageReceived;

        /// <summary>
        /// Raised when listeners should rebuild visible history for a channel.
        /// </summary>
        public event Action<ChatChannel, IReadOnlyList<ChatMessage>> HistoryRefreshed;

        /// <summary>
        /// Raised whenever the active account username changes.
        /// </summary>
        public event Action<string> ActiveUsernameChanged;

        /// <summary>
        /// Current active account username cached from <see cref="SaveManager"/>.
        /// </summary>
        public string ActiveUsername
        {
            get
            {
                RefreshCachedUsername(false);
                return cachedActiveUsername;
            }
        }

        /// <summary>
        /// Maximum number of entries preserved per channel.
        /// </summary>
        public int HistoryLimit
        {
            get => historyLimit;
            set => historyLimit = Mathf.Max(1, value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateSingleton);
        }

        private static ChatService CreateSingleton()
        {
            var go = new GameObject(nameof(ChatService));
            return go.AddComponent<ChatService>();
        }

        /// <inheritdoc />
        protected override void OnSingletonAwake()
        {
            base.OnSingletonAwake();

            lock (syncRoot)
            {
                histories.Clear();
                for (int i = 0; i < ChannelValues.Length; i++)
                    histories[ChannelValues[i]] = new List<ChatMessage>(DefaultHistoryLimit);
            }

            SaveManager.ActiveAccountUsernameChanged += HandleActiveAccountUsernameChanged;
            RefreshCachedUsername(true);
        }

        /// <inheritdoc />
        protected override void OnSingletonDestroyed()
        {
            SaveManager.ActiveAccountUsernameChanged -= HandleActiveAccountUsernameChanged;
            base.OnSingletonDestroyed();

            lock (syncRoot)
            {
                histories.Clear();
            }
        }

        /// <summary>
        /// Publishes a player-authored chat message to the Public channel.
        /// </summary>
        public void PublishPublicMessage(string sender, string text)
        {
            string normalised = NormaliseMessage(text);
            if (string.IsNullOrEmpty(normalised))
                return;

            RefreshCachedUsername(false);

            string resolvedSender = !string.IsNullOrWhiteSpace(sender) ? sender.Trim() : ActiveUsername;
            bool isLocal = !string.IsNullOrEmpty(ActiveUsername) &&
                           string.Equals(resolvedSender, ActiveUsername, StringComparison.OrdinalIgnoreCase);

            var message = new ChatMessage(ChatChannel.Public, resolvedSender ?? string.Empty, normalised, DateTime.UtcNow, isLocal);
            EnqueueMessage(message);
        }

        /// <summary>
        /// Publishes a gameplay/system message to the Game channel.
        /// </summary>
        public void PublishGameMessage(string text)
        {
            string normalised = NormaliseMessage(text);
            if (string.IsNullOrEmpty(normalised))
                return;

            var message = new ChatMessage(ChatChannel.Game, "Game", normalised, DateTime.UtcNow, false);
            EnqueueMessage(message);
        }

        /// <summary>
        /// Publishes a companion dialogue line to the Companion channel.
        /// </summary>
        /// <param name="sender">Display name of the speaker. Falls back to "Companion" when empty.</param>
        /// <param name="text">Dialogue text that should be queued.</param>
        /// <param name="isLocalPlayerAuthor">Whether the local player authored the line (used for colour selection).</param>
        public void PublishCompanionMessage(string sender, string text, bool isLocalPlayerAuthor = false)
        {
            string normalised = NormaliseMessage(text);
            if (string.IsNullOrEmpty(normalised))
                return;

            string resolvedSender = !string.IsNullOrWhiteSpace(sender) ? sender.Trim() : "Companion";
            var message = new ChatMessage(ChatChannel.Companion, resolvedSender, normalised, DateTime.UtcNow, isLocalPlayerAuthor);
            EnqueueMessage(message);
        }

        /// <summary>
        /// Helper used by external systems to replay history (for example after subscribing to events).
        /// </summary>
        public void RequestFullRefresh()
        {
            for (int i = 0; i < ChannelValues.Length; i++)
            {
                var channel = ChannelValues[i];
                HistoryRefreshed?.Invoke(channel, GetHistorySnapshot(channel));
            }
        }

        /// <summary>
        /// Retrieves a snapshot of the cached history for the requested channel.
        /// </summary>
        public IReadOnlyList<ChatMessage> GetHistorySnapshot(ChatChannel channel)
        {
            lock (syncRoot)
            {
                if (!histories.TryGetValue(channel, out var list) || list.Count == 0)
                    return Array.Empty<ChatMessage>();

                return list.ToArray();
            }
        }

        /// <summary>
        /// Emits a default welcome message to the Game channel.
        /// </summary>
        public void PublishSystemWelcome()
        {
            string username = ActiveUsername;
            if (string.IsNullOrEmpty(username))
                PublishGameMessage("Welcome to Gielinor!");
            else
                PublishGameMessage($"Welcome back, {username}!");
        }

        private void EnqueueMessage(ChatMessage message)
        {
            List<ChatMessage> history;
            lock (syncRoot)
            {
                if (!histories.TryGetValue(message.Channel, out history))
                {
                    history = new List<ChatMessage>(DefaultHistoryLimit);
                    histories[message.Channel] = history;
                }

                history.Add(message);

                int limit = Mathf.Max(1, historyLimit);
                if (history.Count > limit)
                    history.RemoveRange(0, history.Count - limit);
            }

            MessageReceived?.Invoke(message);
        }

        private void RefreshCachedUsername(bool forceNotify)
        {
            string latest = SaveManager.ActiveAccountUsername ?? string.Empty;
            if (!forceNotify && string.Equals(latest, cachedActiveUsername, StringComparison.Ordinal))
                return;

            cachedActiveUsername = latest;
            ActiveUsernameChanged?.Invoke(cachedActiveUsername);
        }

        private void HandleActiveAccountUsernameChanged(string _)
        {
            RefreshCachedUsername(true);
        }

        private static string NormaliseMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string collapsed = WhitespaceRegex.Replace(text, " ");
            return collapsed.Trim();
        }
    }
}
