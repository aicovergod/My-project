using System.Collections.Generic;
using Inventory;
using Pets;
using UnityEngine;

namespace Skills.Common
{
    /// <summary>
    ///     Centralises shared inventory handling logic for gathering skills. The helper keeps a single
    ///     cached lookup of <see cref="ItemData"/> assets loaded from Resources, exposes a convenience method for
    ///     retrieving those assets, and reproduces the overflow routing rules that send double-drops into
    ///     the active pet's backpack when necessary.
    /// </summary>
    public static class GatheringInventoryHelper
    {
        // Shared item cache so woodcutting, fishing, and mining all reuse the same lookup table.
        private static Dictionary<string, ItemData> sharedItemCache;

        /// <summary>
        ///     Ensures the provided skill-specific dictionary references the shared item cache. Skills keep a
        ///     field pointing to their cache so repeat lookups avoid additional Resources loads.
        /// </summary>
        /// <param name="skillCache">Dictionary owned by the skill.</param>
        public static void EnsureItemCache(ref Dictionary<string, ItemData> skillCache)
        {
            if (skillCache != null && skillCache.Count > 0)
                return;

            if (sharedItemCache == null || sharedItemCache.Count == 0)
            {
                sharedItemCache = new Dictionary<string, ItemData>();
                var items = Resources.LoadAll<ItemData>("Item");
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.id))
                        sharedItemCache[item.id] = item;
                }
            }

            skillCache = sharedItemCache;
        }

        /// <summary>
        ///     Retrieves a cached <see cref="ItemData"/> for the supplied identifier, loading the shared cache on demand.
        /// </summary>
        /// <param name="itemId">Identifier of the item that should be fetched.</param>
        /// <param name="skillCache">Skill-owned cache dictionary.</param>
        /// <returns>The matching <see cref="ItemData"/> or <c>null</c> when no asset is registered.</returns>
        public static ItemData GetItemData(string itemId, ref Dictionary<string, ItemData> skillCache)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            EnsureItemCache(ref skillCache);
            return skillCache != null && skillCache.TryGetValue(itemId, out var item) ? item : null;
        }

        /// <summary>
        ///     Validates whether the player's inventory (and pet, if applicable) can accept the gathered item.
        ///     The helper mirrors the previous per-skill logic so callers receive a boolean answer plus the
        ///     calculated quantity required for the reward.
        /// </summary>
        /// <param name="inventory">Player inventory attempting to store the resource.</param>
        /// <param name="itemId">Identifier of the gathered resource.</param>
        /// <param name="doubleDropPetId">Pet identifier that doubles the gathered output when active.</param>
        /// <param name="skillCache">Skill-owned cache dictionary used for item lookups.</param>
        /// <param name="requiredQuantity">Outputs the amount of items that must fit.</param>
        /// <returns><c>true</c> when the item can be stored, otherwise <c>false</c>.</returns>
        public static bool CanAcceptGatheredItem(
            Inventory.Inventory inventory,
            string itemId,
            string doubleDropPetId,
            ref Dictionary<string, ItemData> skillCache,
            out int requiredQuantity)
        {
            requiredQuantity = 1;

            if (inventory == null || string.IsNullOrEmpty(itemId))
                return true;

            var item = GetItemData(itemId, ref skillCache);
            if (item == null)
                return true;

            requiredQuantity = CalculateRequiredQuantity(doubleDropPetId);
            if (inventory.CanAddItem(item, requiredQuantity))
                return true;

            if (requiredQuantity <= 1)
                return false;

            var petInventory = GetActivePetInventory();
            if (petInventory == null)
                return false;

            if (inventory.CanAddItem(item, 1) && petInventory.CanAddItem(item, 1))
                return true;

            return petInventory.CanAddItem(item, requiredQuantity);
        }

        /// <summary>
        ///     Determines whether the active pet doubles the gathered resource.
        /// </summary>
        /// <param name="doubleDropPetId">Identifier of the pet that grants the bonus.</param>
        /// <returns>2 when the pet is active, otherwise 1.</returns>
        private static int CalculateRequiredQuantity(string doubleDropPetId)
        {
            if (string.IsNullOrEmpty(doubleDropPetId))
                return 1;

            var activePet = PetDropSystem.ActivePet;
            return activePet != null && activePet.id == doubleDropPetId ? 2 : 1;
        }

        /// <summary>
        ///     Resolves the inventory component attached to the active pet, if any.
        /// </summary>
        /// <returns>The pet inventory when available, otherwise <c>null</c>.</returns>
        private static Inventory.Inventory GetActivePetInventory()
        {
            var petObject = PetDropSystem.ActivePetObject;
            if (petObject == null)
                return null;

            var storage = petObject.GetComponent<PetStorage>();
            return storage != null ? storage.GetComponent<Inventory.Inventory>() : null;
        }
    }
}
