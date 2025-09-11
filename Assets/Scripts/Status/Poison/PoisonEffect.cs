using System;
using UnityEngine;
using Combat;

namespace Status.Poison
{
    /// <summary>
    /// Runtime poison state and ticking logic.
    /// </summary>
    public class PoisonEffect
    {
        /// <summary>Configuration driving this poison.</summary>
        public PoisonConfig Config { get; }

        /// <summary>Current damage dealt per tick.</summary>
        public int CurrentDamage { get; private set; }

        /// <summary>Ticks since last decay step.</summary>
        public int TicksSinceDecay { get; private set; }

        /// <summary>Time accumulated toward the next tick.</summary>
        public float TickTimer { get; private set; }

        /// <summary>Whether the poison is active.</summary>
        public bool IsActive => active;

        private bool active;

        /// <summary>Invoked when poison deals damage.</summary>
        public event Action<int> OnPoisonTick;

        /// <summary>Invoked when poison ends.</summary>
        public event Action OnPoisonEnd;

        /// <summary>
        /// Initialize a poison effect with the given configuration.
        /// </summary>
        public PoisonEffect(PoisonConfig cfg)
        {
            Config = cfg;
        }

        /// <summary>
        /// Start or refresh the poison on a target.
        /// </summary>
        /// <param name="target">Target stats (unused, for API compatibility).</param>
        public void ApplyTo(CombatTarget target)
        {
            active = true;
            CurrentDamage = Config.startDamagePerTick;
            TicksSinceDecay = 0;
            TickTimer = 0f;
        }

        /// <summary>
        /// Progress the poison and apply damage when appropriate.
        /// </summary>
        /// <param name="delta">Delta time.</param>
        /// <param name="dealTrueDamage">Delegate used to apply true damage.</param>
        public void Tick(float delta, Func<int, bool> dealTrueDamage)
        {
            if (!active)
                return;
            TickTimer += delta;
            if (TickTimer >= Config.tickIntervalSeconds)
            {
                TickTimer -= Config.tickIntervalSeconds;
                dealTrueDamage?.Invoke(CurrentDamage);
                OnPoisonTick?.Invoke(CurrentDamage);
                TicksSinceDecay++;
                if (TicksSinceDecay >= Config.hitsPerDecayStep)
                {
                    TicksSinceDecay = 0;
                    CurrentDamage = Mathf.Max(Config.minDamagePerTick, CurrentDamage - Config.decayAmountPerStep);
                }
                if (CurrentDamage <= Config.minDamagePerTick)
                    End();
            }
        }

        /// <summary>
        /// Forcefully end the poison effect.
        /// </summary>
        public void ForceEnd()
        {
            if (!active)
                return;
            End();
        }

        /// <summary>
        /// Restore internal state when loading from save.
        /// </summary>
        public void RestoreState(int currentDamage, int ticksSinceDecay, float tickTimer)
        {
            CurrentDamage = currentDamage;
            TicksSinceDecay = ticksSinceDecay;
            TickTimer = tickTimer;
            active = currentDamage > 0;
        }

        private void End()
        {
            active = false;
            OnPoisonEnd?.Invoke();
        }
    }
}
