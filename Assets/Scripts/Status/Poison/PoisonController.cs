using UnityEngine;
using Combat;
using Status;
using Util;

namespace Status.Poison
{
    /// <summary>
    /// Handles applying and updating poison on an entity.
    /// </summary>
    public class PoisonController : MonoBehaviour, ITickable
    {
        [Tooltip("Stats component implementing CombatTarget for this entity.")]
        [SerializeField] private MonoBehaviour statsComponent;

        private const float DefaultPoisonInterval = 15f;

        private CombatTarget stats;
        private CombatController combat;
        private PoisonEffect active;
        private float immunityTimer;
        private int ticksUntilNextDamage;
        private int intervalTicks;
        private bool tickerSubscribed;

        /// <summary>Invoked when poison deals damage.</summary>
        public event System.Action<int> OnPoisonTick;

        /// <summary>Invoked when the poison effect ends.</summary>
        public event System.Action OnPoisonEnd;

        /// <summary>Returns true if this entity is currently immune to poison.</summary>
        public bool IsImmune => immunityTimer > 0f;

        /// <summary>Access to the current poison effect.</summary>
        public PoisonEffect ActiveEffect => active;

        /// <summary>Remaining time of poison immunity in seconds.</summary>
        public float ImmunityTimer
        {
            get => immunityTimer;
            set => immunityTimer = value;
        }

        private void Awake()
        {
            stats = statsComponent as CombatTarget ?? GetComponent<CombatTarget>();
            combat = GetComponent<CombatController>() ?? GetComponentInParent<CombatController>() ?? GetComponentInChildren<CombatController>();
        }

        /// <summary>
        /// Apply a poison configuration to this entity, refreshing if already poisoned.
        /// </summary>
        public void ApplyPoison(PoisonConfig cfg)
        {
            if (IsImmune || cfg == null)
                return;
            if (stats != null && !stats.IsAlive)
                return;
            if (active == null)
            {
                active = new PoisonEffect(cfg);
                active.OnPoisonTick += HandlePoisonTick;
                active.OnPoisonEnd += HandlePoisonEnded;
            }
            active.ApplyTo(stats);
            ConfigureTickCadence(cfg, active.TickTimer);
            SubscribeToTicker();
            NotifyBuffApplied(cfg);
        }

        /// <summary>
        /// Cure current poison and optionally grant temporary immunity.
        /// </summary>
        public void CurePoison(float immunitySeconds)
        {
            if (active != null)
            {
                active.ForceEnd();
                active = null;
            }
            ticksUntilNextDamage = 0;
            intervalTicks = 0;
            UnsubscribeFromTicker();
            immunityTimer = Mathf.Max(immunityTimer, immunitySeconds);
        }

        /// <summary>
        /// Recalculates the countdown using the active effect's current state. Used after save restores.
        /// </summary>
        public void RefreshTickCountdown()
        {
            if (active == null)
            {
                ticksUntilNextDamage = 0;
                intervalTicks = 0;
                UnsubscribeFromTicker();
                return;
            }

            ConfigureTickCadence(active.Config, active.TickTimer);
            SubscribeToTicker();
        }

        private void Update()
        {
            if (immunityTimer > 0f)
                immunityTimer = Mathf.Max(0f, immunityTimer - Time.deltaTime);
        }

        /// <inheritdoc />
        public void OnTick()
        {
            if (active == null)
            {
                UnsubscribeFromTicker();
                return;
            }

            if (stats == null || !stats.IsAlive)
            {
                active.ForceEnd();
                return;
            }

            if (ticksUntilNextDamage > 0)
            {
                ticksUntilNextDamage--;
            }

            active.Tick(Ticker.TickDuration, DealTrueDamageBridge);
        }

        private void OnDisable()
        {
            if (active != null)
            {
                active.ForceEnd();
                active = null;
            }
            ticksUntilNextDamage = 0;
            intervalTicks = 0;
            UnsubscribeFromTicker();
        }

