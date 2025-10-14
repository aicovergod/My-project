// Assets/Scripts/Inventory/InventoryEntry.cs
using System;

namespace Inventory
{
    /// <summary>
    /// Represents a single inventory slot entry containing a reference to the stored
    /// <see cref="ItemData"/> and its associated stack count. Keeping this struct in the
    /// Inventory namespace preserves the original public API so existing systems that
    /// reference <c>Inventory.InventoryEntry</c> continue to compile.
    /// </summary>
    [Serializable]
    public struct InventoryEntry
    {
        /// <summary>
        /// Item currently stored in the slot. Null when the slot is empty.
        /// </summary>
        public ItemData item;

        /// <summary>
        /// Number of items stacked in the slot. Zero when the slot is empty.
        /// </summary>
        public int count;
    }
}
