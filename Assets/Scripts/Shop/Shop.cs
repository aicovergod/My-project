using UnityEngine;
using Inventory;

namespace ShopSystem
{
    /// <summary>
    /// Describes an item the shop sells and its price.
    /// </summary>
    [System.Serializable]
    public struct ShopItem
    {
        public ItemData item;
        public int price;
    }

    /// <summary>
    /// Basic shop component that can hold up to 30 items and a currency type.
    /// </summary>
    public class Shop : MonoBehaviour
    {
        public const int MaxSlots = 30;

        [Header("Currency")]
        public ItemData currency;

        [Header("Stock")]
        [Tooltip("Items available for purchase (max 30).")]
        public ShopItem[] stock = new ShopItem[MaxSlots];

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
            if (entry.item == null)
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

            return true;
        }
    }
}
