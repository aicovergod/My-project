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
        public int amount;
        public SpriteRenderer iconRenderer;

        private void Reset()
        {
            // Ensure collider is configured as a trigger for easy pickup.
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
            Initialize(item, amount);
        }

        private void Start()
        {
            Initialize(item, amount);
        }

        private void OnValidate()
        {
            Initialize(item, amount);
        }

        public void Initialize(ItemData item, int amount)
        {
            this.item = item;
            this.amount = amount;
            iconRenderer ??= GetComponentInChildren<SpriteRenderer>();
            if (iconRenderer != null && item != null)
                iconRenderer.sprite = item.icon;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag("Player"))
                return;

            Inventory inv = other.GetComponent<Inventory>();
            if (inv != null && inv.AddItem(item, amount))
            {
                Destroy(gameObject);
            }
        }
    }
}