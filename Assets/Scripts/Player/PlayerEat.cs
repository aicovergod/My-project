using UnityEngine;
using Inventory;

namespace Player
{
    /// <summary>
    /// Handles consuming food items to restore player hitpoints.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerEat : MonoBehaviour
    {
        private PlayerHitpoints hitpoints;

        private void Awake()
        {
            hitpoints = GetComponent<PlayerHitpoints>();
        }

        /// <summary>
        /// Consume the given food item and heal the player.
        /// </summary>
        /// <param name="item">Item data describing the food.</param>
        /// <returns>True if the item was consumed.</returns>
        public bool Eat(ItemData item)
        {
            if (item == null || hitpoints == null || item.healAmount <= 0)
                return false;

            hitpoints.Heal(item.healAmount);
            return true;
        }
    }
}
