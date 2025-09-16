using System.Collections.Generic;
using UnityEngine;
using World;

namespace Inventory
{
    /// <summary>
    /// Loads all <see cref="ItemData"/> assets at startup and provides
    /// fast lookup by item id. The database will initialize itself on first use
    /// so callers do not need to ensure the component exists ahead of time.
    /// </summary>
    public class ItemDatabase : ScenePersistentObject
    {
        private static ItemDatabase instance;
        private readonly Dictionary<string, ItemData> items = new();

        /// <summary>
        /// Ensure the database exists and has loaded its items. This is invoked
        /// automatically by <see cref="GetItem"/> but may be called manually by
        /// systems that need the database before any lookups occur.
        /// </summary>
        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            // Create a new hidden GameObject so the database survives scene loads.
            var go = new GameObject(nameof(ItemDatabase));
            instance = go.AddComponent<ItemDatabase>();
        }

        /// <summary>
        /// Retrieve an item by its unique identifier.
        /// </summary>
        public static ItemData GetItem(string id)
        {
            EnsureInstance();
            instance.items.TryGetValue(id, out var item);
            return item;
        }

        protected override void Awake()
        {
            // Singleton enforcement.
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            base.Awake();

            var loadedItems = Resources.LoadAll<ItemData>("Item");
            foreach (var item in loadedItems)
            {
                if (!string.IsNullOrEmpty(item.id))
                    items[item.id] = item;
            }
        }
    }
}
