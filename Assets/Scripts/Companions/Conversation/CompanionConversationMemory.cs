using System;
using System.Collections.Generic;
using Core.Save;
using UI.Chat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Companions.Conversation
{
    /// <summary>
    /// Records recent companion conversations so dialogue systems can reference the latest topics
    /// across play sessions. The component listens to the companion chat channel, stores a bounded
    /// transcript, and persists it through the shared <see cref="SaveManager"/> infrastructure.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionConversationMemory : MonoBehaviour, ISaveable
    {
        private const string SaveKey = "companion_conversation_history";

        [SerializeField, Tooltip("Maximum number of transcript entries to retain."), Min(1)]
        private int maxEntries = 120;

        [SerializeField, Tooltip("Maximum age (in minutes) of entries kept in memory. Set to 0 to disable time trimming."), Min(0f)]
        private float retentionWindowMinutes = 45f;

        [SerializeField, Tooltip("Optional debug flag that emits verbose logging when messages are captured.")]
        private bool enableDebugLogging;

        /// <summary>Backing list containing the ordered conversation transcript.</summary>
        private readonly List<ConversationEntry> entries = new List<ConversationEntry>(64);

        /// <summary>True once a chat subscription is active so duplicate hooks are prevented.</summary>
        private bool chatSubscribed;

        /// <summary>Cached reference to the chat service instance that currently has the listener bound.</summary>
        private ChatService subscribedChat;

        /// <summary>Tracks the last detected greeting so dialogue logic can throttle repeats.</summary>
        public DateTime? LastGreetingUtc { get; private set; }

        /// <summary>Tracks the last detected question so call-and-response flows can branch quickly.</summary>
        public DateTime? LastQuestionUtc { get; private set; }

        /// <summary>Stores the most recent mood shared by the player.</summary>
        public string LastKnownPlayerMood { get; private set; } = string.Empty;

        /// <summary>Timestamp when <see cref="LastKnownPlayerMood"/> was last updated.</summary>
        public DateTime? LastKnownPlayerMoodUtc { get; private set; }

        /// <summary>Stores the most recent status response emitted by the companion.</summary>
        public string LastStatusResponse { get; private set; } = string.Empty;

        /// <summary>Timestamp when <see cref="LastStatusResponse"/> was last updated.</summary>
        public DateTime? LastStatusResponseUtc { get; private set; }

        private static readonly string[] GreetingKeywords =
        {
            "hello",
            "hi",
            "hey",
            "greetings",
            "well met"
        };

        /// <summary>
        /// Identifies the speaker that authored a conversation entry.
        /// </summary>
        public enum Speaker
        {
            Player = 0,
            Companion = 1,
            System = 2
        }

        /// <summary>
        /// Immutable runtime representation of a single companion conversation entry.
        /// </summary>
        public readonly struct ConversationEntry
        {
            public ConversationEntry(Speaker speaker, string message, DateTime timestampUtc)
            {
                Speaker = speaker;
                Message = message ?? string.Empty;
                TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
            }

            /// <summary>Speaker that authored the line.</summary>
            public Speaker Speaker { get; }

            /// <summary>Message payload supplied by the chat log.</summary>
            public string Message { get; }

            /// <summary>UTC timestamp captured when the line was recorded.</summary>
            public DateTime TimestampUtc { get; }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;

            SaveManager.Register(this);
            TrySubscribeToChatService();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeFromChat();

            // Persist the latest state before detaching so the next session resumes the same transcript.
            Save();
            SaveManager.Unregister(this);
        }

        /// <summary>
        /// Retrieves the most recent entries, returning them in chronological order.
        /// </summary>
        /// <param name="count">Maximum number of entries that should be returned.</param>
        public IReadOnlyList<ConversationEntry> GetRecentEntries(int count)
        {
            if (count <= 0 || entries.Count == 0)
                return Array.Empty<ConversationEntry>();

            int clamped = Mathf.Min(count, entries.Count);
            int startIndex = entries.Count - clamped;
            var snapshot = new ConversationEntry[clamped];
            entries.CopyTo(startIndex, snapshot, 0, clamped);
            return snapshot;
        }

        /// <summary>
        /// Appends a new entry to the transcript using the provided speaker and text payload.
        /// The helper automatically trims the log and persists the update.
        /// </summary>
        /// <param name="speaker">Speaker responsible for the dialogue line.</param>
        /// <param name="text">Text that should be stored. Empty or whitespace strings are ignored.</param>
        public void AppendEntry(Speaker speaker, string text)
        {
            AppendEntry(speaker, text, DateTime.UtcNow);
        }

        /// <summary>
        /// Updates the cached player mood so the conversation service can acknowledge it in future replies.
        /// </summary>
        /// <param name="mood">Textual description of the player's mood.</param>
        /// <param name="timestampUtc">Timestamp to record for the update.</param>
        public void SetLastKnownPlayerMood(string mood, DateTime timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(mood))
                return;

            string trimmed = mood.Trim();
            LastKnownPlayerMood = trimmed;
            LastKnownPlayerMoodUtc = EnsureUtc(timestampUtc);

            if (enableDebugLogging)
                Debug.Log($"[CompanionConversationMemory] Recorded player mood '{LastKnownPlayerMood}' at {LastKnownPlayerMoodUtc:o}.");

            Save();
        }

        /// <summary>
        /// Stores the most recent companion status response so repeat messages can be avoided.
        /// </summary>
        /// <param name="responseText">Status line emitted by the companion.</param>
        /// <param name="timestampUtc">Timestamp to log for the response.</param>
        public void RegisterStatusResponse(string responseText, DateTime timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return;

            LastStatusResponse = responseText.Trim();
            LastStatusResponseUtc = EnsureUtc(timestampUtc);

            if (enableDebugLogging)
                Debug.Log($"[CompanionConversationMemory] Registered status response '{LastStatusResponse}'.");

            Save();
        }

        /// <summary>
        /// Appends a new entry using an explicit timestamp. Exposed to support deterministic unit tests.
        /// </summary>
        /// <param name="speaker">Speaker responsible for the dialogue line.</param>
        /// <param name="text">Text that should be stored. Empty or whitespace strings are ignored.</param>
        /// <param name="timestampUtc">Timestamp that should be recorded for the entry.</param>
        public void AppendEntry(Speaker speaker, string text, DateTime timestampUtc)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string trimmed = text.Trim();
            AppendEntryInternal(new ConversationEntry(speaker, trimmed, timestampUtc));
        }

        /// <summary>
        /// Removes all history and metadata. Primarily intended for automated tests and debug tooling.
        /// </summary>
        [ContextMenu("Clear Conversation History")]
        public void ClearHistory()
        {
            entries.Clear();
            LastGreetingUtc = null;
            LastQuestionUtc = null;
            LastKnownPlayerMood = string.Empty;
            LastKnownPlayerMoodUtc = null;
            LastStatusResponse = string.Empty;
            LastStatusResponseUtc = null;
            Save();
        }

        /// <inheritdoc />
        public void Load()
        {
            entries.Clear();
            LastGreetingUtc = null;
            LastQuestionUtc = null;
            LastKnownPlayerMood = string.Empty;
            LastKnownPlayerMoodUtc = null;
            LastStatusResponse = string.Empty;
            LastStatusResponseUtc = null;

            var data = SaveManager.Load<ConversationLogData>(SaveKey);
            if (data?.entries != null)
            {
                for (int i = 0; i < data.entries.Count; i++)
                {
                    var entryData = data.entries[i];
                    var timestamp = SafeCreateUtc(entryData.timestampTicks);
                    var entry = new ConversationEntry(entryData.speaker, entryData.text ?? string.Empty, timestamp);
                    entries.Add(entry);
                    EvaluateEntryForMetadata(entry);
                }
            }

            if (data != null)
            {
                LastKnownPlayerMood = data.lastKnownPlayerMood ?? string.Empty;
                LastKnownPlayerMoodUtc = SafeCreateUtcNullable(data.lastKnownPlayerMoodTimestampTicks);
                LastStatusResponse = data.lastStatusResponse ?? string.Empty;
                LastStatusResponseUtc = SafeCreateUtcNullable(data.lastStatusResponseTicks);
            }

            bool trimmed = TrimEntries(DateTime.UtcNow);
            if (trimmed)
                Save();
        }

        /// <inheritdoc />
        public void Save()
        {
            var payload = new ConversationLogData
            {
                entries = new List<ConversationEntryData>(entries.Count),
                lastKnownPlayerMood = LastKnownPlayerMood,
                lastKnownPlayerMoodTimestampTicks = LastKnownPlayerMoodUtc?.Ticks ?? 0,
                lastStatusResponse = LastStatusResponse,
                lastStatusResponseTicks = LastStatusResponseUtc?.Ticks ?? 0
            };

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                payload.entries.Add(new ConversationEntryData
                {
                    speaker = entry.Speaker,
                    text = entry.Message,
                    timestampTicks = entry.TimestampUtc.Ticks
                });
            }

            SaveManager.Save(SaveKey, payload);
        }

        private void HandleSceneLoaded(Scene _, LoadSceneMode __)
        {
            TrySubscribeToChatService();
        }

        /// <summary>
        /// Attempts to subscribe to the chat service when the singleton becomes available.
        /// </summary>
        private void TrySubscribeToChatService()
        {
            // Unity null check accounts for the previous instance being destroyed between scene loads.
            if (chatSubscribed && subscribedChat == null)
                chatSubscribed = false;

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            if (chatSubscribed && subscribedChat == chat)
                return;

            // Ensure the listener is removed from any lingering instance before rebinding to the new one.
            if (subscribedChat != null)
                subscribedChat.MessageReceived -= HandleMessageReceived;

            chat.MessageReceived -= HandleMessageReceived;
            chat.MessageReceived += HandleMessageReceived;
            chatSubscribed = true;
            subscribedChat = chat;
        }

        /// <summary>
        /// Removes the chat subscription when the component is disabled or destroyed.
        /// </summary>
        private void UnsubscribeFromChat()
        {
            if (!chatSubscribed)
                return;

            if (subscribedChat != null)
                subscribedChat.MessageReceived -= HandleMessageReceived;

            chatSubscribed = false;
            subscribedChat = null;
        }

        private void HandleMessageReceived(ChatMessage message)
        {
            if (message.Channel != ChatChannel.Companion)
                return;

            var speaker = message.IsLocalPlayerAuthor ? Speaker.Player : Speaker.Companion;
            var entry = new ConversationEntry(speaker, message.Text, message.TimestampUtc);
            AppendEntryInternal(entry);
        }

        /// <summary>
        /// Shared append logic that applies metadata, trimming, and persistence in one place.
        /// </summary>
        private void AppendEntryInternal(ConversationEntry entry)
        {
            entries.Add(entry);
            EvaluateEntryForMetadata(entry);

            if (enableDebugLogging)
            {
                Debug.Log($"[CompanionConversationMemory] Logged {entry.Speaker} line at {entry.TimestampUtc:o}: {entry.Message}");
            }

            bool trimmed = TrimEntries(DateTime.UtcNow);
            if (trimmed)
            {
                if (enableDebugLogging)
                    Debug.Log("[CompanionConversationMemory] Trimmed expired conversation entries.");
            }

            Save();
        }

        /// <summary>
        /// Evaluates an entry and updates the metadata caches when the content matches tracked topics.
        /// </summary>
        private void EvaluateEntryForMetadata(ConversationEntry entry)
        {
            string lower = entry.Message.ToLowerInvariant();

            if (LooksLikeGreeting(lower))
                LastGreetingUtc = entry.TimestampUtc;

            if (lower.Contains("?"))
                LastQuestionUtc = entry.TimestampUtc;
        }

        /// <summary>
        /// Removes entries that exceed the retention window or capacity. Returns true when the list changed.
        /// </summary>
        private bool TrimEntries(DateTime nowUtc)
        {
            bool modified = false;
            TimeSpan? retentionWindow = ResolveRetentionWindow();
            if (retentionWindow.HasValue)
            {
                DateTime cutoff = nowUtc - retentionWindow.Value;
                int removed = entries.RemoveAll(e => e.TimestampUtc < cutoff);
                if (removed > 0)
                    modified = true;
            }

            int limit = Mathf.Max(1, maxEntries);
            if (entries.Count > limit)
            {
                int excess = entries.Count - limit;
                entries.RemoveRange(0, excess);
                modified = true;
            }

            if (modified)
                RebuildMetadata();

            return modified;
        }

        /// <summary>
        /// Recomputes metadata caches from the current transcript to keep them consistent after trimming.
        /// </summary>
        private void RebuildMetadata()
        {
            LastGreetingUtc = null;
            LastQuestionUtc = null;

            for (int i = 0; i < entries.Count; i++)
                EvaluateEntryForMetadata(entries[i]);
        }

        private static bool LooksLikeGreeting(string lower)
        {
            if (string.IsNullOrEmpty(lower))
                return false;

            for (int i = 0; i < GreetingKeywords.Length; i++)
            {
                if (lower.StartsWith(GreetingKeywords[i], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private TimeSpan? ResolveRetentionWindow()
        {
            if (retentionWindowMinutes <= 0f)
                return null;

            return TimeSpan.FromMinutes(retentionWindowMinutes);
        }

        private static DateTime EnsureUtc(DateTime timestamp)
        {
            return timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
        }

        private static DateTime SafeCreateUtc(long ticks)
        {
            if (ticks <= 0)
                return DateTime.UtcNow;

            try
            {
                return new DateTime(ticks, DateTimeKind.Utc);
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private static DateTime? SafeCreateUtcNullable(long ticks)
        {
            if (ticks <= 0)
                return null;

            try
            {
                return new DateTime(ticks, DateTimeKind.Utc);
            }
            catch
            {
                return null;
            }
        }

        [Serializable]
        private sealed class ConversationLogData
        {
            public List<ConversationEntryData> entries = new List<ConversationEntryData>();
            public string lastKnownPlayerMood;
            public long lastKnownPlayerMoodTimestampTicks;
            public string lastStatusResponse;
            public long lastStatusResponseTicks;
        }

        [Serializable]
        private struct ConversationEntryData
        {
            public Speaker speaker;
            public string text;
            public long timestampTicks;
        }
    }
}
