using System;

namespace UI.Chat
{
    /// <summary>
    /// Immutable data payload describing a single line of chat displayed in the HUD.
    /// </summary>
    public readonly struct ChatMessage
    {
        /// <summary>
        /// Initialises a new chat message.
        /// </summary>
        /// <param name="channel">Channel the message should appear in.</param>
        /// <param name="sender">Display name of the author. May be empty for system lines.</param>
        /// <param name="text">Message contents.</param>
        /// <param name="timestampUtc">UTC timestamp applied by the chat service.</param>
        /// <param name="isLocalPlayerAuthor">True when authored by the active player.</param>
        public ChatMessage(ChatChannel channel, string sender, string text, DateTime timestampUtc, bool isLocalPlayerAuthor)
        {
            Channel = channel;
            Sender = sender ?? string.Empty;
            Text = text ?? string.Empty;
            TimestampUtc = timestampUtc;
            IsLocalPlayerAuthor = isLocalPlayerAuthor;
        }

        /// <summary>Channel the message belongs to.</summary>
        public ChatChannel Channel { get; }

        /// <summary>Name to display alongside the chat line.</summary>
        public string Sender { get; }

        /// <summary>Message text payload.</summary>
        public string Text { get; }

        /// <summary>UTC timestamp applied when the message was emitted.</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>Indicates whether the local player authored the message.</summary>
        public bool IsLocalPlayerAuthor { get; }
    }
}
