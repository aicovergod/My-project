using System;
using UnityEngine;
using Core.Save;
using Player;
using ShopSystem;

namespace Inventory
{
    public struct InventoryEntry
    {
        public ItemData item;
        public int count;
    }

    /// <summary>
    /// Indicates how a stack split action should be handled.
    /// </summary>
    public enum StackSplitType
    {
        Drop,
        Sell,
        Split
    }

    /// <summary>
    /// Handles the logical data portion of the inventory.  This component stores
    /// item stacks, exposes operations to mutate them and persists the state via
    /// the <see cref="SaveManager"/>.  UI concerns are delegated to
    /// <see cref="InventoryUI"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryModel : MonoBehaviour
    {
        [Header("Inventory")]
        [Tooltip("Maximum number of items the inventory can hold.")]
        public int size = 20;

        private InventoryEntry[] items;

        // Active shop context when interacting with a shop
        private Shop currentShop;

        private PlayerMover playerMover;

        public event Action OnInventoryChanged;

        private const string SaveKey = "InventoryData";

        public bool CanDropItems => playerMover == null || playerMover.CanDrop;

        public InventoryEntry GetEntry(int index)
        {
            if (index < 0 || index >= items.Length)
                return default;
            return items[index];
        }

        public int Count => items?.Length ?? 0;

        private void Awake()
        {
            size = Mathf.Max(1, size);
            items = new InventoryEntry[size];
            playerMover = GetComponent<PlayerMover>();
            Load();
        }

        /// <summary>
        /// Adds an item, stacking when possible. Returns true if the entire
        /// quantity could be added.
        /// </summary>
        public bool AddItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0)
                return false;

            if (!CanAddItem(item, quantity))
                return false;

            int remaining = quantity;

            if (item.stackable)
            {
                for (int i = 0; i < items.Length && remaining > 0; i++)
                {
                    if (items[i].item == item && items[i].count < item.maxStack)
                    {
                        int add = Mathf.Min(item.maxStack - items[i].count, remaining);
                        items[i].count += add;
                        remaining -= add;
                    }
                }
            }

            for (int i = 0; i < items.Length && remaining > 0; i++)
            {
                if (items[i].item == null)
                {
                    items[i].item = item;
                    items[i].count = item.stackable ? Mathf.Min(item.maxStack, remaining) : 1;
                    remaining -= items[i].count;
                }
            }

