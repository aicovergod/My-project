using UnityEngine;
using Combat;
using EquipmentSystem;
using Skills;

namespace Player
{
    /// <summary>
    /// Stores the player's current combat style and exposes aggregated combat stats.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCombatLoadout : MonoBehaviour
    {
        [SerializeField] private CombatStyle style = CombatStyle.Accurate;

        private EquipmentAggregator equipment;
        private SkillManager skills;

        /// <summary>Current selected combat style.</summary>
        public CombatStyle Style
        {
            get => style;
            set => style = value;
        }

        private void Awake()
        {
            equipment = GetComponent<EquipmentAggregator>();
            skills = GetComponent<SkillManager>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F))
                CycleStyle();
        }

        /// <summary>Cycle combat style in order Accurate → Aggressive → Defensive → Controlled.</summary>
        public void CycleStyle()
        {
            style = (CombatStyle)(((int)style + 1) % 4);
        }

        /// <summary>Get a snapshot of combat stats for the player.</summary>
        public CombatantStats GetCombatantStats()
        {
            return CombatantStats.ForPlayer(skills, equipment, style, DamageType.Melee);
        }
    }
}
