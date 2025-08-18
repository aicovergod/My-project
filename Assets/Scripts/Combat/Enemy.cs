using UnityEngine;
using Player;

namespace Combat
{
    /// <summary>
    /// Basic enemy implementation handling hitpoints, damage application
    /// and simple attack stats. Designed for tick based combat.
    /// </summary>
    [DisallowMultipleComponent]
    public class Enemy : MonoBehaviour
    {
        [SerializeField] private int maxHitpoints = 10;
        [SerializeField] private int attack = 1;
        [SerializeField] private int defence = 1;
        [SerializeField] private int attackSpeedTicks = 4;

        public int MaxHitpoints => maxHitpoints;
        public int CurrentHitpoints { get; private set; }
        public int Attack => attack;
        public int Defence => defence;
        public int AttackSpeedTicks => attackSpeedTicks;

        public event System.Action<Enemy> OnDied;
        public event System.Action<int> OnDamagedPlayer;

        private void Awake()
        {
            CurrentHitpoints = maxHitpoints;
        }

        public void ApplyDamage(int amount)
        {
            if (amount <= 0 || CurrentHitpoints <= 0)
                return;
            CurrentHitpoints = Mathf.Max(0, CurrentHitpoints - amount);
            if (CurrentHitpoints <= 0)
                Die();
        }

        public void DealDamage(PlayerHitpoints player, int amount)
        {
            if (amount <= 0)
                return;
            player.OnEnemyDealtDamage(amount);
            OnDamagedPlayer?.Invoke(amount);
        }

        private void Die()
        {
            OnDied?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
