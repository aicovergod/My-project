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

        [SerializeField]
        [Tooltip("World-space size of a single tile. Positions are snapped to the tile centre before spawning.")]
        private float tileSize = 1f;

        /// <summary>
        /// Snaps an arbitrary world position to the centre of its tile while preserving the original Z axis.
        /// </summary>
        /// <param name="worldPosition">Incoming world position.</param>
        /// <returns>World position aligned to the tile centre.</returns>
        public Vector3 SnapPositionToTileCenter(Vector3 worldPosition)
        {
            float size = tileSize;
            if (size <= 0f)
            {
                size = 1f;
            }

            float halfSize = size * 0.5f;
            float snappedX = Mathf.Floor(worldPosition.x / size) * size + halfSize;
            float snappedY = Mathf.Floor(worldPosition.y / size) * size + halfSize;

            return new Vector3(snappedX, snappedY, worldPosition.z);
        }

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

            Vector3 spawnPos = SnapPositionToTileCenter(pos);

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

            // Shrink the pickup's sprite to one-third of its original size.
            var renderer = pickup.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.transform.localScale *= 1f / 3f;
            }
        }
    }
}
