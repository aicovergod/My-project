using UnityEngine;
using Inventory;

namespace Pets
{
    /// <summary>
    /// Handles picking up the active pet and converting it into an inventory item.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class PetClickable : MonoBehaviour
    {
        private PetDefinition definition;
        private PetStorage storage;
        private float nextPickupTime;

        private const float PickupCooldown = 3f;
        private const float PickupRadius = 1.5f;

        public void Init(PetDefinition def, PetStorage petStorage)
        {
            definition = def;
            storage = petStorage;
        }

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        /// <summary>
        /// Attempts to convert the active pet to an inventory item.
        /// </summary>
        public void Pickup()
        {
            if (Time.time < nextPickupTime)
                return;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                Vector2 playerPos = player.transform.position;
                Vector2 petPos = transform.position;
                if (Vector2.Distance(playerPos, petPos) > PickupRadius)
                    return;
            }

            if (definition != null && definition.pickupItem != null)
            {
                if (!InventoryBridge.AddItem(definition.pickupItem, 1))
                {
                    nextPickupTime = Time.time + PickupCooldown;
                    return;
                }
            }

            storage?.Close();
            PetDropSystem.DespawnActive();
            PetToastUI.Show("You pick up the pet.");
            Destroy(gameObject);
        }
    }
}

