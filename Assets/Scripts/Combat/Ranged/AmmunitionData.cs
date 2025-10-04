using UnityEngine;
using Inventory;

namespace Combat.Ranged
{
    /// <summary>
    /// Scriptable definition describing ammunition that can be consumed by ranged weapons.
    /// </summary>
    [CreateAssetMenu(menuName = "Combat/Ranged/Ammunition", fileName = "AmmunitionData")]
    public class AmmunitionData : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Optional explicit item id override. When empty the linked ItemData id is used.")]
        [SerializeField] private string ammoIdOverride;
        [Tooltip("Item definition backing this ammunition entry.")]
        [SerializeField] private ItemData ammoItem;
        [Tooltip("Group that determines which weapons can consume this ammo.")]
        public AmmunitionCategory category = AmmunitionCategory.Arrow;
        [Tooltip("Set to true for ammo that should not be consumed (training bows, etc.).")]
        public bool infinite;

        [Header("Formula Modifiers")]
        [Tooltip("Multiplier applied to the attack roll when this ammo is used.")]
        [Min(0f)] public float accuracyMultiplier = 1f;
        [Tooltip("Multiplier applied to the damage roll when this ammo is used.")]
        [Min(0f)] public float damageMultiplier = 1f;
        [Tooltip("Extra tiles added to the weapon's range when this ammo is loaded.")]
        public float rangeBonusTiles;

        [Header("Effects")]
        [Tooltip("Optional special effect triggered when the projectile lands.")]
        public RangedSpecialEffect specialEffect;
        [Tooltip("When true, the ammo carries a poison payload and should hook into poison systems on hit.")]
        public bool appliesPoison;
        [Tooltip("Chance for poison to trigger when appliesPoison is true.")]
        [Range(0f, 1f)] public float poisonApplyChance = 0.25f;

        [Header("Recovery")]
        [Tooltip("Chance to recover ammo after a successful hit. Overrides weapon settings when > 0.")]
        [Range(0f, 1f)] public float recoveryChanceOnHit;
        [Tooltip("Chance to recover ammo after a miss. Overrides weapon settings when > 0.")]
        [Range(0f, 1f)] public float recoveryChanceOnMiss;
        [Tooltip("If true, recovered ammo spawns in the world when inventory space is unavailable.")]
        public bool spawnRecoveryAsGroundItem = true;

        /// <summary>
        /// Resolved ammunition id using overrides when provided.
        /// </summary>
        public string AmmoId => !string.IsNullOrWhiteSpace(ammoIdOverride)
            ? ammoIdOverride
            : ammoItem != null ? ammoItem.id : name;

        /// <summary>
        /// Direct reference to the item asset backing this ammo entry. Null when the id is resolved purely via override.
        /// </summary>
        public ItemData AmmoItem => ammoItem;

        private void OnValidate()
        {
            accuracyMultiplier = Mathf.Max(0f, accuracyMultiplier);
            damageMultiplier = Mathf.Max(0f, damageMultiplier);
            if (ammoItem != null && string.IsNullOrWhiteSpace(ammoIdOverride))
                ammoIdOverride = ammoItem.id;
        }
    }
}
