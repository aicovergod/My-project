using System;
using UnityEngine;
using Items;
using Skills;

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
        Shield
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

        [Header("Requirements")]
        public SkillRequirement[] skillRequirements;

        [Header("Combat")]
        public ItemCombatStats combat = ItemCombatStats.Default;

        [Obsolete("Use combat.Attack instead", false)] public int attackBonus;
        [Obsolete("Use per-type defence bonuses", false)] public int defenceBonus;
        [Obsolete("Use combat.AttackSpeedTicks", false)] public int attackSpeed = 4;

        [Header("Legacy Bonuses")]
        [Obsolete("Use combat.Strength", false)] public int strengthBonus;
        [Obsolete("Use combat.Range", false)] public int rangeBonus;
        [Obsolete("Use combat.Magic", false)] public int magicBonus;
        [Obsolete("Use combat.MeleeDefence", false)] public int meleeDefenceBonus;
        [Obsolete("Use combat.RangeDefence", false)] public int rangedDefenceBonus;
        [Obsolete("Use combat.MagicDefence", false)] public int magicDefenceBonus;
    }
}