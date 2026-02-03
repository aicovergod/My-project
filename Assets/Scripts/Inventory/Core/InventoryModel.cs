// Assets/Scripts/Inventory/Core/InventoryModel.cs
using System;
using UnityEngine;
using Inventory;

namespace Inventory.Core
{
    /// <summary>
    /// Data-oriented model that owns the inventory slot array and exposes the
    /// mutation APIs used by <see cref="Inventory.Inventory"/>. The model raises
    /// change notifications so UI and persistence layers can react without
    /// coupling to the backing array.
    /// </summary>
    public sealed class InventoryModel
    {
        /// <summary>
        /// Serializable payload used for persistence.
        /// </summary>
        [Serializable]
        public class InventorySaveData
        {
            public SlotData[] slots;
        }

        /// <summary>
        /// Serializable representation of a single slot for save data.
        /// </summary>
        [Serializable]
        public class SlotData
        {
            public string id;
            public int count;
        }

        /// <summary>
        /// Raised whenever the overall inventory contents change. The boolean
        /// payload indicates whether listeners should immediately persist the
        /// change (true) or only refresh visuals (false).
        /// </summary>
        public event Action<bool> InventoryChanged;

        /// <summary>
        /// Raised when a specific slot changes. Observers can rebuild visuals
        /// for the provided index using the latest entry data.
        /// </summary>
        public event Action<int, InventoryEntry> SlotChanged;

        /// <summary>
        /// Delegate invoked to determine whether a given item can be stored in
        /// this inventory instance. When null all items are allowed.
        /// </summary>
        public Func<ItemData, bool> CanStoreRule { get; set; }

        private InventoryEntry[] slots;
        private ItemCombinationDatabase combinationDatabase;

        /// <summary>
        /// Creates a new model with the requested slot count.
        /// </summary>
        public InventoryModel(int size, Func<ItemData, bool> canStoreRule = null, ItemCombinationDatabase combinationDatabase = null)
        {
            size = Mathf.Max(1, size);
            slots = new InventoryEntry[size];
            CanStoreRule = canStoreRule;
            this.combinationDatabase = combinationDatabase;
        }

        /// <summary>
        /// Number of slots currently represented by the model.
        /// </summary>
        public int Size => slots.Length;

        /// <summary>
        /// Updates the combination database reference used when combining items.
        /// </summary>
        public void SetCombinationDatabase(ItemCombinationDatabase database)
        {
            combinationDatabase = database;
        }

        /// <summary>
        /// Resizes the slot array while preserving existing contents whenever
        /// possible.
        /// </summary>
        public void Resize(int newSize)
        {
            newSize = Mathf.Max(1, newSize);
            if (newSize == slots.Length)
                return;

            var previous = slots;
            slots = new InventoryEntry[newSize];
            int copyLength = Mathf.Min(previous.Length, slots.Length);
            Array.Copy(previous, slots, copyLength);

            // Notify observers for all slots so UIs rebuild from the new data.
            for (int i = 0; i < slots.Length; i++)
                SlotChanged?.Invoke(i, slots[i]);

            if (previous.Length > slots.Length)
                RaiseInventoryChanged(true);
            else
                RaiseInventoryChanged(false);
        }

        /// <summary>
        /// Returns a copy of the entry at <paramref name="index"/>.
        /// </summary>
        public InventoryEntry GetEntry(int index)
        {
            if (index < 0 || index >= slots.Length)
                return default;
            return slots[index];
        }

        /// <summary>
        /// Returns true if the provided item can be stored in this inventory.
        /// </summary>
        private bool CanStore(ItemData item)
        {
            if (item == null)
                return false;
            return CanStoreRule == null || CanStoreRule(item);
        }

