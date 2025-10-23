using System.Collections.Generic;
using Inventory;
using Pets;
using UnityEngine;
using Companions;

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
        /// <summary>
        ///     Describes the capacity evaluation for a gathered item across the player, pet, and companion
        ///     inventories. Controllers can inspect the result to decide whether to block the action and which
        ///     feedback string to display.
        /// </summary>
        public readonly struct GatheringInventoryCapacityResult
        {
            /// <summary>The cached item reference resolved for the supplied identifier.</summary>
            public ItemData Item { get; }

            /// <summary>Total quantity that must fit in the inventory after considering pet bonuses.</summary>
            public int RequiredQuantity { get; }

            /// <summary>
            ///     Indicates whether the player's inventory alone can accept the gathered quantity without
            ///     leveraging pet overflow rules.
            /// </summary>
            public bool PlayerInventoryHasCapacity { get; }

            /// <summary>
            ///     Indicates whether either the player's inventory or the active pet's storage can accept the
            ///     gathered quantity using the standard overflow routing rules.
            /// </summary>
            public bool PlayerOrPetHasCapacity { get; }

            /// <summary>True when the companion inventory can accept the gathered quantity.</summary>
            public bool CompanionInventoryHasCapacity { get; }

            /// <summary>
            ///     Constructs the immutable result used to describe the inventory capacity check.
            /// </summary>
            /// <param name="item">Resolved <see cref="ItemData"/> for the gathered resource.</param>
            /// <param name="requiredQuantity">Quantity that must be stored.</param>
            /// <param name="playerInventoryHasCapacity">Whether the player inventory alone has space.</param>
            /// <param name="playerOrPetHasCapacity">
            ///     Whether the player inventory or pet storage can store the quantity.
            /// </param>
            /// <param name="companionInventoryHasCapacity">Whether the companion inventory has space.</param>
            public GatheringInventoryCapacityResult(
                ItemData item,
                int requiredQuantity,
                bool playerInventoryHasCapacity,
                bool playerOrPetHasCapacity,
                bool companionInventoryHasCapacity)
            {
                Item = item;
                RequiredQuantity = Mathf.Max(1, requiredQuantity);
                PlayerInventoryHasCapacity = playerInventoryHasCapacity;
                PlayerOrPetHasCapacity = playerOrPetHasCapacity;
                CompanionInventoryHasCapacity = companionInventoryHasCapacity;
            }
        }

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
            var result = EvaluateGatheredItemCapacity(
                inventory,
                itemId,
                doubleDropPetId,
                ref skillCache);

            requiredQuantity = result.RequiredQuantity;
            return result.PlayerOrPetHasCapacity;
        }

        /// <summary>
        ///     Evaluates whether the gathered item can be stored by the player's inventory, pet overflow, or
        ///     companion backpack. Returns an immutable result that describes the available capacity so callers
        ///     can decide which feedback message to surface.
        /// </summary>
        /// <param name="inventory">Player inventory attempting to store the resource.</param>
        /// <param name="itemId">Identifier of the gathered resource.</param>
        /// <param name="doubleDropPetId">Pet identifier that doubles the gathered output when active.</param>
        /// <param name="skillCache">Skill-owned cache dictionary used for item lookups.</param>
        /// <returns>
        ///     Result describing the resolved item, required quantity, and whether each inventory has space
        ///     available.
        /// </returns>
        public static GatheringInventoryCapacityResult EvaluateGatheredItemCapacity(
            Inventory.Inventory inventory,
            string itemId,
            string doubleDropPetId,
            ref Dictionary<string, ItemData> skillCache)
        {
            int requiredQuantity = 1;
            ItemData item = null;

            if (string.IsNullOrEmpty(itemId))
            {
                return new GatheringInventoryCapacityResult(
                    null,
                    requiredQuantity,
                    true,
                    true,
                    true);
            }

            item = GetItemData(itemId, ref skillCache);
            requiredQuantity = CalculateRequiredQuantity(doubleDropPetId);

            if (item == null)
            {
                return new GatheringInventoryCapacityResult(
                    null,
                    requiredQuantity,
                    true,
                    true,
                    true);
            }

            bool companionHasCapacity = EvaluateCompanionInventoryCapacity(item, requiredQuantity);

            if (inventory == null)
            {
                return new GatheringInventoryCapacityResult(
                    item,
                    requiredQuantity,
                    true,
                    true,
                    companionHasCapacity);
            }

            bool playerInventoryHasCapacity = inventory.CanAddItem(item, requiredQuantity);
            bool playerOrPetHasCapacity = playerInventoryHasCapacity;

            if (!playerOrPetHasCapacity && requiredQuantity > 1)
            {
                var petInventory = GetActivePetInventory();
                if (petInventory != null)
                {
                    bool splitAcrossInventories =
                        inventory.CanAddItem(item, 1) && petInventory.CanAddItem(item, 1);

                    playerOrPetHasCapacity = splitAcrossInventories
                        || petInventory.CanAddItem(item, requiredQuantity);
                }
            }

            return new GatheringInventoryCapacityResult(
                item,
                requiredQuantity,
                playerInventoryHasCapacity,
                playerOrPetHasCapacity,
                companionHasCapacity);
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

        /// <summary>
        ///     Resolves whether the companion inventory can accept the specified item quantity.
        /// </summary>
        /// <param name="item">Item that would be added to the inventory.</param>
        /// <param name="requiredQuantity">Quantity that must be stored.</param>
        /// <returns><c>true</c> when the companion inventory has space or is unavailable.</returns>
        private static bool EvaluateCompanionInventoryCapacity(ItemData item, int requiredQuantity)
        {
            if (item == null)
                return true;

            var companionInventoryWrapper = CompanionManager.CompanionInventory;
            if (companionInventoryWrapper == null)
                return true;

            var companionInventory = companionInventoryWrapper.InventoryComponent;
            if (companionInventory == null)
                return true;

            return companionInventory.CanAddItem(item, Mathf.Max(1, requiredQuantity));
        }
    }
}
