using System;
using UnityEngine;
using Items;

namespace Inventory
{
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

        [Tooltip("Maximum number of items per stack when stackable.")]
        public int maxStack = 1;

        [Tooltip("If true, stacks of this item can be split in the inventory.")]
        public bool splittable = true;

        [Header("Equipment")] [Tooltip("Slot this item can be equipped to. Use None for non-equippable items.")]
        public EquipmentSlot equipmentSlot = EquipmentSlot.None;

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