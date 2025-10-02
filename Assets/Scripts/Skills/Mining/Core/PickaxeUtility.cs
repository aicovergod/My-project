using System;
using System.Collections.Generic;
using Inventory;
using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    ///     Shared helper that resolves whether an <see cref="ItemData"/> represents a pickaxe.
    ///     The lookup prefers dedicated pickaxe definitions when available while falling back
    ///     to resource heuristics so runtime checks remain resilient.
    /// </summary>
    public static class PickaxeUtility
    {
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;
        private static HashSet<string> cachedPickaxeIds;
        private static bool cacheInitialised;

        /// <summary>
        ///     Determines whether the supplied item is classified as a pickaxe.
        /// </summary>
        /// <param name="item">Item data to evaluate.</param>
        /// <returns><c>true</c> when the item is recognised as a pickaxe.</returns>
        public static bool IsPickaxe(ItemData item)
        {
            if (item == null)
                return false;

            EnsureCache();
            return cachedPickaxeIds != null && cachedPickaxeIds.Contains(item.id);
        }

        /// <summary>
        ///     Determines whether the supplied item identifier is classified as a pickaxe.
        /// </summary>
        /// <param name="itemId">Identifier to evaluate.</param>
        /// <returns><c>true</c> when the identifier maps to a known pickaxe.</returns>
        public static bool IsPickaxeId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            EnsureCache();
            return cachedPickaxeIds != null && cachedPickaxeIds.Contains(itemId.Trim());
        }

        /// <summary>
        ///     Clears the cached pickaxe identifiers. Primarily exposed for editor/testing usage.
        /// </summary>
        public static void ClearCache()
        {
            cachedPickaxeIds?.Clear();
            cacheInitialised = false;
        }

        private static void EnsureCache()
        {
            if (cacheInitialised)
                return;

            cacheInitialised = true;
            cachedPickaxeIds = new HashSet<string>(IdComparer);

            // Attempt to use explicit pickaxe definitions first so designers can add new picks
            // without touching code. When no definitions are present we gracefully fall back to
            // name-based heuristics using the item database under Resources/Item.
            try
            {
                var definitions = Resources.LoadAll<PickaxeDefinition>(string.Empty);
                for (int i = 0; i < definitions.Length; i++)
                {
                    var definition = definitions[i];
                    if (definition == null)
                        continue;

                    string id = definition.Id;
                    if (!string.IsNullOrWhiteSpace(id))
                        cachedPickaxeIds.Add(id.Trim());
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PickaxeUtility] Failed to load pickaxe definitions: {ex.Message}");
            }

            if (cachedPickaxeIds.Count > 0)
                return;

            try
            {
                var items = Resources.LoadAll<ItemData>("Item");
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item == null)
                        continue;

                    if (ContainsPickaxeKeyword(item.id) || ContainsPickaxeKeyword(item.itemName))
                        cachedPickaxeIds.Add(item.id);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PickaxeUtility] Failed to resolve pickaxe items from Resources: {ex.Message}");
            }
        }

        private static bool ContainsPickaxeKeyword(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && value.IndexOf("pickaxe", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
