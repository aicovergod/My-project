/// Feature: Introduced ItemStack struct for world drop integrations.
using System;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// Lightweight value type representing an item definition and quantity pair.
    /// Used by systems that need to move item stacks without referencing inventory slots directly.
    /// </summary>
    [Serializable]
    public readonly struct ItemStack
    {
        /// <summary>Item definition contained within the stack.</summary>
        public ItemData Item { get; }

        /// <summary>Quantity associated with the stack.</summary>
        public int Quantity { get; }

        /// <summary>
        /// Creates a new stack using the provided definition and quantity, clamping negatives to zero.
        /// </summary>
        public ItemStack(ItemData item, int quantity)
        {
            Item = item;
            Quantity = Mathf.Max(0, quantity);
        }

        /// <summary>True when the stack contains a valid item reference and positive quantity.</summary>
        public bool IsValid => Item != null && Quantity > 0;

        /// <summary>Returns a new stack with the same item but a different quantity.</summary>
        public ItemStack WithQuantity(int quantity)
        {
            return new ItemStack(Item, quantity);
        }
    }
}
