using System;

namespace Player.Ranks
{
    /// <summary>
    /// Enumerates the hierarchy of player account ranks available to gameplay systems.
    /// Higher values imply elevated permissions when dispatching commands or unlocking tooling.
    /// </summary>
    [Serializable]
    public enum PlayerRank
    {
        /// <summary>
        /// Default permission level granted to all accounts when no elevated status is configured.
        /// </summary>
        Player = 0,
        /// <summary>
        /// Entry-level support rank suited for moderation helpers and QA-focused tooling.
        /// </summary>
        Support = 1,
        /// <summary>
        /// Core moderation tier that unlocks disruptive world manipulation commands.
        /// </summary>
        Moderator = 2,
        /// <summary>
        /// Administrative rank intended for live-ops staff with broad control over state.
        /// </summary>
        Admin = 3,
        /// <summary>
        /// Internal development tier with unrestricted access to debugging features.
        /// </summary>
        Developer = 4,
    }
}
