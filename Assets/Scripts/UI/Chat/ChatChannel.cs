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
    }
}