        /// <summary>
        /// Returns the total number of instances of <paramref name="item"/> in
        /// the inventory.
        /// </summary>
        public int GetItemCount(ItemData item)
        {
            if (item == null)
                return 0;

            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].item == item)
                    count += slots[i].count;
            }

            return count;
        }

        /// <summary>
        /// Returns true if an item with the given identifier exists in any slot.
        /// </summary>
        public bool HasItem(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                if (entry.item != null && entry.item.id == id)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if there is capacity to add <paramref name="quantity"/>
        /// of the specified item.
        /// </summary>
        public bool CanAddItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0)
                return false;
            if (!CanStore(item))
                return false;

            int space = 0;

            if (item.stackable)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].item == item)
                        space += item.MaxStack - slots[i].count;
                    else if (slots[i].item == null)
                        space += item.MaxStack;

                    if (space >= quantity)
                        return true;
                }
            }
            else
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].item == null)
                        space++;
                    if (space >= quantity)
                        return true;
                }
            }

            return space >= quantity;
        }

        /// <summary>
        /// Adds an item to the inventory, stacking when possible.
        /// </summary>
        public bool AddItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0)
                return false;
            if (!CanStore(item))
                return false;
            if (!CanAddItem(item, quantity))
                return false;

            int remaining = quantity;

            if (item.stackable)
            {
                for (int i = 0; i < slots.Length && remaining > 0; i++)
                {
                    if (slots[i].item == item && slots[i].count < item.MaxStack)
                    {
                        int add = Mathf.Min(item.MaxStack - slots[i].count, remaining);
                        if (add > 0)
                        {
                            slots[i].count += add;
                            remaining -= add;
                            SlotChanged?.Invoke(i, slots[i]);
                        }
                    }
                }
            }

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (slots[i].item == null)
                {
                    slots[i].item = item;
                    slots[i].count = item.stackable ? Mathf.Min(item.MaxStack, remaining) : 1;
                    remaining -= slots[i].count;
                    SlotChanged?.Invoke(i, slots[i]);
                }
            }

            bool success = remaining <= 0;
            if (success)
                RaiseInventoryChanged(true);
            return success;
        }

        /// <summary>
        /// Removes up to <paramref name="count"/> of the specified item from the
        /// inventory.
        /// </summary>
        public bool RemoveItem(ItemData item, int count)
        {
            if (item == null || count <= 0)
                return false;

            if (GetItemCount(item) < count)
                return false;

            for (int i = 0; i < slots.Length && count > 0; i++)
            {
                if (slots[i].item == item)
                {
                    int remove = Mathf.Min(count, slots[i].count);
                    if (remove > 0)
                    {
                        slots[i].count -= remove;
                        count -= remove;
                        if (slots[i].count <= 0)
                            slots[i].item = null;
                        SlotChanged?.Invoke(i, slots[i]);
                    }
                }
            }

            bool success = count <= 0;
            if (success)
                RaiseInventoryChanged(true);
            return success;
        }

        /// <summary>
        /// Removes the first occurrence of an item with the given identifier.
        /// </summary>
        public bool RemoveItem(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].item != null && slots[i].item.id == id)
                {
                    slots[i].count--;
                    if (slots[i].count <= 0)
                        slots[i].item = null;
                    SlotChanged?.Invoke(i, slots[i]);

                    RaiseInventoryChanged(true);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Replaces the item at <paramref name="slotIndex"/> if it currently
        /// contains <paramref name="oldItem"/>.
        /// </summary>
        public bool ReplaceItem(int slotIndex, ItemData oldItem, ItemData newItem, int newCount)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length)
                return false;

            var entry = slots[slotIndex];
            if (entry.item != oldItem)
                return false;

            entry.item = newItem;
            entry.count = newCount;
            slots[slotIndex] = entry;
            SlotChanged?.Invoke(slotIndex, entry);
            RaiseInventoryChanged(true);
            return true;
        }

        /// <summary>
        /// Clears a slot and raises the associated events.
        /// </summary>
        public void ClearSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length)
                return;

            slots[slotIndex] = default;
            SlotChanged?.Invoke(slotIndex, slots[slotIndex]);
            RaiseInventoryChanged(true);
        }

        /// <summary>
        /// Removes and returns the entry stored at the provided index.
        /// </summary>
        public InventoryEntry TakeEntry(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length)
                return default;

            var entry = slots[slotIndex];
            slots[slotIndex] = default;
            SlotChanged?.Invoke(slotIndex, slots[slotIndex]);
            RaiseInventoryChanged(true);
            return entry;
        }

        /// <summary>
        /// Directly assigns a slot entry, replacing any existing contents.
        /// </summary>
        public bool SetEntry(int slotIndex, InventoryEntry entry)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length)
                return false;

            if (entry.item != null && !CanStore(entry.item))
                return false;

            slots[slotIndex] = entry;
            SlotChanged?.Invoke(slotIndex, entry);
            RaiseInventoryChanged(true);
            return true;
        }

        /// <summary>
        /// Clears every slot in the inventory and raises change notifications when any
        /// entries were removed.
        /// </summary>
        /// <returns>
        /// <c>true</c> when at least one slot transitioned from occupied to empty;
        /// otherwise, <c>false</c> to indicate the inventory was already empty.
        /// </returns>
        public bool ClearAllSlots()
        {
            bool clearedAny = false;

            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                if (entry.item == null && entry.count <= 0)
                    continue;

                slots[i] = default;
                SlotChanged?.Invoke(i, slots[i]);
                clearedAny = true;
            }

            if (clearedAny)
                RaiseInventoryChanged(true);

            return clearedAny;
        }

        /// <summary>
        /// Removes a quantity from the specified slot without dropping it in the
        /// world.
        /// </summary>
        public void RemoveFromSlot(int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length)
                return;

            var entry = slots[slotIndex];
            if (entry.item == null)
                return;

            int remove = Mathf.Clamp(quantity, 1, entry.count);
            entry.count -= remove;
            if (entry.count <= 0)
                entry.item = null;
            slots[slotIndex] = entry;
            SlotChanged?.Invoke(slotIndex, entry);
            RaiseInventoryChanged(true);
        }

        /// <summary>
        /// Splits a stack, moving <paramref name="quantity"/> items to a free
        /// slot when available.
        /// </summary>
        public void SplitStack(int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length)
                return;

            var entry = slots[slotIndex];
            if (entry.item == null || !entry.item.splittable)
                return;

            int amount = Mathf.Clamp(quantity, 1, entry.count - 1);
            if (amount <= 0)
                return;

            int target = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].item == null)
                {
                    target = i;
                    break;
                }
            }

            if (target == -1)
                return;

            entry.count -= amount;
            slots[slotIndex] = entry;
            SlotChanged?.Invoke(slotIndex, entry);

            slots[target].item = entry.item;
            slots[target].count = amount;
            SlotChanged?.Invoke(target, slots[target]);

            RaiseInventoryChanged(true);
        }

        /// <summary>
        /// Attempts to combine two slots using the configured combination
        /// database. Returns true when a combination was processed.
        /// </summary>
        public bool CombineItems(int srcIndex, int dstIndex, out bool keepSelection)
        {
            keepSelection = false;
            if (srcIndex < 0 || dstIndex < 0 || srcIndex >= slots.Length || dstIndex >= slots.Length)
                return false;
            if (combinationDatabase == null)
                return false;

            var srcItem = slots[srcIndex].item;
            var dstItem = slots[dstIndex].item;
            if (srcItem == null || dstItem == null)
                return false;

            var result = combinationDatabase.GetResult(srcItem, dstItem);
            if (result == null)
                return false;

            var originalSrc = slots[srcIndex];
            var originalDst = slots[dstIndex];

            slots[srcIndex].count--;
            if (slots[srcIndex].count <= 0)
                slots[srcIndex].item = null;
            SlotChanged?.Invoke(srcIndex, slots[srcIndex]);

            slots[dstIndex].count--;
            if (slots[dstIndex].count <= 0)
                slots[dstIndex].item = null;
            SlotChanged?.Invoke(dstIndex, slots[dstIndex]);

            bool added = AddItem(result, 1);
            if (added)
            {
                // AddItem already persisted the change. Emit a refresh-only event so listeners update visuals without
                // triggering a redundant save.
                RaiseInventoryChanged(false);
                return true;
            }

            // Restore consumed ingredients if the result could not be placed.
            slots[srcIndex] = originalSrc;
            SlotChanged?.Invoke(srcIndex, slots[srcIndex]);

            slots[dstIndex] = originalDst;
            SlotChanged?.Invoke(dstIndex, slots[dstIndex]);
            return false;
        }

        /// <summary>
        /// Serialises the inventory contents into a simple DTO for persistence.
        /// </summary>
        public InventorySaveData CaptureState()
        {
            var data = new InventorySaveData
            {
                slots = new SlotData[slots.Length]
            };

            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                data.slots[i] = new SlotData
                {
                    id = entry.item != null ? entry.item.id : string.Empty,
                    count = entry.item != null ? entry.count : 0
                };
            }

            return data;
        }

        /// <summary>
        /// Restores the inventory contents from serialised state.
        /// </summary>
        public void RestoreState(InventorySaveData data)
        {
            if (data?.slots == null)
                return;

            int len = Mathf.Min(slots.Length, data.slots.Length);
            for (int i = 0; i < len; i++)
            {
                var slot = data.slots[i];
                if (slot == null)
                {
                    slots[i] = default;
                    SlotChanged?.Invoke(i, slots[i]);
                    continue;
                }
                if (!string.IsNullOrEmpty(slot.id))
                {
                    var item = ItemDatabase.GetItem(slot.id);
                    if (item == null)
                    {
                        Debug.LogWarning($"InventoryModel.RestoreState: Failed to resolve item id '{slot.id}' for slot {i}. Resetting slot to empty.");
                        slots[i] = default;
                    }
                    else
                    {
                        slots[i].item = item;
                        slots[i].count = slot.count;
                    }
                }
                else
                {
                    slots[i] = default;
                }

                SlotChanged?.Invoke(i, slots[i]);
            }

            RaiseInventoryChanged(false);
        }

        private void RaiseInventoryChanged(bool persist)
        {
            InventoryChanged?.Invoke(persist);
        }
    }
}
