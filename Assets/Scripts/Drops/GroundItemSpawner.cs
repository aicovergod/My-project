using UnityEngine;
using Inventory;
using System.Reflection;

namespace MyGame.Drops
{
    /// <summary>
    /// Adapter for spawning ground item pickups.
    /// </summary>
    public class GroundItemSpawner : MonoBehaviour
    {
        /// <summary>Prefab used when the project lacks an inventory spawner.</summary>
        public ItemPickup pickupPrefab;

        /// <summary>When true, attempts to route spawning to Inventory.ItemPickup.Spawn.</summary>
        public bool useInventorySpawner = true;

        /// <summary>
        /// Spawns an item pickup in the world.
        /// </summary>
        /// <param name="def">Item definition.</param>
        /// <param name="amount">Quantity.</param>
        /// <param name="pos">World position.</param>
        public void Spawn(ItemData def, int amount, Vector3 pos)
        {
            if (def == null || amount <= 0)
            {
                return;
            }

            Vector3 spawnPos = pos + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.1f);

            if (useInventorySpawner)
            {
                MethodInfo m = typeof(ItemPickup).GetMethod("Spawn", BindingFlags.Public | BindingFlags.Static);
                if (m != null)
                {
                    m.Invoke(null, new object[] { def, amount, spawnPos });
                    return;
                }
            }

            if (pickupPrefab == null)
            {
                Debug.LogError("GroundItemSpawner: No pickup prefab assigned.");
                return;
            }

            ItemPickup pickup = Instantiate(pickupPrefab, spawnPos, Quaternion.identity);
            MethodInfo init = pickup.GetType().GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
            if (init != null)
            {
                init.Invoke(pickup, new object[] { def, amount });
            }
            else
            {
                FieldInfo itemField = pickup.GetType().GetField("item") ?? pickup.GetType().GetField("itemDefinition");
                FieldInfo amtField = pickup.GetType().GetField("amount") ?? pickup.GetType().GetField("quantity");
                if (itemField != null)
                {
                    itemField.SetValue(pickup, def);
                }
                if (amtField != null)
                {
                    amtField.SetValue(pickup, amount);
                }
            }
        }
    }
}
