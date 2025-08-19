using UnityEngine;
using Combat;
using MyGame.Drops;

namespace NPC
{
    /// <summary>
    /// Simple adaptor tying an NPC to the combat system using a combat profile.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcCombatant : MonoBehaviour, CombatTarget
    {
        [SerializeField] private NpcCombatProfile profile;
        private int currentHp;

        public event System.Action<int, int> OnHealthChanged; // current, max
        public event System.Action OnDeath;

        public bool IsAlive => currentHp > 0;
        public DamageType PreferredDefenceType => profile != null ? profile.AttackType : DamageType.Melee;
        public int CurrentHP => currentHp;
        public int MaxHP => profile != null ? profile.DefenceLevel : currentHp;

        private void Awake()
        {
            currentHp = profile != null ? profile.DefenceLevel : 1;
            OnHealthChanged?.Invoke(currentHp, MaxHP);
        }

        /// <summary>Apply damage to this NPC.</summary>
        public void ApplyDamage(int amount, DamageType type, object source)
        {
            currentHp = Mathf.Max(0, currentHp - amount);
            Debug.Log($"{name} took {amount} damage ({currentHp}/{MaxHP}).");
            OnHealthChanged?.Invoke(currentHp, MaxHP);
            if (currentHp <= 0)
            {
                OnDeath?.Invoke();
                var dropper = GetComponent<NpcDropper>();
                dropper?.OnDeath();
            }
        }

        /// <summary>Get combat stats for this NPC.</summary>
        public CombatantStats GetCombatantStats()
        {
            return CombatantStats.ForNpc(profile);
        }
    }
}
