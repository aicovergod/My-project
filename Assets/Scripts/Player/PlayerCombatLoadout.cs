using System;
using System.Collections.Generic;
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

        private static readonly CombatStyle[] MeleeStyles =
        {
            CombatStyle.Accurate,
            CombatStyle.Aggressive,
            CombatStyle.Defensive,
            CombatStyle.Controlled
        };

        private static readonly CombatStyle[] RangedStyles =
        {
            CombatStyle.Accurate,
            CombatStyle.Rapid,
            CombatStyle.Longrange
        };

        private static readonly CombatStyle[] MagicStyles = { CombatStyle.Accurate };

        /// <summary>Raised whenever the combat style changes.</summary>
        public event Action<CombatStyle> StyleChanged;

        /// <summary>Raised whenever the active damage type changes.</summary>
        public event Action<DamageType> DamageTypeChanged;

        private EquipmentAggregator equipmentAggregator;
        private Equipment equipment;
        private SkillManager skills;

        /// <summary>Current selected combat style.</summary>
        public CombatStyle Style
        {
            get => style;
            set
            {
                var available = GetAvailableStylesInternal();
                if (Array.IndexOf(available, value) < 0 && available.Length > 0)
                    value = available[0];
                SetStyleInternal(value, true);
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
            if (UnityEngine.Input.GetKeyDown(KeyCode.F))
                CycleStyle();
        }

        /// <summary>Cycle combat style in the order defined for the current weapon.</summary>
        public void CycleStyle()
        {
            var styles = GetAvailableStylesInternal();
            if (styles.Length == 0)
                return;
            int index = Array.IndexOf(styles, style);
            if (index < 0)
                index = 0;
            else
                index = (index + 1) % styles.Length;
            Style = styles[index];
        }

        /// <summary>Get a snapshot of combat stats for the player.</summary>
        public CombatantStats GetCombatantStats()
        {
            return CombatantStats.ForPlayer(skills, equipmentAggregator, style, damageType);
        }

        /// <summary>Set the current damage type.</summary>
        public void SetDamageType(DamageType type)
        {
            UpdateDamageType(type);
        }

        public void Save()
        {
            SaveManager.Save(SaveKey, style);
        }

        public void Load()
        {
            style = SaveManager.Load<CombatStyle>(SaveKey);
            EnsureStyleValidForCurrentWeapon(false);
        }

        /// <summary>Return the combat styles available for the current damage type.</summary>
        public IReadOnlyList<CombatStyle> GetAvailableStyles()
        {
            return GetAvailableStylesInternal();
        }

        /// <summary>Check whether the provided style is valid for the current weapon category.</summary>
        public bool IsStyleAvailable(CombatStyle candidate)
        {
            return Array.IndexOf(GetAvailableStylesInternal(), candidate) >= 0;
        }

        private CombatStyle[] GetAvailableStylesInternal()
        {
            return damageType switch
            {
                DamageType.Ranged => RangedStyles,
                DamageType.Magic => MagicStyles,
                _ => MeleeStyles
            };
        }

        private void SetStyleInternal(CombatStyle newStyle, bool shouldSave)
        {
            if (style == newStyle)
                return;
            style = newStyle;
            if (shouldSave)
                Save();
            StyleChanged?.Invoke(style);
        }

        private void EnsureStyleValidForCurrentWeapon(bool persist = true)
        {
            var styles = GetAvailableStylesInternal();
            if (styles.Length == 0)
                return;
            if (Array.IndexOf(styles, style) >= 0)
                return;
            SetStyleInternal(styles[0], persist);
        }

        private void UpdateDamageType(DamageType newType)
        {
            if (damageType == newType)
                return;
            damageType = newType;
            EnsureStyleValidForCurrentWeapon();
            DamageTypeChanged?.Invoke(damageType);
        }

        private void HandleEquipmentChanged(EquipmentSlot slot)
        {
            if (slot != EquipmentSlot.Weapon)
                return;

            var entry = equipment != null ? equipment.GetEquipped(EquipmentSlot.Weapon) : default;
            var weapon = entry.item;

            var poisonApplier = GetComponent<OnHitPoisonApplier>();
            if (weapon != null && weapon.onHitPoison != null)
            {
                if (poisonApplier == null)
                    poisonApplier = gameObject.AddComponent<OnHitPoisonApplier>();
                poisonApplier.poison = weapon.onHitPoison;
                poisonApplier.applyChance = weapon.poisonApplyChance;
                poisonApplier.requiresDamage = weapon.poisonRequiresDamage;
            }
            else if (poisonApplier != null)
            {
                Destroy(poisonApplier);
            }

            DamageType resolvedType = WeaponClassificationUtility.ResolveDamageType(weapon);
            bool spellSelected = MagicUI.ActiveSpell != null;
            DamageType newType = spellSelected ? DamageType.Magic : resolvedType;

            if (!spellSelected)
            {
                if (newType == DamageType.Magic)
                {
                    if (MagicUI.ActiveSpell == null)
                        MagicUI.RestoreLastSpell();
                }
                else
                {
                    MagicUI.ClearActiveSpell();
                }
            }

            UpdateDamageType(newType);
        }
    }
}
