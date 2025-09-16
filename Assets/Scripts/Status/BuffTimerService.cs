using System.Collections.Generic;
using UnityEngine;
using Util;

namespace Status
{
    /// <summary>
    /// Centralised runtime service that tracks timed buffs and publishes HUD friendly events. The
    /// service listens to <see cref="BuffEvents"/> so combat, consumables and scripted sequences can
    /// remain loosely coupled.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuffTimerService : MonoBehaviour, ITickable
    {
        public static BuffTimerService Instance { get; private set; }

        [Tooltip("Hard limit to avoid runaway buff spawning in error cases.")]
        [SerializeField] private int maxTrackedBuffs = 64;

        [Tooltip("Log state transitions for debugging.")]
        [SerializeField] private bool logDebugMessages;

        private readonly Dictionary<BuffKey, BuffTimerInstance> activeBuffs = new();
        private readonly List<BuffKey> removalBuffer = new();
        private bool subscribedToTicker;
        private int sequenceCounter;

        /// <summary>Raised when a buff becomes active for the first time.</summary>
        public event System.Action<BuffTimerInstance> BuffStarted;

        /// <summary>Raised every tick while a buff is active (after <see cref="RemainingTicks"/> updates).</summary>
        public event System.Action<BuffTimerInstance> BuffUpdated;

        /// <summary>Raised when a recurring buff loops back to its full duration.</summary>
        public event System.Action<BuffTimerInstance> BuffLooped;

        /// <summary>Raised when a buff reaches its configured warning threshold.</summary>
        public event System.Action<BuffTimerInstance> BuffWarning;

        /// <summary>Raised when a buff ends either by expiry or manual removal.</summary>
        public event System.Action<BuffTimerInstance, BuffEndReason> BuffEnded;

        public IReadOnlyDictionary<BuffKey, BuffTimerInstance> ActiveBuffs => activeBuffs;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            BuffEvents.BuffApplied += HandleBuffApplied;
            BuffEvents.BuffRefreshed += HandleBuffRefreshed;
            BuffEvents.BuffRemoved += HandleBuffRemoved;
        }

        private void OnDisable()
        {
            BuffEvents.BuffApplied -= HandleBuffApplied;
            BuffEvents.BuffRefreshed -= HandleBuffRefreshed;
            BuffEvents.BuffRemoved -= HandleBuffRemoved;

            if (subscribedToTicker && Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }
            subscribedToTicker = false;
        }

        /// <summary>
        /// Returns true if the target currently has the specified buff.
        /// </summary>
        public bool TryGetBuff(GameObject target, BuffType type, out BuffTimerInstance instance)
        {
            return activeBuffs.TryGetValue(new BuffKey(target, type), out instance);
        }

        /// <summary>
        /// Copies all buffs currently affecting the target into the provided buffer.
        /// </summary>
        public void GetBuffsFor(GameObject target, List<BuffTimerInstance> buffer)
        {
            if (buffer == null)
                return;
            foreach (var pair in activeBuffs)
            {
                if (pair.Value.Target == target)
                    buffer.Add(pair.Value);
            }
        }

        private void HandleBuffApplied(BuffEventContext context)
        {
            if (context.target == null)
                return;

            var key = new BuffKey(context.target, context.definition.type);
            if (!activeBuffs.TryGetValue(key, out var instance))
            {
                if (activeBuffs.Count >= maxTrackedBuffs)
                {
                    Debug.LogWarning($"BuffTimerService reached the maximum capacity of {maxTrackedBuffs}. Ignoring new buff {context.definition.type}.");
                    return;
                }

                sequenceCounter++;
                instance = new BuffTimerInstance(context, sequenceCounter);
                activeBuffs[key] = instance;
                EnsureTickerSubscription();
                Log($"Started buff {instance.DisplayName} on {context.target.name} (ticks: {instance.RemainingTicks}).");
                BuffStarted?.Invoke(instance);
                BuffUpdated?.Invoke(instance);
            }
            else
            {
                instance.ApplyContext(context);
                Log($"Refreshed buff {instance.DisplayName} on {context.target.name} (ticks: {instance.RemainingTicks}).");
                BuffUpdated?.Invoke(instance);
            }
        }

        private void HandleBuffRefreshed(BuffEventContext context)
        {
            if (context.target == null)
                return;

            var key = new BuffKey(context.target, context.definition.type);
            if (activeBuffs.TryGetValue(key, out var instance))
            {
                instance.ApplyContext(context);
                Log($"Updated buff metadata {instance.DisplayName} on {context.target.name}.");
                BuffUpdated?.Invoke(instance);
            }
            else
            {
                // If a refresh is received before an apply we treat it as a brand new buff.
                HandleBuffApplied(context);
            }
        }

        private void HandleBuffRemoved(BuffEventContext context)
        {
            var key = new BuffKey(context.target, context.definition.type);
            if (!activeBuffs.TryGetValue(key, out var instance))
                return;

            activeBuffs.Remove(key);
            BuffEnded?.Invoke(instance, BuffEndReason.Manual);
            Log($"Removed buff {instance.DisplayName} from {(context.target != null ? context.target.name : "target")}.");
            ReleaseTickerSubscriptionIfIdle();
        }

        public void OnTick()
        {
            removalBuffer.Clear();

            foreach (var pair in activeBuffs)
            {
                var instance = pair.Value;
                if (instance.IsIndefinite && !instance.IsRecurring)
                    continue;

                if (instance.IsRecurring)
                {
                    instance.RemainingTicks = Mathf.Max(0, instance.RemainingTicks - 1);
                    if (instance.RemainingTicks <= 0)
                    {
                        BuffLooped?.Invoke(instance);
                        instance.ResetTimer();
                    }
                    BuffUpdated?.Invoke(instance);
                    continue;
                }

                if (!instance.HasDuration)
                    continue;

                int newRemaining = Mathf.Max(0, instance.RemainingTicks - 1);
                instance.RemainingTicks = newRemaining;

                if (instance.CanWarn && newRemaining == instance.WarningTicks)
                    BuffWarning?.Invoke(instance);

                if (newRemaining <= 0)
                {
                    removalBuffer.Add(pair.Key);
                    BuffEnded?.Invoke(instance, BuffEndReason.Expired);
                    Log($"Buff {instance.DisplayName} expired.");
                }
                else
                {
                    BuffUpdated?.Invoke(instance);
                }
            }

            if (removalBuffer.Count > 0)
            {
                for (int i = 0; i < removalBuffer.Count; i++)
                {
                    activeBuffs.Remove(removalBuffer[i]);
                }
                removalBuffer.Clear();
                ReleaseTickerSubscriptionIfIdle();
            }
        }

        private void EnsureTickerSubscription()
        {
            if (subscribedToTicker)
                return;

            var ticker = Ticker.Instance ?? FindObjectOfType<Ticker>();
            if (ticker == null)
                return;

            ticker.Subscribe(this);
            subscribedToTicker = true;
        }

        private void ReleaseTickerSubscriptionIfIdle()
        {
            if (!subscribedToTicker || activeBuffs.Count > 0)
                return;

            var ticker = Ticker.Instance ?? FindObjectOfType<Ticker>();
            if (ticker != null)
                ticker.Unsubscribe(this);
            subscribedToTicker = false;
        }

        private void Log(string message)
        {
            if (logDebugMessages)
                Debug.Log($"[BuffTimerService] {message}");
        }
    }
}
