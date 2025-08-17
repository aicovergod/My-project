using UnityEngine;
using Inventory;

namespace Pets
{
    /// <summary>
    /// Utility to spawn pets when their item is used.
    /// </summary>
    public static class PetUseHandler
    {
        /// <summary>
        /// Attempt to use the given inventory item as a pet summon.
        /// </summary>
        public static bool TryUse(ItemData item)
        {
            var pet = PetDropSystem.FindPetByItem(item);
            if (pet == null)
                return false;

            var player = GameObject.FindGameObjectWithTag("Player");
            Vector3 pos = player != null ? player.transform.position : Vector3.zero;
            PetDropSystem.SpawnPet(pet, pos);
            return true;
        }
    }
}