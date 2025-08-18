using System;
using UnityEngine;
using Inventory;
using World;

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

        [Tooltip("If true, the shop will buy this item from the player.")]
        public bool playerSell;

        [Tooltip("Currency amount paid to the player when selling this item.")]
        public int playerSellPrice;
    }

    /// <summary>
    /// Basic shop component that can hold up to 30 items and a currency type.
    /// </summary>
    [RequireComponent(typeof(MinimapMarker))]
    public class Shop : MonoBehaviour
    {
        public const int MaxSlots = 30;

        /// <summary>
        /// Fired after a player successfully buys an item from this shop.
        /// Gameplay scripts can subscribe for quests, analytics, etc.
        /// </summary>
        public event Action<ShopItem> OnItemBought;

        /// <summary>
        /// Fired after a player successfully sells an item to this shop.
        /// Gameplay scripts can subscribe for quests, analytics, etc.
        /// </summary>
        public event Action<ShopItem> OnItemSold;

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

        private void Reset()
        {
            var marker = GetComponent<MinimapMarker>();
            if (marker != null)
                marker.type = MinimapMarker.MarkerType.Shop;
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
        /// player inventory.  Returns true if the purchase succeeds and raises
        /// <see cref="OnItemBought"/>.
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
            if (!playerInventory.CanAddItem(entry.item, 1))
                return false;

            if (!playerInventory.RemoveItem(currency, entry.price))
                return false;

            if (!playerInventory.AddItem(entry.item, 1))
            {
                // Should not happen because we checked CanAddItem, but just in case
                // refund the currency.
                playerInventory.AddItem(currency, entry.price);
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

            // Notify listeners that an item was purchased.
            OnItemBought?.Invoke(new ShopItem
            {
                item = entry.item,
                price = entry.price,
                quantity = 1,
                playerSell = entry.playerSell,
                playerSellPrice = entry.playerSellPrice
            });

            return true;
        }

        /// <summary>
        /// Attempts to buy an item from the player and add it to the shop's stock.
        /// The player receives currency equal to the configured sell price. On
        /// success <see cref="OnItemSold"/> is invoked.
        /// When the player's inventory is full, non-stackable items may be sold by
        /// replacing the item's slot with the currency payout.
        /// </summary>
        public bool Sell(ItemData item, Inventory.Inventory playerInventory, int playerSlotIndex = -1)
        {
            if (item == null || playerInventory == null)
                return false;

            // Find the slot matching this item from the initial stock so players
            // can sell even if the item is currently sold out.
            int stockIndex = -1;
            for (int i = 0; i < initialStock.Length; i++)
            {
                if (initialStock[i].item == item)
                {
                    stockIndex = i;
                    break;
                }
            }

            if (stockIndex == -1)
                return false; // shop doesn't buy this item

            var config = initialStock[stockIndex];
            if (!config.playerSell || config.playerSellPrice <= 0)
                return false; // item not sellable to this shop

            if (playerInventory.GetItemCount(item) <= 0)
                return false;

            bool usedReplacement = false;

            // Check if the player can receive the currency normally. If not and the
            // item is non-stackable, attempt to replace the sold item's slot with
            // currency.
            if (!playerInventory.CanAddItem(currency, config.playerSellPrice))
            {
                if (playerSlotIndex >= 0 && !item.stackable && currency != null &&
                    currency.stackable && config.playerSellPrice <= currency.maxStack)
                {
                    if (!playerInventory.ReplaceItem(playerSlotIndex, item, currency, config.playerSellPrice))
                        return false;
                    usedReplacement = true;
                }
                else
                {
                    return false;
                }
            }

            if (!usedReplacement)
            {
                if (!playerInventory.RemoveItem(item, 1))
                    return false;

                playerInventory.AddItem(currency, config.playerSellPrice);
            }

            // Insert or update stock entry
            var entry = stock[stockIndex];
            if (entry.item == null)
            {
                entry = config;
                entry.quantity = 0;
            }
            entry.quantity++;
            stock[stockIndex] = entry;

            if (restockTimers != null && stockIndex < restockTimers.Length)
                restockTimers[stockIndex] = 0f;

            // Notify listeners that an item was sold to the shop.
            OnItemSold?.Invoke(new ShopItem
            {
                item = config.item,
                price = config.playerSellPrice,
                quantity = 1,
                playerSell = config.playerSell,
                playerSellPrice = config.playerSellPrice
            });

            return true;
        }

        /// <summary>
        /// Returns true if the shop will buy the specified item and outputs the sell price.
        /// </summary>
        public bool TryGetSellPrice(ItemData item, out int sellPrice)
        {
            sellPrice = 0;
            if (item == null)
                return false;
            for (int i = 0; i < initialStock.Length; i++)
            {
                var config = initialStock[i];
                if (config.item == item && config.playerSell && config.playerSellPrice > 0)
                {
                    sellPrice = config.playerSellPrice;
                    return true;
                }
            }
            return false;
        }
    }
}
