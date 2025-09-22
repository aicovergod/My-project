// ------------------------------------------------------------------------------
// CHANGES: Introduced a shared helper for resolving the active Player instance
// after a scene load so login resume and other systems can safely locate the
// in-scene prefab without instantiating duplicates.
// ------------------------------------------------------------------------------
using UnityEngine;

namespace Player
{
    /// <summary>
    /// Utility methods that locate the active player object within the currently loaded scene.
    /// </summary>
    public static class PlayerLocator
    {
        /// <summary>
        /// Attempts to locate the player object by tag or by common player components.
        /// </summary>
        /// <param name="player">Outputs the resolved player GameObject when found.</param>
        /// <returns>True when a player object could be resolved.</returns>
        public static bool TryFindPlayer(out GameObject player)
        {
            player = GameObject.FindWithTag("Player");
            if (player != null)
                return true;

            var mover = Object.FindObjectOfType<PlayerMover>(true);
            if (mover != null)
            {
                player = mover.gameObject;
                return true;
            }

            var hitpoints = Object.FindObjectOfType<PlayerHitpoints>(true);
            if (hitpoints != null)
            {
                player = hitpoints.gameObject;
                return true;
            }

            player = null;
            return false;
        }
    }
}
