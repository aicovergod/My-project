/// Feature: Added chatbox facade for companion pickup messaging.
using UnityEngine;

namespace UI.Chat
{
    /// <summary>
    /// Lightweight facade that mirrors the legacy ChatboxUI API expected by older systems.
    /// Routes system messages through the active <see cref="ChatService"/> instance.
    /// </summary>
    public static class ChatboxUI
    {
        /// <summary>Posts a system message to the game chat channel when possible.</summary>
        public static void PostSystemMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var service = ChatService.Instance;
            if (service == null)
            {
                Debug.LogWarning("ChatboxUI.PostSystemMessage called without an active ChatService instance.");
                return;
            }

            service.PublishGameMessage(message);
        }
    }
}
