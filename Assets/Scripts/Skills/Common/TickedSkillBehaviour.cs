using System.Collections;
using UnityEngine;
using Util;

namespace Skills.Common
{
    /// <summary>
    ///     Base component for any skill that relies on the global <see cref="Ticker"/> to drive its logic.
    ///     Handles subscribing/unsubscribing when enabled or disabled and exposes hooks so derived
    ///     classes only need to focus on their actual tick behaviour.
    /// </summary>
    public abstract class TickedSkillBehaviour : MonoBehaviour, ITickable
    {
        private Coroutine tickerCoroutine;
        private bool tickerSubscribed;

        /// <summary>
        ///     When enabled the component will emit debug messages whenever it subscribes or unsubscribes
        ///     from the <see cref="Ticker"/> singleton. Derived classes can override this to toggle logging.
        /// </summary>
        protected virtual bool LogTickerSubscription => false;

        /// <summary>
        ///     Human readable identifier used in debug logs. Defaults to the component type name but can be
        ///     overridden for more context (for example, when multiple instances exist).
        /// </summary>
        protected virtual string TickerDebugName => GetType().Name;

        /// <summary>
        ///     Automatically attempts to subscribe to the ticker when the component becomes enabled.
        /// </summary>
        protected virtual void OnEnable()
        {
            TrySubscribeToTicker();
        }

        /// <summary>
        ///     Ensures the subscription (and any pending coroutine) is torn down when disabled so no stray
        ///     ticks are processed after the object leaves the scene.
        /// </summary>
        protected virtual void OnDisable()
        {
            CancelTickerSubscription();
        }

        /// <summary>
        ///     Explicitly attempts to register this behaviour with <see cref="Ticker.Instance"/>. When the
        ///     singleton is not yet spawned the base class will queue a coroutine that keeps retrying each
        ///     frame until the ticker becomes available.
        /// </summary>
        protected void TrySubscribeToTicker()
        {
            if (tickerSubscribed)
                return;

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Subscribe(this);
                tickerSubscribed = true;
                if (LogTickerSubscription)
                    Debug.Log($"{TickerDebugName} subscribed to ticker.");
                OnTickerReady();
            }
            else if (tickerCoroutine == null)
            {
                tickerCoroutine = StartCoroutine(WaitForTicker());
            }
        }

        /// <summary>
        ///     Stops listening for ticks and cancels the waiter coroutine if one is pending. Safe to invoke
        ///     multiple times and from derived classes when manual control is required.
        /// </summary>
        protected void CancelTickerSubscription()
        {
            if (tickerCoroutine != null)
            {
                StopCoroutine(tickerCoroutine);
                tickerCoroutine = null;
            }

            if (tickerSubscribed && Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
                if (LogTickerSubscription)
                    Debug.Log($"{TickerDebugName} unsubscribed from ticker.");
            }

            tickerSubscribed = false;
        }

        /// <summary>
        ///     Coroutine that waits for the ticker singleton to be created before subscribing.
        /// </summary>
        private IEnumerator WaitForTicker()
        {
            while (Ticker.Instance == null)
                yield return null;

            tickerCoroutine = null;
            Ticker.Instance.Subscribe(this);
            tickerSubscribed = true;
            if (LogTickerSubscription)
                Debug.Log($"{TickerDebugName} subscribed to ticker after waiting.");
            OnTickerReady();
        }

        /// <summary>
        ///     Called immediately after a successful subscription (both when the ticker already existed or
        ///     once the waiter coroutine completes). Derived skills can override this to perform any
        ///     additional setup that relies on the ticker being alive.
        /// </summary>
        protected virtual void OnTickerReady()
        {
        }

        /// <summary>
        ///     Utility property so derived classes can query the current subscription status if needed.
        /// </summary>
        protected bool IsSubscribedToTicker => tickerSubscribed;

        /// <summary>
        ///     Implementation of <see cref="ITickable"/> that forwards to <see cref="HandleTick"/> so
        ///     derived classes only need to override a single method.
        /// </summary>
        public void OnTick()
        {
            HandleTick();
        }

        /// <summary>
        ///     Called every OSRS tick. Derived skill behaviours implement their actual logic here.
        /// </summary>
        protected abstract void HandleTick();
    }
}
