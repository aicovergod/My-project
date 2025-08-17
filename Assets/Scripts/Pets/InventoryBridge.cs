using System.Collections.Generic;
using UnityEngine;
using Inventory;

namespace Pets
{
    /// <summary>
    /// Adapter around the project's inventory system with a stub fallback.
    /// </summary>
    public static class InventoryBridge
    {
        private static readonly List<ItemData> stub = new();

        /// <summary>
        /// Add an item to the player's inventory or a stub list.
        /// </summary>
        public static bool AddItem(ItemData item, int amount)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var inv = player.GetComponent<Inventory.Inventory>();
                if (inv != null)
                    return inv.AddItem(item, amount);
            }

            Debug.Log($"InventoryBridge: Added {amount}x {item?.itemName} (stub).");
            for (int i = 0; i < amount; i++)
                stub.Add(item);
            return true;
        }
    }
}