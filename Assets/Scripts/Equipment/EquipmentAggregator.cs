using System;
using UnityEngine;
using Inventory;
using Items;
using Beastmaster;

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
            if (equipment != null)
            {
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
            }

            if (PetMergeController.Instance != null && PetMergeController.Instance.IsMerged)
            {
                var pet = PetMergeController.Instance.MergedEquipStats;
                result.attack += pet.attack;
                result.strength += pet.strength;
                result.range += pet.range;
                result.magic += pet.magic;
                result.meleeDef += pet.meleeDef;
                result.rangeDef += pet.rangeDef;
                result.magicDef += pet.magicDef;
                if (pet.attackSpeedTicks > 0)
                    result.attackSpeedTicks = pet.attackSpeedTicks;
            }

            if (result.attackSpeedTicks <= 0)
                result.attackSpeedTicks = 4;

            return result;
        }
    }
}
