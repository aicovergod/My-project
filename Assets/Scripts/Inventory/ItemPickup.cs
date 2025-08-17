using UnityEngine;

namespace Inventory
{
    /// <summary>
    /// When attached to an item prefab in the world this component allows the
    /// player to pick up the item simply by walking over it.  The item is added to
    /// the player's inventory and the pickup object is removed from the scene.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class ItemPickup : MonoBehaviour
    {
        public ItemData item;

        private void Reset()
        {
            // Ensure collider is configured as a trigger for easy pickup.
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag("Player"))
                return;

            Inventory inv = other.GetComponent<Inventory>();
            if (inv != null && inv.AddItem(item))
            {
                Destroy(gameObject);
            }
        }
    }
}