            bool success = remaining <= 0;
            if (success)
                OnInventoryChanged?.Invoke();
            return success;
        }

        /// <summary>
        /// Returns the total number of a given item currently in the inventory.
        /// </summary>
        public int GetItemCount(ItemData item)
        {
            int count = 0;
            if (item == null) return count;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].item == item)
                    count += items[i].count;
            }
            return count;
        }

        /// <summary>
        /// Returns true if there is room to add the specified quantity of the
        /// given item.
        /// </summary>
        public bool CanAddItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0)
                return false;

            int space = 0;

            if (item.stackable)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].item == item)
                        space += item.maxStack - items[i].count;
                    else if (items[i].item == null)
                        space += item.maxStack;

                    if (space >= quantity)
                        return true;
                }
            }
            else
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].item == null)
                        space++;
                    if (space >= quantity)
                        return true;
                }
            }

            return space >= quantity;
        }

        /// <summary>
        /// Removes up to 'count' of the specified item from the inventory.
        /// Returns true if the requested amount was removed.
        /// </summary>
        public bool RemoveItem(ItemData item, int count)
        {
            if (item == null || count <= 0)
                return false;

            for (int i = 0; i < items.Length && count > 0; i++)
            {
                if (items[i].item == item)
                {
                    int remove = Mathf.Min(count, items[i].count);
                    items[i].count -= remove;
                    count -= remove;
                    if (items[i].count <= 0)
                        items[i].item = null;
                }
            }

            bool success = count <= 0;
            if (success)
                OnInventoryChanged?.Invoke();
            return success;
        }

        /// <summary>
        /// Replaces the item in the specified slot with another item and quantity.
        /// The slot must currently contain the expected item. Returns true on success.
        /// </summary>
        public bool ReplaceItem(int slotIndex, ItemData expectedItem, ItemData newItem, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= items.Length)
                return false;
            if (items[slotIndex].item != expectedItem)
                return false;
            if (newItem == null || quantity <= 0)
                return false;

            if (newItem.stackable)
            {
                if (quantity > newItem.maxStack)
                    return false;
            }
            else if (quantity != 1)
            {
                return false;
            }

            items[slotIndex].item = newItem;
            items[slotIndex].count = newItem.stackable ? quantity : 1;
            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Checks whether the inventory contains an item by ID.
        /// </summary>
        public bool HasItem(string id)
        {
            foreach (var entry in items)
            {
                if (entry.item != null && entry.item.id == id)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the first occurrence of an item with the given ID.
        /// Returns true if an item was removed.
        /// </summary>
        public bool RemoveItem(string id)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].item != null && items[i].item.id == id)
                {
                    items[i].count--;
                    if (items[i].count <= 0)
                        items[i].item = null;
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Attempts to sell a quantity of the item at the given slot index to
        /// the current shop.
        /// </summary>
        public void SellItem(int slotIndex, int quantity = 1)
        {
            if (currentShop == null)
                return;
            if (slotIndex < 0 || slotIndex >= items.Length)
                return;
            int sold = 0;
            for (int i = 0; i < quantity; i++)
            {
                var item = items[slotIndex].item;
                if (item == null)
                    break;

                if (currentShop.Sell(item, this, slotIndex))
                    sold++;
                else
                    break;
            }

            if (sold > 0)
            {
                OnInventoryChanged?.Invoke();
            }
        }

        public void DropItem(int slotIndex, int quantity = 1)
        {
            if (!CanDropItems) return;
            if (slotIndex < 0 || slotIndex >= items.Length) return;
            var entry = items[slotIndex];
            if (entry.item == null) return;

            int remove = Mathf.Clamp(quantity, 1, entry.count);
            entry.count -= remove;
            if (entry.count <= 0)
                entry.item = null;
            items[slotIndex] = entry;
            OnInventoryChanged?.Invoke();
        }

        public void SplitStack(int slotIndex, int quantity)
        {
            if (slotIndex < 0 || slotIndex >= items.Length) return;
            var entry = items[slotIndex];
            if (entry.item == null || !entry.item.splittable) return;

            int amount = Mathf.Clamp(quantity, 1, entry.count - 1);
            if (amount <= 0) return;

            int target = -1;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].item == null)
                {
                    target = i;
                    break;
                }
            }

            if (target == -1) return; // no space

            entry.count -= amount;
            items[slotIndex] = entry;
            items[target].item = entry.item;
            items[target].count = amount;
            OnInventoryChanged?.Invoke();
        }

        public void Swap(int a, int b)
        {
            if (a < 0 || a >= items.Length || b < 0 || b >= items.Length)
                return;
            var temp = items[a];
            items[a] = items[b];
            items[b] = temp;
            OnInventoryChanged?.Invoke();
        }

        /// <summary>
        /// Sets the active shop context used for selling and tooltip information.
        /// </summary>
        public void SetShopContext(Shop shop)
        {
            currentShop = shop;
        }

        public Shop CurrentShop => currentShop;

        [Serializable]
        private class InventorySaveData
        {
            public SlotData[] slots;
        }

        [Serializable]
        private class SlotData
        {
            public string id;
            public int count;
        }

        public void Save()
        {
            var data = new InventorySaveData
            {
                slots = new SlotData[items.Length]
            };

            for (int i = 0; i < items.Length; i++)
            {
                var entry = items[i];
                data.slots[i] = new SlotData
                {
                    id = entry.item != null ? entry.item.id : null,
                    count = entry.count
                };
            }

            SaveManager.Save(SaveKey, data);
        }

        public void Load()
        {
            var data = SaveManager.Load<InventorySaveData>(SaveKey);
            if (data == null || data.slots == null)
                return;

            int len = Mathf.Min(items.Length, data.slots.Length);
            for (int i = 0; i < len; i++)
            {
                var slot = data.slots[i];
                if (!string.IsNullOrEmpty(slot.id))
                {
                    var item = Resources.Load<ItemData>("Item/" + slot.id);
                    items[i].item = item;
                    items[i].count = slot.count;
                }
            }
            OnInventoryChanged?.Invoke();
        }
    }
}
