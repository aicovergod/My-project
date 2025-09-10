using UnityEngine;
using Combat;
using EquipmentSystem;
using Skills;
using Core.Save;
using UI;
using Inventory;

namespace Player
{
    /// <summary>
    /// Stores the player's current combat style and exposes aggregated combat stats.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCombatLoadout : MonoBehaviour, ISaveable
    {
        [SerializeField] private CombatStyle style = CombatStyle.Accurate;
        [SerializeField] private DamageType damageType = DamageType.Melee;
        private const string SaveKey = "PlayerCombatStyle";

        private EquipmentAggregator equipmentAggregator;
        private Equipment equipment;
        private SkillManager skills;

        /// <summary>Current selected combat style.</summary>
        public CombatStyle Style
        {
            get => style;
            set
            {
                if (style == value)
                    return;
                style = value;
                Save();
            }
        }

        private void Awake()
        {
            equipmentAggregator = GetComponent<EquipmentAggregator>();
            equipment = GetComponent<Equipment>();
            skills = GetComponent<SkillManager>();
            SaveManager.Register(this);
            if (equipment != null)
            {
                equipment.OnEquipmentChanged += HandleEquipmentChanged;
                HandleEquipmentChanged(EquipmentSlot.Weapon);
            }
        }

        private void OnDestroy()
        {
            if (equipment != null)
                equipment.OnEquipmentChanged -= HandleEquipmentChanged;
            SaveManager.Unregister(this);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F))
                CycleStyle();
        }

        /// <summary>Cycle combat style in order Accurate → Aggressive → Defensive → Controlled.</summary>
        public void CycleStyle()
        {
            Style = (CombatStyle)(((int)style + 1) % 4);
        }

        /// <summary>Get a snapshot of combat stats for the player.</summary>
        public CombatantStats GetCombatantStats()
        {
            return CombatantStats.ForPlayer(skills, equipmentAggregator, style, damageType);
        }

        /// <summary>Set the current damage type.</summary>
        public void SetDamageType(DamageType type)
        {
            damageType = type;
        }

        public void Save()
        {
            SaveManager.Save(SaveKey, style);
        }

        public void Load()
        {
            style = SaveManager.Load<CombatStyle>(SaveKey);
        }

        private void HandleEquipmentChanged(EquipmentSlot slot)
        {
            if (slot != EquipmentSlot.Weapon)
                return;

            var entry = equipment != null ? equipment.GetEquipped(EquipmentSlot.Weapon) : default;
            var weapon = entry.item;
            if (weapon != null)
            {
                if (weapon.combat.Magic > 0)
                    damageType = DamageType.Magic;
                else if (weapon.combat.Range > 0)
                    damageType = DamageType.Ranged;
                else
                {
                    damageType = DamageType.Melee;
                    MagicUI.ClearActiveSpell();
                }
            }
            else
            {
                damageType = DamageType.Melee;
                MagicUI.ClearActiveSpell();
            }
        }
    }
}
