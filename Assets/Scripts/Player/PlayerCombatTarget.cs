using UnityEngine;
using Combat;
using Skills.Mining;

namespace Player
{
    /// <summary>
    /// Adapts the player into a CombatTarget so NPCs can attack.
    /// </summary>
    [RequireComponent(typeof(PlayerHitpoints))]
    public class PlayerCombatTarget : MonoBehaviour, CombatTarget
    {
        private PlayerHitpoints hitpoints;

        private void Awake()
        {
            hitpoints = GetComponent<PlayerHitpoints>();
        }

        public bool IsAlive => hitpoints.CurrentHp > 0;
        public DamageType PreferredDefenceType => DamageType.Melee;
        public int CurrentHP => hitpoints.CurrentHp;
        public int MaxHP => hitpoints.MaxHp;

        public void ApplyDamage(int amount, DamageType type, object source)
        {
            hitpoints.OnEnemyDealtDamage(amount);
            FloatingText.Show(amount.ToString(), transform.position, Color.red);
            Debug.Log($"Player took {amount} damage.");
        }
    }
}
