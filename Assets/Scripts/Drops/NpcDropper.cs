using UnityEngine;
using Inventory;
using Pets;

namespace MyGame.Drops
{
    /// <summary>
    /// Component that resolves a drop table and gives items to the player or spawns them.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class NpcDropper : MonoBehaviour
    {
        /// <summary>Drop table to roll.</summary>
        public DropTable dropTable;

        /// <summary>Luck multiplier applied to rolls.</summary>
        public float luckMultiplier = 1f;

        /// <summary>Radius for random spawn offset.</summary>
        public float spawnSpreadRadius = 0.35f;

        /// <summary>Whether to spawn at the NPC's feet.</summary>
        public bool spawnAtFeet = true;

        /// <summary>Spawner responsible for instantiating ground items.</summary>
        public GroundItemSpawner spawner;

        private void Awake()
        {
            if (spawner == null)
            {
                spawner = FindObjectOfType<GroundItemSpawner>();
                if (spawner == null)
                {
                    Debug.LogError("NpcDropper: No GroundItemSpawner found in scene.");
                }
            }
        }

        /// <summary>
        /// Resolves the drop table and spawns items around the NPC.
        /// </summary>
        /// <param name="overridePosition">Optional override for the spawn position.</param>
        public void RollAndSpawn(Vector3? overridePosition = null)
        {
            if (dropTable == null)
            {
                Debug.LogWarning("NpcDropper.RollAndSpawn called without drop table.");
                return;
            }

            Vector3 basePos = overridePosition ?? transform.position;
            if (!spawnAtFeet)
            {
                basePos = transform.position; // placeholder for future expansion
            }

            var drops = DropResolver.Resolve(dropTable, luckMultiplier);
            if (drops.Count == 0)
            {
                var name = !string.IsNullOrEmpty(dropTable.tableName) ? dropTable.tableName : dropTable.name;
                Debug.Log($"NpcDropper: Drop table '{name}' produced no drops.");
            }
            foreach (var drop in drops)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * spawnSpreadRadius;
                Vector3 pos = basePos + (Vector3)offset;

                if (spawner != null)
                {
                    Debug.Log($"NpcDropper: Spawning {drop.quantity}x {drop.item?.name} at {pos}.");
                    spawner.Spawn(drop.item, drop.quantity, pos);
                }
                else
                {
                    Debug.LogWarning($"NpcDropper: No GroundItemSpawner available; adding {drop.quantity}x {drop.item?.name} to inventory.");
                    InventoryBridge.AddItem(drop.item, drop.quantity);
                }
            }
        }

        /// <summary>
        /// Example method to hook into an NPC's death event.
        /// </summary>
        public void OnDeath()
        {
            RollAndSpawn();
        }

#if UNITY_EDITOR
        [ContextMenu("Test Single Roll")]
        private void TestSingleRoll()
        {
            RollAndSpawn();
        }
#endif
    }
}
