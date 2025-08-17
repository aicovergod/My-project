using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// Loads all <see cref="ItemData"/> assets at startup and provides
    /// fast lookup by item id.
    /// </summary>
    public class ItemDatabase : MonoBehaviour
    {
        private static ItemDatabase instance;
        private readonly Dictionary<string, ItemData> items = new();

        /// <summary>
        /// Retrieve an item by its unique identifier.
        /// </summary>
        public static ItemData GetItem(string id)
        {
            if (instance == null)
            {
                Debug.LogError("ItemDatabase has not been initialized.");
                return null;
            }

            instance.items.TryGetValue(id, out var item);
            return item;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            var loadedItems = Resources.LoadAll<ItemData>("Item");
            foreach (var item in loadedItems)
            {
                if (!string.IsNullOrEmpty(item.id))
                    items[item.id] = item;
            }
        }
    }
}
