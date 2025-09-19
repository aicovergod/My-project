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
        /// Played when the player gains a Hitpoints level between levels 2 and 49.
        /// </summary>
        HitpointsLevelUpLow,

        /// <summary>
        /// Played when the player gains a Hitpoints level between levels 50 and 99.
        /// </summary>
        HitpointsLevelUpHigh,

        /// <summary>
        /// Played when the player gains a Strength level between levels 2 and 49.
        /// </summary>
        StrengthLevelUpLow,

        /// <summary>
        /// Played when the player gains a Strength level between levels 50 and 99.
        /// </summary>
        StrengthLevelUpHigh,

        /// <summary>
        /// Ambient hit when chopping trees. Reserved for future use.
        /// </summary>
        TreeChop
    }
}
