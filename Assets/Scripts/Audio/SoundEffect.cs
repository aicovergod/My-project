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
        /// Ambient hit when chopping trees. Reserved for future use.
        /// </summary>
        TreeChop
    }
}
