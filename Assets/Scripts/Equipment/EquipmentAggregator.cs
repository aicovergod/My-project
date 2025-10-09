using System;
using UnityEngine;
using Combat;
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
            public int rangeStrength;
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
        public CombinedStats GetCombinedStats(CombatStyle? styleOverride = null)
        {
            CombinedStats result = new CombinedStats { attackSpeedTicks = 4 };
            CombatStyle resolvedStyle = styleOverride ?? CombatStyle.Accurate;
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
                    result.rangeStrength += stats.RangeStrength;
                    result.magic += stats.Magic;
                    result.meleeDef += stats.MeleeDefence;
                    result.rangeDef += stats.RangeDefence;
                    result.magicDef += stats.MagicDefence;
                    if (slot == EquipmentSlot.Weapon)
                    {
                        int ticks = item.GetAttackSpeedTicks(resolvedStyle);
                        if (ticks > 0)
                            result.attackSpeedTicks = ticks;
                    }
                }
            }

            if (PetMergeController.Instance != null && PetMergeController.Instance.IsMerged)
            {
                var pet = PetMergeController.Instance.MergedEquipStats;
                result.attack += pet.attack;
                result.strength += pet.strength;
                result.range += pet.range;
                result.rangeStrength += pet.rangeStrength;
                result.magic += pet.magic;
                result.meleeDef += pet.meleeDef;
                result.rangeDef += pet.rangeDef;
                result.magicDef += pet.magicDef;
                if (pet.attackSpeedTicks > 0)
                    result.attackSpeedTicks = pet.attackSpeedTicks;
            }

            result.attackSpeedTicks = Mathf.Max(1, result.attackSpeedTicks);

            return result;
        }
    }
}
