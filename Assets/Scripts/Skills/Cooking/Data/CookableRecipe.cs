using UnityEngine;

namespace Skills.Cooking
{
    /// <summary>
    /// Defines a recipe for cooking. Maps a raw item to its cooked result and
    /// contains information about level requirements and burn chance.
    /// </summary>
    [CreateAssetMenu(menuName = "Skills/Cooking/Cookable Recipe")]
    public class CookableRecipe : ScriptableObject
    {
        [Tooltip("Item id for the raw food.")]
        public string rawItemId;

        [Tooltip("Item id for the cooked result.")]
        public string cookedItemId;

        [Tooltip("Cooking level required to attempt this recipe.")]
        public int requiredLevel = 1;

        [Tooltip("XP gained on a successful cook.")]
        public int xp = 0;

        [Range(0f, 1f)]
        [Tooltip("Chance to burn the food at the required level.")]
        public float burnChance = 0.0f;

        [Tooltip("Level at which the item can no longer be burned.")]
        public int noBurnLevel = 99;
    }
}
