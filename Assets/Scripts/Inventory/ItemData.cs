using System;
using UnityEngine;
using Items;
using Skills;
using Skills.Fishing;
using Status.Poison;

namespace Inventory
{
    [Serializable]
    public struct SkillRequirement
    {
        public SkillType skill;
        public int level;
    }

    /// <summary>
    /// Equipment slots available for equippable items. Items that are not
    /// equippable should use <see cref="EquipmentSlot.None"/>.
    /// </summary>
    public enum EquipmentSlot
    {
        None,
        Head,
        Amulet,
        Cape,
        Arrow,
        Body,
        Legs,
        Boots,
        Gloves,
        Ring,
        Weapon,
        Shield,
        Charm
    }

    /// <summary>
    /// Simple data container describing an item that can be stored in the
    /// player's inventory.  It is implemented as a ScriptableObject so items can
    /// be created easily via the Unity editor and referenced by pickup objects.
    /// </summary>
    [CreateAssetMenu(menuName = "Inventory/ItemData")]
    public class ItemData : ScriptableObject
    {
        [Header("Identification")] public string id;

        [Header("Display")] public string itemName;
        public Sprite icon;

        [Header("Description")] [TextArea] public string description;

        [Header("Stacking")] [Tooltip("If true, multiple items can occupy a single inventory slot.")]
        public bool stackable;

        /// <summary>
        /// Maximum stack size for stackable items. Old School RuneScape uses a
        /// 32-bit signed integer limit (2,147,483,647) for all stackable items,
        /// so we mirror that behaviour here.
        /// </summary>
        public const int OSRS_MAX_STACK = int.MaxValue;

        // Serialized only so existing assets don't lose data. At runtime the
        // <see cref="MaxStack"/> property should be used instead which returns
        // the OSRS limit for stackable items or 1 for non-stackables.
        [HideInInspector] public int maxStack = OSRS_MAX_STACK;

        /// <summary>
        /// Gets the maximum number of items that can occupy a single inventory
        /// slot. Stackable items use the OSRS limit; non-stackables always return
        /// 1.
        /// </summary>
        public int MaxStack => stackable ? OSRS_MAX_STACK : 1;

        [Tooltip("If true, stacks of this item can be split in the inventory.")]
        public bool splittable = true;

        [Header("Restrictions")]
        [Tooltip("If true, this item cannot be dropped.")]
        public bool isUndroppable = false;

        [Header("Consumable")]
        [Tooltip("Hitpoints restored when this item is consumed.")]
        public int healAmount = 0;
        [Tooltip("Item id to replace this item with after consuming. Leave empty for single-use items.")]
        public string replacementItemId;

        private void OnValidate()
        {
            // Keep the serialized value in sync for inspector visibility.
            maxStack = stackable ? OSRS_MAX_STACK : 1;
        }

        [Header("Equipment")] [Tooltip("Slot this item can be equipped to. Use None for non-equippable items.")]
        public EquipmentSlot equipmentSlot = EquipmentSlot.None;

        [Header("Fishing Bonuses")]
        [Tooltip("Extra bycatch roll chance (0.01 = 1%).")]
        public float bycatchChanceBonus = 0f;

        [Tooltip("Additional fishing XP multiplier (0.025 = +2.5% XP).")]
        public float fishingXpBonusMultiplier = 0f;

        [Tooltip("Water types where the fishing XP bonus applies.")]
        public WaterType fishingXpBonusWaterTypes = WaterType.Any;

        [Header("Woodcutting Bonuses")]
        [Tooltip("Additional woodcutting XP multiplier (0.025 = +2.5% XP).")]
        public float woodcuttingXpBonusMultiplier = 0f;

        [Header("Mining Bonuses")]
        [Tooltip("Additional mining XP multiplier (0.025 = +2.5% XP).")]
        public float miningXpBonusMultiplier = 0f;

        [Header("Cooking Bonuses")]
        [Tooltip("Additional cooking XP multiplier (0.025 = +2.5% XP).")]
        public float cookingXpBonusMultiplier = 0f;

        [Header("Requirements")]
        public SkillRequirement[] skillRequirements;

        [Header("Combat")]
        public ItemCombatStats combat = ItemCombatStats.Default;

        [Header("Poison")]
        public PoisonConfig onHitPoison;

        [Range(0f, 1f)]
        [Tooltip("Chance that the poison is applied on a successful hit.")]
        public float poisonApplyChance = 0.25f;

        [Tooltip("Only apply poison if the hit dealt damage.")]
        public bool poisonRequiresDamage = true;

        [Header("Halberd AOE")]
        public bool isHalberd = false;
        public float aoeRadiusTiles = 0f;
        public float coneAngleDeg = 0f;
        public int aoeMaxTargets = 0;
        public float aoeMultiplier = 0f;
    }
}
