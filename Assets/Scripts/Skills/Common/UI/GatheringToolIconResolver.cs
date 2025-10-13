using System.Collections.Generic;
using Inventory;
using UnityEngine;

namespace Skills.Common.UI
{
    /// <summary>
    /// Provides cached lookups for gathering tool item icons so HUDs can reuse the same sprite instances.
    /// </summary>
    public static class GatheringToolIconResolver
    {
        /// <summary>
        /// Cache of previously loaded item data keyed by the item identifier.
        /// </summary>
        private static readonly Dictionary<string, ItemData> cachedItems = new Dictionary<string, ItemData>();

        /// <summary>
        /// Resolves the sprite icon for the supplied item identifier.
        /// Returns <c>null</c> when the identifier is missing or the item cannot be located.
        /// </summary>
        /// <param name="itemId">Identifier for the tool to resolve.</param>
        public static Sprite GetIcon(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            if (!cachedItems.TryGetValue(itemId, out var itemData))
            {
                itemData = Resources.Load<ItemData>("Item/" + itemId);
                cachedItems[itemId] = itemData;
            }

            return itemData != null ? itemData.icon : null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Clears the cached item lookups. Useful for editor tooling or hot-reload scenarios where assets change at runtime.
        /// </summary>
        public static void ClearCache()
        {
            cachedItems.Clear();
        }
#endif
    }
}
