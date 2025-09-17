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
        // Cached timer definition so removal payloads mirror the most recent HUD entry.
        private BuffTimerDefinition lastPoisonDefinition;
        private bool hasLastPoisonDefinition;

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

        /// <summary>
        /// Reissues the poison buff timer so HUD countdowns match the remaining lifetime after a save restore.
        /// </summary>
        public void ResyncBuffTimerWithState()
        {
            if (active == null)
                return;

            var cfg = active.Config;
            if (cfg == null)
                return;

            float lifetimeSeconds = CalculatePoisonLifetimeSeconds(cfg);
            float remainingSeconds = Mathf.Max(0f, lifetimeSeconds);

            if (lifetimeSeconds > 0f && cfg.decayAmountPerStep > 0 && cfg.hitsPerDecayStep > 0)
            {
                float intervalSeconds = cfg.tickIntervalSeconds > 0f ? cfg.tickIntervalSeconds : DefaultPoisonInterval;
                int decayAmount = Mathf.Max(1, cfg.decayAmountPerStep);
                int hitsPerStep = Mathf.Max(1, cfg.hitsPerDecayStep);
                // Determine how many full decay steps have completed so we can subtract their elapsed ticks.
                int totalDecaySteps = Mathf.Max(1, Mathf.CeilToInt((cfg.startDamagePerTick - cfg.minDamagePerTick) / (float)decayAmount));
                int damageDelta = Mathf.Max(0, cfg.startDamagePerTick - active.CurrentDamage);
                int completedSteps = Mathf.Clamp(damageDelta / decayAmount, 0, totalDecaySteps);
                // TicksSinceDecay tracks progress within the current step, so subtract that partial progress as well.
                int ticksIntoCurrentStep = Mathf.Clamp(active.TicksSinceDecay, 0, Mathf.Max(0, hitsPerStep - 1));
                long totalTicksConsumed = (long)completedSteps * hitsPerStep + ticksIntoCurrentStep;
                float partialTimer = Mathf.Clamp(active.TickTimer, 0f, intervalSeconds);
                float elapsedSeconds = (float)totalTicksConsumed * intervalSeconds + partialTimer;
                remainingSeconds = Mathf.Max(0f, lifetimeSeconds - elapsedSeconds);
            }

            ReportPoisonTimer(cfg, remainingSeconds, true);
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

        /// <summary>
        /// Calculates the total poison lifetime using the config values and default interval fallback.
        /// </summary>
        private float CalculatePoisonLifetimeSeconds(PoisonConfig cfg)
        {
            if (cfg == null)
                return 0f;

            float intervalSeconds = cfg.tickIntervalSeconds > 0f ? cfg.tickIntervalSeconds : DefaultPoisonInterval;
            if (cfg.decayAmountPerStep <= 0 || cfg.hitsPerDecayStep <= 0)
                return 0f;

            int decayAmount = Mathf.Max(1, cfg.decayAmountPerStep);
            int decaySteps = Mathf.Max(1, Mathf.CeilToInt((cfg.startDamagePerTick - cfg.minDamagePerTick) / (float)decayAmount));
            int totalTicks = decaySteps * cfg.hitsPerDecayStep;
            return Mathf.Max(0f, totalTicks * intervalSeconds);
        }

        /// <summary>
        /// Builds a consistent buff definition so the HUD receives identical metadata on apply and removal.
        /// </summary>
        private BuffTimerDefinition CreateBuffDefinition(PoisonConfig cfg, float durationSeconds)
        {
            string icon = cfg != null && !string.IsNullOrEmpty(cfg.Id) ? cfg.Id.ToLowerInvariant() : "poison";
            float clampedDuration = durationSeconds > 0f ? durationSeconds : 0f;
            return new BuffTimerDefinition
            {
                type = BuffType.Poison,
                displayName = "Poison",
                iconId = icon,
                durationSeconds = clampedDuration,
                recurringIntervalSeconds = 0f,
                isRecurring = false,
                showExpiryWarning = false,
                expiryWarningTicks = 0
            };
        }

        /// <summary>
        /// Caches the active timer definition and forwards it to the combat controller so shared systems update.
        /// </summary>
        private void ReportPoisonTimer(PoisonConfig cfg, float durationSeconds, bool refreshTimer)
        {
            if (combat == null || cfg == null)
                return;

            lastPoisonDefinition = CreateBuffDefinition(cfg, durationSeconds);
            hasLastPoisonDefinition = true;
            combat.ReportStatusEffectApplied(lastPoisonDefinition, cfg.Id, refreshTimer);
        }

        private void NotifyBuffApplied(PoisonConfig cfg)
        {
            if (cfg == null)
                return;

            float lifetimeSeconds = CalculatePoisonLifetimeSeconds(cfg);
            ReportPoisonTimer(cfg, lifetimeSeconds, true);
        }

        private void NotifyBuffRemoved(PoisonConfig cfg)
        {
            if (combat == null)
            {
                hasLastPoisonDefinition = false;
                return;
            }

            BuffTimerDefinition definition = hasLastPoisonDefinition
                ? lastPoisonDefinition
                : CreateBuffDefinition(cfg, CalculatePoisonLifetimeSeconds(cfg));
            combat.ReportStatusEffectRemoved(definition, cfg != null ? cfg.Id : null);
            hasLastPoisonDefinition = false;
        }
    }
}
