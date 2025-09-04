using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inventory
{
    [CreateAssetMenu(menuName = "Inventory/Item Combination Database")]
    public class ItemCombinationDatabase : ScriptableObject
    {
        [Serializable]
        public struct Recipe
        {
            public ItemData first;
            public ItemData second;
            public ItemData result;
        }

        public Recipe[] recipes;
        private Dictionary<(ItemData, ItemData), ItemData> lookup;

        public ItemData GetResult(ItemData a, ItemData b)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<(ItemData, ItemData), ItemData>();
                if (recipes != null)
                {
                    foreach (var r in recipes)
                    {
                        if (r.first != null && r.second != null && r.result != null)
                        {
                            lookup[(r.first, r.second)] = r.result;
                            lookup[(r.second, r.first)] = r.result;
                        }
                    }
                }
            }

            ItemData result;
            return lookup != null && lookup.TryGetValue((a, b), out result) ? result : null;
        }
    }
}
