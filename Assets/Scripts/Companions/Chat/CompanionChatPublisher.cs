using System;
using UI.Chat;

namespace Companions.Chat
{
    /// <summary>
    /// Centralises companion chat publishing so callers avoid duplicating service lookups,
    /// whitespace guards, and display name resolution.
    /// </summary>
    public static class CompanionChatPublisher
    {
        /// <summary>
        /// Attempts to publish a companion dialogue line using the supplied resolver.
        /// </summary>
        /// <param name="lineResolver">Delegate that produces the chat text. Evaluated lazily.</param>
        /// <param name="requireActiveCompanion">
        /// When true, the line will only be published if a companion is currently active.
        /// </param>
        /// <returns>True if a message was published; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lineResolver"/> is null.</exception>
        public static bool TryPublish(Func<string> lineResolver, bool requireActiveCompanion = false)
        {
            if (lineResolver == null)
                throw new ArgumentNullException(nameof(lineResolver));

            if (requireActiveCompanion && !CompanionManager.HasActiveCompanion)
                return false;

            var chat = ChatService.Instance;
            if (chat == null)
                return false;

            string message = lineResolver();
            if (string.IsNullOrWhiteSpace(message))
                return false;

            chat.PublishCompanionMessage(
                CompanionDisplayUtility.GetDisplayName(CompanionManager.ActiveDefinition),
                message);
            return true;
        }
    }
}