        /// <summary>
        /// Bridge method to apply true damage using the existing damage system.
        /// </summary>
        private bool DealTrueDamageBridge(int amount)
        {
            stats?.ApplyDamage(amount, DamageType.Poison, SpellElement.None, this);
            return true;
        }

        private void HandlePoisonTick(int damage)
        {
            ticksUntilNextDamage = intervalTicks;
            OnPoisonTick?.Invoke(damage);
        }

        private void HandlePoisonEnded()
        {
            var cfg = active != null ? active.Config : null;
            UnsubscribeFromTicker();
            ticksUntilNextDamage = 0;
            intervalTicks = 0;
            NotifyBuffRemoved(cfg);
            OnPoisonEnd?.Invoke();
            active = null;
        }

        /// <summary>
        /// Ensures the controller is subscribed to the shared ticker while poison is active.
        /// </summary>
        private void SubscribeToTicker()
        {
            if (tickerSubscribed)
                return;
            if (Ticker.Instance == null)
                return;
            Ticker.Instance.Subscribe(this);
            tickerSubscribed = true;
        }

        /// <summary>
        /// Removes the controller from the shared ticker to avoid dangling callbacks.
        /// </summary>
        private void UnsubscribeFromTicker()
        {
            if (!tickerSubscribed)
                return;
            if (Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }
            tickerSubscribed = false;
        }

        /// <summary>
        /// Updates tick cadence data so damage aligns with the shared tick countdown.
        /// </summary>
        private void ConfigureTickCadence(PoisonConfig cfg, float currentTickTimer)
        {
            if (cfg == null)
            {
                ticksUntilNextDamage = 0;
                intervalTicks = 0;
                return;
            }

            float intervalSeconds = cfg.tickIntervalSeconds > 0f ? cfg.tickIntervalSeconds : DefaultPoisonInterval;
            intervalTicks = Mathf.Max(1, Mathf.CeilToInt(intervalSeconds / Ticker.TickDuration));
            float clampedTimer = Mathf.Clamp(currentTickTimer, 0f, intervalSeconds);
            float remainingSeconds = Mathf.Max(0f, intervalSeconds - clampedTimer);
            int remainingTicks = Mathf.CeilToInt(remainingSeconds / Ticker.TickDuration);
            ticksUntilNextDamage = Mathf.Clamp(remainingTicks, 0, intervalTicks);
        }

        private void NotifyBuffApplied(PoisonConfig cfg)
        {
            if (combat == null || cfg == null)
                return;

            float interval = cfg.tickIntervalSeconds > 0f ? cfg.tickIntervalSeconds : DefaultPoisonInterval;
            string icon = !string.IsNullOrEmpty(cfg.Id) ? cfg.Id.ToLowerInvariant() : "poison";
            var definition = new BuffTimerDefinition
            {
                type = BuffType.Poison,
                displayName = "Poison",
                iconId = icon,
                durationSeconds = interval,
                recurringIntervalSeconds = interval,
                isRecurring = true,
                showExpiryWarning = false,
                expiryWarningTicks = 0
            };
            combat.ReportStatusEffectApplied(definition, cfg.Id, true);
        }

        private void NotifyBuffRemoved(PoisonConfig cfg)
        {
            if (combat == null)
                return;

            float interval = cfg != null && cfg.tickIntervalSeconds > 0f ? cfg.tickIntervalSeconds : DefaultPoisonInterval;
            string icon = cfg != null && !string.IsNullOrEmpty(cfg.Id) ? cfg.Id.ToLowerInvariant() : "poison";
            var definition = new BuffTimerDefinition
            {
                type = BuffType.Poison,
                displayName = "Poison",
                iconId = icon,
                durationSeconds = interval,
                recurringIntervalSeconds = interval,
                isRecurring = true,
                showExpiryWarning = false,
                expiryWarningTicks = 0
            };
            combat.ReportStatusEffectRemoved(definition, cfg != null ? cfg.Id : null);
        }
    }
}
