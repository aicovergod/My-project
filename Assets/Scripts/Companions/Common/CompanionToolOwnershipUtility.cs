using System;
using System.Collections.Generic;
using Companions.Equipment;
using Inventory;
using Skills.Common;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions
{
    /// <summary>
    /// Provides shared ownership checks for gathering tools so companion controllers reuse
    /// a single, cache-aware implementation when validating prerequisites.
    /// </summary>
    public static class CompanionToolOwnershipUtility
    {
        /// <summary>
        /// Determines whether the companion already owns (in their backpack) or currently has equipped
        /// the tool identified by the supplied item identifier. The lookup honours the shared gathering
        /// item cache so repeated calls avoid unnecessary Resources loads.
        /// </summary>
        /// <param name="toolId">Identifier of the tool that should be resolved.</param>
        /// <param name="inventory">Inventory used to check for owned copies of the tool.</param>
        /// <param name="equipment">Equipment component used to check the currently equipped tool.</param>
        /// <param name="itemCache">
        /// Skill-specific cache dictionary that will be rebound to the shared gathering cache on demand.
        /// </param>
        /// <param name="isEquipped">Outputs whether the resolved tool is currently equipped.</param>
        /// <returns>
        /// <c>true</c> when the companion owns at least one copy of the tool or has it equipped,
        /// otherwise <c>false</c>.
        /// </returns>
        public static bool HasTool(
            string toolId,
            RuntimeInventory inventory,
            CompanionEquipment equipment,
            ref Dictionary<string, ItemData> itemCache,
            out bool isEquipped)
        {
            isEquipped = false;

            if (string.IsNullOrWhiteSpace(toolId))
                return false;

            var item = GatheringInventoryHelper.GetItemData(toolId, ref itemCache);
            if (item == null)
                return false;

            bool ownsInInventory = inventory != null && inventory.GetItemCount(item) > 0;

            if (equipment != null)
            {
                var equippedEntry = equipment.GetEquipped(EquipmentSlot.Weapon);
                isEquipped = equippedEntry.item == item;
            }

            return ownsInInventory || isEquipped;
        }
    }
}
