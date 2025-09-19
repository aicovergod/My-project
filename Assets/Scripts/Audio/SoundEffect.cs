using System;

namespace Audio
{
    /// <summary>
    /// Identifiers for sound effects that can be requested through <see cref="SoundManager"/>.
    /// The enum keeps gameplay code decoupled from actual file names stored on disk.
    /// </summary>
    [Serializable]
    public enum SoundEffect
    {
        /// <summary>
        /// Played when the player gains an Attack level.
        /// </summary>
        AttackLevelUp,

        /// <summary>
        /// Played when the player gains a Defence level.
        /// </summary>
        DefenceLevelUp,

        /// <summary>
        /// Played when the player gains a Magic level.
        /// </summary>
        MagicLevelUp,

        /// <summary>
        /// Played when the player gains a Mining level.
        /// </summary>
        MiningLevelUp,

        /// <summary>
        /// Played when the player gains a Woodcutting level.
        /// </summary>
        WoodcuttingLevelUp,

        /// <summary>
        /// Played when the player gains a Fishing level.
        /// </summary>
        FishingLevelUp,

        /// <summary>
        /// Played when the player gains a Cooking level.
        /// </summary>
        CookingLevelUp,

        /// <summary>
        /// Played when the player gains a Beastmaster level. Reuses the defence level up chime.
        /// </summary>
        BeastmasterLevelUp,

        /// <summary>
        /// Played when the player dies.
        /// </summary>
        PlayerDeath,

        /// <summary>
        /// Ambient hit when chopping trees. Reserved for future use.
        /// </summary>
        TreeChop
    }
}
