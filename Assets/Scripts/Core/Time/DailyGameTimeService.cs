using System;
using System.Collections;
using UnityEngine;
using Util;
using World;

namespace Core.Time
{
    /// <summary>
    /// Cross-game service that caches the current UTC day so any feature (fishing, quests,
    /// seasonal events, rotating shops, etc.) can coordinate deterministic day-based logic
    /// without depending on fishing-specific helpers.
    /// </summary>
    /// <remarks>
    /// The service is instantiated through <c>PersistentObjects.asset</c> which keeps it alive
    /// across scene loads. It polls <see cref="Ticker"/> once per OSRS-style tick to detect
    /// UTC day changes and raises <see cref="DayChanged"/> so interested systems can react
    /// when the calendar rolls over.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DailyGameTimeService : ScenePersistentObject, ITickable
    {
        private static DailyGameTimeService instance;

        private DateTime cachedUtcDay;
        private bool started;
        private bool subscribedToTicker;
        private Coroutine tickerSubscriptionRoutine;

        /// <summary>
        /// Event fired whenever <see cref="CurrentUtcDay"/> advances. Subscribers receive the
        /// newly cached day so they can refresh daily quests, shop inventories, or seasonal
        /// rotations in lockstep with the global calendar.
        /// </summary>
        public static event Action<DateTime> DayChanged;

        /// <summary>
        /// Returns the cached UTC calendar day. When the service has not booted yet the value
        /// falls back to <see cref="DateTime.UtcNow"/> so callers can still resolve seeds early
        /// during startup flows.
        /// </summary>
        public static DateTime CurrentUtcDay => instance != null ? instance.cachedUtcDay : DateTime.UtcNow.Date;

        /// <summary>
        /// Builds a deterministic daily seed using the cached UTC day ticks combined with
        /// contextual hashes provided by the caller. This keeps daily rolls stable for a
        /// calendar day while still allowing per-entity randomness.
        /// </summary>
        /// <param name="contextHashes">Hashes that should be folded into the daily seed.</param>
        /// <returns>A reproducible seed that stays constant for the current UTC day.</returns>
        public static int ComposeDailySeed(ReadOnlySpan<int> contextHashes)
        {
            long ticks = CurrentUtcDay.Ticks;

            unchecked
            {
                int seed = (int)(ticks ^ (ticks >> 32));
                foreach (int hash in contextHashes)
                {
                    seed = HashCombine(seed, hash);
                }

                return seed;
            }
        }

        /// <summary>
        /// Convenience overload when the caller does not need to supply additional context.
        /// </summary>
        public static int ComposeDailySeed()
        {
            return ComposeDailySeed(ReadOnlySpan<int>.Empty);
        }

        /// <inheritdoc />
        protected override void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            base.Awake();

            instance = this;
            cachedUtcDay = DateTime.UtcNow.Date;
        }

        private void Start()
        {
            started = true;
            SubscribeToTicker();
            EvaluateDayChange();
        }

        private void OnEnable()
        {
            if (instance == null)
            {
                instance = this;
            }

            if (started)
            {
                SubscribeToTicker();
                EvaluateDayChange();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromTicker();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                UnsubscribeFromTicker();
                instance = null;
            }
        }

        /// <inheritdoc />
        public void OnTick()
        {
            EvaluateDayChange();
        }

        private void SubscribeToTicker()
        {
            if (subscribedToTicker)
            {
                return;
            }

            if (Ticker.Instance == null)
            {
                if (tickerSubscriptionRoutine == null && isActiveAndEnabled)
                {
                    tickerSubscriptionRoutine = StartCoroutine(WaitForTickerAndSubscribe());
                }
                return;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
            EvaluateDayChange();
        }

        private void UnsubscribeFromTicker()
        {
            if (tickerSubscriptionRoutine != null)
            {
                StopCoroutine(tickerSubscriptionRoutine);
                tickerSubscriptionRoutine = null;
            }

            if (!subscribedToTicker)
            {
                return;
            }

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }

            subscribedToTicker = false;
        }

        private void EvaluateDayChange()
        {
            DateTime today = DateTime.UtcNow.Date;
            if (today == cachedUtcDay)
            {
                return;
            }

            cachedUtcDay = today;
            DayChanged?.Invoke(today);
        }

        /// <summary>
        /// Waits for the global <see cref="Ticker"/> singleton to finish booting so the service can
        /// receive OSRS tick callbacks even if it spawns before the ticker prefab.
        /// </summary>
        private IEnumerator WaitForTickerAndSubscribe()
        {
            while (Ticker.Instance == null)
            {
                yield return null;
            }

            tickerSubscriptionRoutine = null;

            if (!isActiveAndEnabled || subscribedToTicker)
            {
                yield break;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
            EvaluateDayChange();
        }

        private static int HashCombine(int a, int b)
        {
            unchecked
            {
                return (a * 397) ^ b;
            }
        }
    }
}
