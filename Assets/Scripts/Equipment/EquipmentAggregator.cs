using System;
using UnityEngine;
using Inventory;
using Items;

namespace EquipmentSystem
{
    /// <summary>
    /// Aggregates combat related stats from all equipped items.
    /// </summary>
    [DisallowMultipleComponent]
    public class EquipmentAggregator : MonoBehaviour
    {
        /// <summary>
        /// Combined combat stats from equipped gear.
        /// </summary>
        public struct CombinedStats
        {
            public int attack;
            public int strength;
            public int range;
            public int magic;
            public int meleeDef;
            public int rangeDef;
            public int magicDef;
            public int attackSpeedTicks;
        }

        private Equipment equipment;

        private void Awake()
        {
            equipment = GetComponent<Equipment>();
        }

        /// <summary>
        /// Sum all equipped item bonuses into a single structure.
        /// </summary>
        public CombinedStats GetCombinedStats()
        {
            CombinedStats result = new CombinedStats { attackSpeedTicks = 4 };
            if (equipment == null)
                return result;

            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (slot == EquipmentSlot.None)
                    continue;
                var entry = equipment.GetEquipped(slot);
                var item = entry.item;
                if (item == null)
                    continue;
                var stats = item.combat;
                result.attack += stats.Attack;
                result.strength += stats.Strength;
                result.range += stats.Range;
                result.magic += stats.Magic;
                result.meleeDef += stats.MeleeDefence;
                result.rangeDef += stats.RangeDefence;
                result.magicDef += stats.MagicDefence;
                if (slot == EquipmentSlot.Weapon && stats.AttackSpeedTicks > 0)
                    result.attackSpeedTicks = stats.AttackSpeedTicks;
            }

            if (result.attackSpeedTicks <= 0)
                result.attackSpeedTicks = 4;

            return result;
        }
    }
}
