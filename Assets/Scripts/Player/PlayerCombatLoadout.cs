using UnityEngine;
using Combat;
using EquipmentSystem;
using Skills;
using Core.Save;

namespace Player
{
    /// <summary>
    /// Stores the player's current combat style and exposes aggregated combat stats.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCombatLoadout : MonoBehaviour, ISaveable
    {
        [SerializeField] private CombatStyle style = CombatStyle.Accurate;
        private const string SaveKey = "PlayerCombatStyle";

        private EquipmentAggregator equipment;
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
            equipment = GetComponent<EquipmentAggregator>();
            skills = GetComponent<SkillManager>();
            SaveManager.Register(this);
        }

        private void OnDestroy()
        {
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
            return CombatantStats.ForPlayer(skills, equipment, style, DamageType.Melee);
        }

        public void Save()
        {
            SaveManager.Save(SaveKey, style);
        }

        public void Load()
        {
            style = SaveManager.Load<CombatStyle>(SaveKey);
        }
    }
}
