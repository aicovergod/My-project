using System.Collections.Generic;
using Inventory;
using UnityEngine;

namespace Skills.Cooking
{
    /// <summary>
    ///     Shared helper that centralises cookable recipe lookups so multiple controllers can
    ///     consistently determine whether the player can cook a raw item from their inventory.
    /// </summary>
    public static class CookingInventoryHelper
    {
        private static readonly Dictionary<string, CookableRecipe> RecipeLookup = new();
        private static bool recipesLoaded;

        /// <summary>
        ///     Result data returned when probing the inventory for a cookable recipe.
        /// </summary>
        public struct CookableInventorySearchResult
        {
            /// <summary>
            ///     Recipe resolved from the inventory search.
            /// </summary>
            public CookableRecipe Recipe;

            /// <summary>
            ///     Raw item instance associated with the recipe.
            /// </summary>
            public ItemData RawItem;

            /// <summary>
            ///     Inventory slot index containing the raw item.
            /// </summary>
            public int SlotIndex;

            /// <summary>
            ///     Total number of raw items currently owned.
            /// </summary>
            public int Quantity;

            /// <summary>
            ///     Indicates whether the player has at least one of the raw item.
            /// </summary>
            public bool HasRequiredQuantity;

            /// <summary>
            ///     Indicates whether the player meets the recipe's Cooking level requirement.
            /// </summary>
            public bool MeetsLevelRequirement;

            /// <summary>
            ///     When <c>true</c>, the preferred inventory slot supplied to the search resolved the recipe.
            /// </summary>
            public bool UsesPreferredSlot;

            /// <summary>
            ///     Failure message explaining why cooking cannot begin.
            /// </summary>
            public string FailureMessage;

            /// <summary>
            ///     <c>true</c> when a recipe and raw item were located.
            /// </summary>
            public bool HasRecipe => Recipe != null && RawItem != null;

            /// <summary>
            ///     <c>true</c> when all cooking requirements are satisfied.
            /// </summary>
            public bool CanCook => HasRecipe && HasRequiredQuantity && MeetsLevelRequirement;
        }

        /// <summary>
        ///     Searches the player's inventory for a cookable recipe, preferring the highlighted slot when possible.
        /// </summary>
        /// <param name="inventory">Inventory to search.</param>
        /// <param name="cookingSkill">Cooking skill used for level checks.</param>
        /// <param name="preferredSlotIndex">Slot index to evaluate before scanning the rest of the inventory.</param>
        /// <returns>Struct describing the located recipe and whether the player can cook it.</returns>
        public static CookableInventorySearchResult FindCookableRecipe(Inventory.Inventory inventory, CookingSkill cookingSkill, int preferredSlotIndex = -1)
        {
            var result = new CookableInventorySearchResult
            {
                Recipe = null,
                RawItem = null,
                SlotIndex = -1,
                Quantity = 0,
                HasRequiredQuantity = false,
                MeetsLevelRequirement = false,
                UsesPreferredSlot = false,
                FailureMessage = string.Empty
            };

            if (inventory == null)
            {
                result.FailureMessage = "You need something raw to cook";
                return result;
            }

            EnsureRecipeLookup();

            if (RecipeLookup.Count == 0)
            {
                result.FailureMessage = "No recipes available";
                return result;
            }

            bool EvaluateSlot(int slotIndex, bool markPreferred)
            {
                if (slotIndex < 0 || slotIndex >= inventory.size)
                    return false;

                var entry = inventory.GetSlot(slotIndex);
                var item = entry.item;
                if (item == null || string.IsNullOrEmpty(item.id))
                    return false;

                if (!RecipeLookup.TryGetValue(item.id, out var recipe))
                    return false;

                result.Recipe = recipe;
                result.RawItem = item;
                result.SlotIndex = slotIndex;
                result.UsesPreferredSlot = markPreferred;
                return true;
            }

            if (preferredSlotIndex >= 0 && preferredSlotIndex < inventory.size)
                EvaluateSlot(preferredSlotIndex, true);

            if (result.Recipe == null)
            {
                for (int i = 0; i < inventory.size; i++)
                {
                    if (i == preferredSlotIndex)
                        continue;

                    if (EvaluateSlot(i, false))
                        break;
                }
            }

            if (!result.HasRecipe)
            {
                result.FailureMessage = "You need something raw to cook";
                return result;
            }

            result.Quantity = inventory.GetItemCount(result.RawItem);
            result.HasRequiredQuantity = result.Quantity > 0;
            if (!result.HasRequiredQuantity)
            {
                result.FailureMessage = "You need something raw to cook";
                return result;
            }

            int playerLevel = cookingSkill != null ? cookingSkill.Level : 1;
            result.MeetsLevelRequirement = playerLevel >= result.Recipe.requiredLevel;
            if (!result.MeetsLevelRequirement)
            {
                result.FailureMessage = $"You need Cooking level {result.Recipe.requiredLevel}";
                return result;
            }

            result.FailureMessage = string.Empty;
            return result;
        }

        /// <summary>
        ///     Loads the cookable recipe database on demand and caches the mapping for quick lookups.
        /// </summary>
        private static void EnsureRecipeLookup()
        {
            if (recipesLoaded)
                return;

            RecipeLookup.Clear();
            var recipes = Resources.LoadAll<CookableRecipe>("CookingDatabase");
            foreach (var recipe in recipes)
            {
                if (recipe == null || string.IsNullOrEmpty(recipe.rawItemId))
                    continue;

                RecipeLookup[recipe.rawItemId] = recipe;
            }

            recipesLoaded = true;
        }
    }
}
