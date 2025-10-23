using UnityEngine;
using Inventory;
using Pets;
using Companions.Conversation;

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

        /// <summary>Whether random spawn spread should be applied on top of the snapped tile centre.</summary>
        [SerializeField]
        private bool enableSpawnSpread = false;

        /// <summary>Radius for optional random spawn offset. Ignored when <see cref="enableSpawnSpread"/> is false.</summary>
        [SerializeField]
        [Min(0f)]
        private float spawnSpreadRadius = 0.35f;

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

            if (spawner != null)
            {
                basePos = spawner.SnapPositionToTileCenter(basePos);
            }

            var drops = DropResolver.Resolve(dropTable, luckMultiplier);
            if (drops.Count == 0)
            {
                var name = !string.IsNullOrEmpty(dropTable.tableName) ? dropTable.tableName : dropTable.name;
                Debug.Log($"NpcDropper: Drop table '{name}' produced no drops.");
            }
            foreach (var drop in drops)
            {
                Vector3 spawnPos = basePos;
                if (enableSpawnSpread && spawnSpreadRadius > 0f)
                {
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * spawnSpreadRadius;
                    spawnPos += (Vector3)offset;

                    if (spawner != null)
                    {
                        spawnPos = spawner.SnapPositionToTileCenter(spawnPos);
                    }
                }

                if (spawner != null)
                {
                    Debug.Log($"NpcDropper: Spawning {drop.quantity}x {drop.item?.name} at {spawnPos}.");
                    spawner.Spawn(drop.item, drop.quantity, spawnPos);
                }
                else
                {
                    Debug.LogWarning($"NpcDropper: No GroundItemSpawner available; adding {drop.quantity}x {drop.item?.name} to inventory.");
                    InventoryBridge.AddItem(drop.item, drop.quantity);
                }

                RegisterLootEvent(drop, spawnPos);
            }
        }

        /// <summary>
        /// Example method to hook into an NPC's death event.
        /// </summary>
        public void OnDeath()
        {
            RollAndSpawn();
        }

        private void RegisterLootEvent(ResolvedDrop drop, Vector3 position)
        {
            if (drop.item == null || drop.quantity <= 0)
                return;

            string itemName = !string.IsNullOrWhiteSpace(drop.item.itemName)
                ? drop.item.itemName
                : drop.item.name;

            if (string.IsNullOrWhiteSpace(itemName))
                itemName = "loot";

            string summary = drop.quantity == 1
                ? $"secured {itemName}"
                : $"secured {drop.quantity} {itemName}";

            string source = dropTable != null && !string.IsNullOrWhiteSpace(dropTable.tableName)
                ? dropTable.tableName
                : name;

            var metadata = CompanionEventMetadata.Create(
                primaryActor: "You",
                secondaryActor: source,
                worldPosition: position);

            CompanionConversationService.RegisterEvent(summary, CompanionEventType.Loot, metadata);
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
