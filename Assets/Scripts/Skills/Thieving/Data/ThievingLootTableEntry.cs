using System;
using UnityEngine;

namespace Skills.Thieving.Data
{
    /// <summary>
    ///     Defines an entry within a thieving loot table. Mirrors the OSRS pickpocket and stall
    ///     loot behaviour where each entry can contribute weighted chances and optional guaranteed drops.
    /// </summary>
    [Serializable]
    public struct ThievingLootTableEntry
    {
        [Tooltip("Identifier of the item awarded when this entry is selected.")]
        public string itemId;

        [Tooltip("Inclusive quantity range rolled when the entry is awarded (min, max).")]
        public Vector2Int quantityRange;

        [Tooltip("Relative weight used when rolling this entry. Set to 0 for purely guaranteed rewards.")]
        public float weight;

        [Tooltip("When true the entry is always granted before any weighted rolls.")]
        public bool guaranteed;

        [Tooltip("Optional noted quantity override. Set to 0 to reuse the rolled quantity.")]
        public int notedQuantity;
    }
}
