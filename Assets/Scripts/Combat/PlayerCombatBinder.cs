using UnityEngine;
using Player;

namespace Combat
{
    /// <summary>
    /// Interface providing combat stats for an entity.
    /// </summary>
    public interface ICombatProfile
    {
        CombatantStats GetCombatStats();
    }

    /// <summary>
    /// Component that allows the player to swap combat stat providers, enabling pet merging.
    /// </summary>
    public class PlayerCombatBinder : MonoBehaviour
    {
        [SerializeField] private PlayerCombatLoadout loadout;

        private ICombatProfile currentProfile;
        private PlayerProfile playerProfile;

        private void Awake()
        {
            if (loadout == null)
                loadout = GetComponent<PlayerCombatLoadout>();
            playerProfile = new PlayerProfile(loadout);
            currentProfile = playerProfile;
        }

        /// <summary>Use the specified profile for combat calculations.</summary>
        public void UseProfile(ICombatProfile profile)
        {
            currentProfile = profile ?? playerProfile;
        }

        /// <summary>Restore the player's own combat profile.</summary>
        public void RestorePlayerProfile()
        {
            currentProfile = playerProfile;
        }

        /// <summary>Get combat stats from the active profile.</summary>
        public CombatantStats GetCombatantStats()
        {
            return currentProfile != null ? currentProfile.GetCombatStats() : new CombatantStats();
        }

        private class PlayerProfile : ICombatProfile
        {
            private readonly PlayerCombatLoadout loadout;

            public PlayerProfile(PlayerCombatLoadout loadout)
            {
                this.loadout = loadout;
            }

            public CombatantStats GetCombatStats()
            {
                return loadout != null ? loadout.GetCombatantStats() : new CombatantStats();
            }
        }
    }
}
