using System;
using Companions.Commands;
using UI.Chat;
using UnityEngine;

namespace Companions.Chat
{
    /// <summary>
    /// Handles chat callbacks relevant to the companion so it can respond to shared
    /// inventory events and interpret local stop commands issued through chat.
    /// </summary>
    public sealed class CompanionChatInventoryResponder
    {
        /// <summary>Normalised comparison string for detecting the standard full inventory game message.</summary>
        private const string PlayerInventoryFullGameMessage = "your inventory is full";

        /// <summary>Normalised comparison string for the combined player and companion inventory message.</summary>
        private const string PlayerAndCompanionInventoryFullGameMessage =
            "your inventory and your companion's inventory are full";

        /// <summary>Throttle key used for inventory full chatter.</summary>
        private const string InventoryFullChatThrottleKey = "InventoryFullMessage";

        /// <summary>Cached chat service reference used for subscription management.</summary>
        private ChatService cachedChatService;

        /// <summary>Tracks whether the component is currently subscribed to chat callbacks.</summary>
        private bool isSubscribed;

        /// <summary>
        /// Subscribes to chat messages so the companion can react to inventory events and stop commands.
        /// Safe to call repeatedly; duplicate subscriptions are ignored.
        /// </summary>
        public void Initialise()
        {
            if (isSubscribed)
                return;

            cachedChatService = ChatService.Instance;
            if (cachedChatService == null)
                return;

            cachedChatService.MessageReceived -= HandleChatMessageReceived;
            cachedChatService.MessageReceived += HandleChatMessageReceived;
            isSubscribed = true;
        }

        /// <summary>
        /// Removes the chat message subscription so no callbacks fire while the companion is inactive.
        /// </summary>
        public void Dispose()
        {
            if (!isSubscribed)
                return;

            if (cachedChatService != null)
                cachedChatService.MessageReceived -= HandleChatMessageReceived;

            cachedChatService = null;
            isSubscribed = false;
        }

        /// <summary>
        /// Reacts to system chat messages so the companion acknowledges inventory state and stop commands.
        /// </summary>
        /// <param name="message">Chat message emitted by the game channel.</param>
        private void HandleChatMessageReceived(ChatMessage message)
        {
            // Default struct instances represent unusable chat lines; guard so we ignore placeholder payloads.
            if (message.Equals(default))
                return;

            if (message.Channel == ChatChannel.Game)
            {
                HandleGameChatMessage(message);
                return;
            }

            if (!message.IsLocalPlayerAuthor)
                return;

            if (message.Channel != ChatChannel.Companion && message.Channel != ChatChannel.Public)
                return;

            TryHandleStopChatCommand(message.Text);
        }

        /// <summary>
        /// Processes game-channel messages so the companion can acknowledge shared inventory events.
        /// </summary>
        /// <param name="message">Game channel chat message.</param>
        private void HandleGameChatMessage(ChatMessage message)
        {
            if (!CompanionManager.HasActiveCompanion)
                return;

            if (string.IsNullOrWhiteSpace(message.Text))
                return;

            string trimmed = message.Text.Trim();
            if (trimmed.Length == 0)
                return;

            string normalised = trimmed.ToLowerInvariant();
            bool playerInventoryFull = string.Equals(normalised, PlayerInventoryFullGameMessage, StringComparison.Ordinal);
            bool combinedInventoryFull = string.Equals(normalised, PlayerAndCompanionInventoryFullGameMessage, StringComparison.Ordinal);

            if (!playerInventoryFull && !combinedInventoryFull)
                return;

            if (!CompanionDialogueThrottle.TryConsume(
                    InventoryFullChatThrottleKey,
                    CompanionDialogueThrottle.DefaultDelaySeconds))
                return;

            string companionLine = combinedInventoryFull
                ? CompanionChatLibrary.GetRandomPlayerAndCompanionInventoryFullLine()
                : CompanionChatLibrary.GetRandomPlayerInventoryFullLine();
            if (string.IsNullOrWhiteSpace(companionLine))
                return;

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string companionName = CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition);
            chat.PublishCompanionMessage(companionName, companionLine);

            if (CompanionManager.EnableDebugLogging)
            {
                string context = combinedInventoryFull ? "(player+companion)" : "(player)";
                Debug.Log($"[Companion] Reacted to full inventory message {context} with: {companionLine}");
            }
        }

        /// <summary>
        /// Evaluates local-player chat to determine whether a stop command should cancel the active companion action.
        /// </summary>
        /// <param name="rawText">Raw chat text entered by the player.</param>
        private void TryHandleStopChatCommand(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return;

            if (!CompanionManager.HasActiveCompanion)
                return;

            var action = CompanionManager.GetActiveAction();
            if (action == CompanionActiveAction.None)
                return;

            if (!CompanionChatCommandProcessor.TryHandleStopCommand(action, rawText))
                return;

            if (!CompanionManager.TryCancelCurrentAction())
                return;

            if (CompanionManager.EnableDebugLogging)
            {
                string trimmed = string.IsNullOrWhiteSpace(rawText) ? string.Empty : rawText.Trim();
                Debug.Log($"[Companion] Stop command '{trimmed}' cancelled active {action}.");
            }
        }
    }
}
