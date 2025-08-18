using UnityEngine;

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
        public int attackBonus;
        public int defenceBonus;
        [Tooltip("Number of 0.6s ticks between attacks when equipped as a weapon.")]
        public int attackSpeed = 4;

        [Header("Bonuses")] public int strengthBonus;
        public int rangeBonus;
        public int magicBonus;
        public int meleeDefenceBonus;
        public int rangedDefenceBonus;
        public int magicDefenceBonus;
    }
}