using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Util;
using World;

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

        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureServiceExists()
        {
            if (Instance != null)
                return;

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !PersistentSceneGate.ShouldSpawnInScene(activeScene))
            {
                BeginWaitingForAllowedScene();
                return;
            }

            CreateOrAdoptService();
        }

        private static BuffTimerService FindExistingService()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<BuffTimerService>();
#else
            return Object.FindObjectOfType<BuffTimerService>();
#endif
        }

        private static void CreateOrAdoptService()
        {
            if (Instance != null)
                return;

            StopWaitingForAllowedScene();

            var existing = FindExistingService();
            if (existing != null)
            {
                Instance = existing;
                existing.EnsurePersistenceComponent();
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    DontDestroyOnLoad(existing.gameObject);
                existing.EnsureSceneGateSubscription();
                return;
            }

            var go = new GameObject("BuffTimerService");
            go.AddComponent<ScenePersistentObject>();
            go.AddComponent<BuffTimerService>();
            DontDestroyOnLoad(go);
        }

        private static void BeginWaitingForAllowedScene()
        {
            if (waitingForAllowedScene)
                return;

            waitingForAllowedScene = true;
            PersistentSceneGate.SceneEvaluationChanged += HandleSceneEvaluationForBootstrap;
        }

        private static void StopWaitingForAllowedScene()
        {
            if (!waitingForAllowedScene)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneEvaluationForBootstrap;
            waitingForAllowedScene = false;
        }

        private static void HandleSceneEvaluationForBootstrap(Scene scene, bool allowed)
        {
            if (!allowed)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            CreateOrAdoptService();
        }

        [Tooltip("Hard limit to avoid runaway buff spawning in error cases.")]
        [SerializeField] private int maxTrackedBuffs = 64;

        [Tooltip("Log state transitions for debugging.")]
        [SerializeField] private bool logDebugMessages;

        private readonly Dictionary<BuffKey, BuffTimerInstance> activeBuffs = new();
        private readonly List<BuffKey> removalBuffer = new();
        private bool subscribedToTicker;
        private bool sceneGateSubscribed;
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
            EnsurePersistenceComponent();
            DontDestroyOnLoad(gameObject);

            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();
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

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                if (sceneGateSubscribed)
                {
                    PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
                    sceneGateSubscribed = false;
                }

                Instance = null;

                if (!applicationIsQuitting)
                    BeginWaitingForAllowedScene();
            }
        }

        private void EnsureSceneGateSubscription()
        {
            if (sceneGateSubscribed)
                return;

            PersistentSceneGate.SceneEvaluationChanged += HandleSceneGateEvaluation;
            sceneGateSubscribed = true;
        }

        private void HandleSceneGateEvaluation(Scene scene, bool allowed)
        {
            if (Instance != this)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            if (allowed)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
            sceneGateSubscribed = false;
            Destroy(gameObject);
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

        /// <summary>
        /// Restores a buff using previously serialised data. When no active instance is found
        /// a new timer is created, otherwise the metadata is refreshed and the remaining ticks
        /// are clamped to the saved countdown so HUD elements resume accurately.
        /// </summary>
        /// <param name="context">Buff metadata captured during saving.</param>
        /// <param name="remainingTicks">Remaining tick count at the time of the save.</param>
        public BuffTimerInstance RestoreBuff(BuffEventContext context, int remainingTicks)
        {
            if (context.target == null)
                return null;

            var key = new BuffKey(context.target, context.definition.type);
            if (!activeBuffs.TryGetValue(key, out var instance))
            {
                if (activeBuffs.Count >= maxTrackedBuffs)
                {
                    Debug.LogWarning($"BuffTimerService cannot restore buff {context.definition.type}; maximum capacity {maxTrackedBuffs} reached.");
                    return null;
                }

                sequenceCounter++;
                instance = new BuffTimerInstance(context, sequenceCounter);
                activeBuffs[key] = instance;
                EnsureTickerSubscription();
                Log($"Restored buff {instance.DisplayName} on {context.target.name} (ticks: {remainingTicks}).");
                BuffStarted?.Invoke(instance);
            }
            else
            {
                instance.ApplyContext(context);
                Log($"Updated buff {instance.DisplayName} on {context.target.name} during restore (ticks: {remainingTicks}).");
            }

            ApplyRestoredTickCount(instance, remainingTicks);
            BuffUpdated?.Invoke(instance);

            if (instance.CanWarn && instance.RemainingTicks == instance.WarningTicks)
                BuffWarning?.Invoke(instance);

            return instance;
        }

        /// <summary>
        /// Removes every buff currently associated with the supplied target. Primarily used for
        /// death/respawn flows so future buffs are automatically cleared without bespoke logic.
        /// </summary>
        /// <param name="target">GameObject whose buffs should be purged.</param>
        /// <param name="reason">Reason communicated to listeners when invoking <see cref="BuffEnded"/>.</param>
        public void RemoveAllBuffs(GameObject target, BuffEndReason reason = BuffEndReason.Manual)
        {
            if (target == null)
                return;

            removalBuffer.Clear();
            List<BuffTimerInstance> removedInstances = null;

            foreach (var pair in activeBuffs)
            {
                if (pair.Value.Target != target)
                    continue;

                removalBuffer.Add(pair.Key);
                removedInstances ??= new List<BuffTimerInstance>();
                removedInstances.Add(pair.Value);
            }

            if (removedInstances == null || removedInstances.Count == 0)
                return;

            for (int i = 0; i < removalBuffer.Count; i++)
                activeBuffs.Remove(removalBuffer[i]);
            removalBuffer.Clear();

            for (int i = 0; i < removedInstances.Count; i++)
            {
                var instance = removedInstances[i];
                BuffEnded?.Invoke(instance, reason);
                Log($"Removed buff {instance.DisplayName} from {target.name} via bulk removal.");
            }

            ReleaseTickerSubscriptionIfIdle();
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

        /// <summary>
        /// Clamps the restored tick count to sensible bounds before the service resumes ticking.
        /// </summary>
        private void ApplyRestoredTickCount(BuffTimerInstance instance, int remainingTicks)
        {
            if (instance == null)
                return;

            if (instance.Definition.isRecurring)
            {
                int clamped = remainingTicks;
                if (instance.IntervalTicks > 0)
                {
                    clamped = remainingTicks <= 0
                        ? Mathf.Max(1, instance.IntervalTicks)
                        : Mathf.Clamp(remainingTicks, 1, instance.IntervalTicks);
                }
                else
                {
                    clamped = Mathf.Max(1, remainingTicks);
                }
                instance.RemainingTicks = clamped;
                return;
            }

            if (instance.HasDuration)
            {
                if (instance.DurationTicks > 0)
                {
                    int clamped = remainingTicks < 0 ? instance.DurationTicks : remainingTicks;
                    instance.RemainingTicks = Mathf.Clamp(clamped, 0, instance.DurationTicks);
                }
                else
                {
                    instance.RemainingTicks = remainingTicks;
                }
                return;
            }

            instance.RemainingTicks = remainingTicks;
        }

        private void EnsurePersistenceComponent()
        {
            if (GetComponent<ScenePersistentObject>() == null)
                gameObject.AddComponent<ScenePersistentObject>();
        }
    }
}
