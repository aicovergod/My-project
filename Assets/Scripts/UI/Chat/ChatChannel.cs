using System;

namespace UI.Chat
{
    /// <summary>
    /// Enumerates the available chat channels within the OSRS-style HUD.
    /// Additional channels can be appended in the future without breaking
    /// existing consumers thanks to the explicit integral values.
    /// </summary>
    [Serializable]
    public enum ChatChannel
    {
        /// <summary>
        /// System and gameplay feedback messages (skill gains, notifications).
        /// </summary>
        Game = 0,

        /// <summary>
        /// Player-authored public chat visible to nearby adventurers.
        /// </summary>
        Public = 1,

        /// <summary>
        /// Dialogue shared between the player and their AI companion.
        /// </summary>
        Companion = 2,
    }

    /// <summary>
    /// Helper utilities that provide deterministic ordering and metadata for <see cref="ChatChannel"/> values.
    /// </summary>
    public static class ChatChannelUtility
    {
        /// <summary>
        /// Cached array of channel values ordered for UI presentation.
        /// </summary>
        private static readonly ChatChannel[] OrderedChannels = BuildOrderedChannels();

        /// <summary>
        /// Returns an ordered copy of all <see cref="ChatChannel"/> values suitable for UI iteration.
        /// The ordering ensures the Companion toggle appears between Game and Public channels regardless
        /// of the underlying enum values so future additions can opt-in without disrupting existing layout.
        /// </summary>
        public static ChatChannel[] GetOrderedChannels()
        {
            var copy = new ChatChannel[OrderedChannels.Length];
            Array.Copy(OrderedChannels, copy, OrderedChannels.Length);
            return copy;
        }

        /// <summary>
        /// Computes the display order rank for the supplied channel so UI systems can
        /// sort channels deterministically even if enum values differ.
        /// </summary>
        /// <param name="channel">Channel whose rank should be resolved.</param>
        /// <returns>Zero-based rank describing the preferred presentation order.</returns>
        public static int ResolveDisplayOrder(ChatChannel channel)
        {
            switch (channel)
            {
                case ChatChannel.Game:
                    return 0;
                case ChatChannel.Companion:
                    return 1;
                case ChatChannel.Public:
                    return 2;
                default:
                    return 100 + (int)channel;
            }
        }

        /// <summary>
        /// Builds the cached ordered channel array by sorting all enum values using <see cref="ResolveDisplayOrder"/>.
        /// </summary>
        private static ChatChannel[] BuildOrderedChannels()
        {
            var values = (ChatChannel[])Enum.GetValues(typeof(ChatChannel));
            Array.Sort(values, (a, b) => ResolveDisplayOrder(a).CompareTo(ResolveDisplayOrder(b)));
            return values;
        }
    }
}
