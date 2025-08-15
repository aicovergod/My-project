using UnityEngine;
using Inventory;

namespace ShopSystem
{
    /// <summary>
    /// Describes an item the shop sells, its price and remaining quantity.
    /// </summary>
    [System.Serializable]
    public struct ShopItem
    {
        public ItemData item;
        public int price;
        public int quantity;
    }

    /// <summary>
    /// Basic shop component that can hold up to 30 items and a currency type.
    /// </summary>
    public class Shop : MonoBehaviour
    {
        public const int MaxSlots = 30;

        [Header("Info")]
        public string shopName;

        [Header("Currency")]
        public ItemData currency;

        [Header("Stock")]
        [Tooltip("Items available for purchase (max 30).")]
        public ShopItem[] stock = new ShopItem[MaxSlots];

        [Header("Restock")]
        [Tooltip("If true, depleted items will return after a delay.")]
        public bool restock;
        [Tooltip("Seconds before a sold-out item is restocked.")]
        public float restockDelay = 30f;

        private ShopItem[] initialStock;
        private float[] restockTimers;

        private void Awake()
        {
            initialStock = new ShopItem[stock.Length];
            restockTimers = new float[stock.Length];
            for (int i = 0; i < stock.Length; i++)
                initialStock[i] = stock[i];
        }

        private void Update()
        {
            if (!restock) return;
            for (int i = 0; i < restockTimers.Length; i++)
            {
                if (restockTimers[i] > 0f)
                {
                    restockTimers[i] -= Time.deltaTime;
                    if (restockTimers[i] <= 0f)
                    {
                        stock[i] = initialStock[i];
                        restockTimers[i] = 0f;
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to buy the item at the given slot index using the provided
        /// player inventory.  Returns true if the purchase succeeds.
        /// </summary>
        public bool Buy(int slotIndex, Inventory.Inventory playerInventory)
        {
            if (playerInventory == null)
                return false;
            if (slotIndex < 0 || slotIndex >= stock.Length)
                return false;

            ShopItem entry = stock[slotIndex];
            if (entry.item == null || entry.quantity <= 0)
                return false;

            // Ensure player has enough currency and room for the item.
            if (playerInventory.GetItemCount(currency) < entry.price)
                return false;
            if (!playerInventory.CanAddItem(entry.item))
                return false;

            if (!playerInventory.RemoveItem(currency, entry.price))
                return false;

            if (!playerInventory.AddItem(entry.item))
            {
                // Should not happen because we checked CanAddItem, but just in case
                // refund the currency.
                for (int i = 0; i < entry.price; i++)
                    playerInventory.AddItem(currency);
                return false;
            }

            entry.quantity--;
            if (entry.quantity <= 0)
            {
                // Clear slot and optionally start restock timer
                stock[slotIndex] = new ShopItem();
                if (restock)
                    restockTimers[slotIndex] = restockDelay;
            }
            else
            {
                stock[slotIndex] = entry;
            }

            return true;
        }
    }
}
