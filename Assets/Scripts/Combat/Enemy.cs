using UnityEngine;
using Player;
using Util;

namespace Combat
{
    /// <summary>
    /// Basic enemy implementation handling hitpoints, damage application
    /// and simple attack stats. Designed for tick based combat.
    /// </summary>
    [DisallowMultipleComponent]
    public class Enemy : MonoBehaviour, ITickable
    {
        [SerializeField] private int maxHitpoints = 10;
        [SerializeField] private int attack = 1;
        [SerializeField] private int defence = 1;
        [SerializeField] private int attackSpeedTicks = 4;

        [Header("Respawn")]
        [Tooltip("Number of ticks before this enemy respawns after dying. 0 disables respawning.")]
        [SerializeField] private int respawnTicks = 0;

        public int MaxHitpoints => maxHitpoints;
        public int CurrentHitpoints { get; private set; }
        public int Attack => attack;
        public int Defence => defence;
        public int AttackSpeedTicks => attackSpeedTicks;

        public event System.Action<Enemy> OnDied;
        public event System.Action<int> OnDamagedPlayer;

        private int respawnTimer;
        private Collider2D col;
        private SpriteRenderer sr;

        private void Awake()
        {
            CurrentHitpoints = maxHitpoints;
            col = GetComponent<Collider2D>();
            sr = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void OnDisable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
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

        public void OnTick()
        {
            if (CurrentHitpoints > 0 || respawnTicks <= 0)
                return;
            if (--respawnTimer <= 0)
                Respawn();
        }

        private void Die()
        {
            OnDied?.Invoke(this);
            if (respawnTicks > 0)
            {
                respawnTimer = respawnTicks;
                if (col) col.enabled = false;
                if (sr) sr.enabled = false;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Respawn()
        {
            CurrentHitpoints = maxHitpoints;
            if (col) col.enabled = true;
            if (sr) sr.enabled = true;
        }
    }
}
