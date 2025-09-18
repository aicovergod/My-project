using System;
using System.Collections;
using System.Collections.Generic;
using Core.Save;
using UnityEngine;
using Status.Poison;

namespace Status
{
    /// <summary>
    /// Bridges <see cref="BuffTimerService"/> with the save system so timed effects survive
    /// scene transitions and application restarts. The bridge serialises the active buff
    /// metadata for the configured target, then restores each timer after load so the HUD
    /// and gameplay logic resume in a consistent state.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-75)]
    public sealed class BuffStateSaveBridge : MonoBehaviour, ISaveable
    {
        [SerializeField, Tooltip("Optional explicit object that owns the buffs. Defaults to this GameObject when unset.")]
        private GameObject targetOverride;

        [SerializeField, Tooltip("Buff categories that are persisted by bespoke systems and should be ignored here.")]
        private BuffType[] ignoredBuffTypes = Array.Empty<BuffType>();

        /// <summary>Reusable buffer for querying the service.</summary>
        private readonly List<BuffTimerInstance> runtimeBuffer = new();

        /// <summary>Lookup table mirroring <see cref="ignoredBuffTypes"/> for quick runtime checks.</summary>
        private readonly HashSet<BuffType> ignoredTypeSet = new();

        /// <summary>Queued buff data awaiting restoration once the timer service becomes available.</summary>
        private readonly List<BuffRestoreRecord> pendingRestores = new();

        /// <summary>Tracks whether a coroutine is already waiting for the timer service.</summary>
        private bool restoreCoroutineRunning;

        /// <summary>Most recent buff snapshot captured when the timer service was available.</summary>
        private BuffSaveData cachedSaveData;

        /// <summary>Indicates whether <see cref="cachedSaveData"/> currently holds valid data.</summary>
        private bool hasCachedSaveData;

        /// <summary>Service instance we are currently listening to for buff state changes.</summary>
        private BuffTimerService subscribedService;

        /// <summary>Structure describing the serialised state of a buff timer.</summary>
        [System.Serializable]
        private sealed class BuffSaveEntry
        {
            public BuffTimerDefinition definition;
            public BuffSourceType sourceType;
            public string sourceId;
            public int remainingTicks;
            public int poisonCurrentDamage;
            public int poisonTicksSinceDecay;
            public float poisonTimeToNextTick;
            public float poisonImmunityTimer;
        }

        /// <summary>Wrapper used when storing the buff entries in JSON format.</summary>
        [System.Serializable]
        private sealed class BuffSaveData
        {
            public BuffSaveEntry[] entries;
        }

        /// <summary>Runtime helper struct used when restoring timers.</summary>
        private struct BuffRestoreRecord
        {
            public BuffTimerDefinition definition;
            public BuffSourceType sourceType;
            public string sourceId;
            public int remainingTicks;
            public int poisonCurrentDamage;
            public int poisonTicksSinceDecay;
            public float poisonTimeToNextTick;
            public float poisonImmunityTimer;
        }

        /// <summary>Resource path for the default poison configuration.</summary>
        private const string DefaultPoisonConfigResourcePath = "Status/Poison/Poison_p";

        /// <summary>Identifier persisted by the default poison configuration.</summary>
        private const string DefaultPoisonConfigId = "poison_p";

        /// <summary>Singleton poison configuration loaded from Resources.</summary>
        private static PoisonConfig cachedPoisonConfig;

        /// <summary>Tracks whether we already attempted to resolve the poison configuration.</summary>
        private static bool attemptedPoisonConfigLoad;

        /// <summary>Resolves the GameObject that owns the buffs we are persisting.</summary>
        private GameObject Target => targetOverride != null ? targetOverride : gameObject;

        /// <summary>Unique save key derived from the owning object's name.</summary>
        private string SaveKey => $"buffs_{(Target != null ? Target.name : name)}";

        private void Awake()
        {
            RebuildIgnoredTypeSet();
        }

        private void OnValidate()
        {
            RebuildIgnoredTypeSet();
        }

        private void OnEnable()
        {
            SaveManager.Register(this);
            SubscribeToServiceEvents();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            Save();
            UnsubscribeFromServiceEvents();
            SaveManager.Unregister(this);
            pendingRestores.Clear();
            restoreCoroutineRunning = false;
        }

        /// <summary>
        /// Hooks into the buff timer service so cached snapshots stay synchronised with runtime changes.
        /// </summary>
        /// <param name="service">Optional service override. Defaults to <see cref="BuffTimerService.Instance"/>.</param>
        private void SubscribeToServiceEvents(BuffTimerService service = null)
        {
            service ??= BuffTimerService.Instance;
            if (service == null || service == subscribedService)
                return;

            UnsubscribeFromServiceEvents();

            service.BuffStarted += HandleBuffStateChanged;
            service.BuffUpdated += HandleBuffStateChanged;
            service.BuffEnded += HandleBuffEnded;
            subscribedService = service;

            CaptureSnapshot(subscribedService, Target);
        }

        /// <summary>
        /// Removes event subscriptions when the component disables or the service changes.
        /// </summary>
        private void UnsubscribeFromServiceEvents()
        {
            if (subscribedService == null)
                return;

            subscribedService.BuffStarted -= HandleBuffStateChanged;
            subscribedService.BuffUpdated -= HandleBuffStateChanged;
            subscribedService.BuffEnded -= HandleBuffEnded;
            subscribedService = null;
        }

        /// <summary>
        /// Serialises the currently active buff timers for the target object.
        /// </summary>
        public void Save()
        {
            var target = Target;
            if (target == null)
            {
                ClearCachedSnapshot();
                SaveManager.Delete(SaveKey);
                return;
            }

            var service = BuffTimerService.Instance;
            if (service == null)
            {
                if (hasCachedSaveData && cachedSaveData != null)
                    SaveManager.Save(SaveKey, cachedSaveData);
                else
                    SaveManager.Delete(SaveKey);
                return;
            }

            bool hasSnapshot = CaptureSnapshot(service, target);
            if (!hasSnapshot)
            {
                SaveManager.Delete(SaveKey);
                return;
            }

            SaveManager.Save(SaveKey, cachedSaveData);
        }

        /// <summary>
        /// Restores previously saved buffs and reapplies them to the timer service.
        /// </summary>
        public void Load()
        {
            pendingRestores.Clear();

            var data = SaveManager.Load<BuffSaveData>(SaveKey);
            if (data?.entries == null || data.entries.Length == 0)
                return;

            for (int i = 0; i < data.entries.Length; i++)
            {
                var entry = data.entries[i];
                if (entry == null)
                    continue;
                if (ignoredTypeSet.Contains(entry.definition.type))
                    continue;

                pendingRestores.Add(new BuffRestoreRecord
                {
                    definition = entry.definition,
                    sourceType = entry.sourceType,
                    sourceId = entry.sourceId,
                    remainingTicks = entry.remainingTicks,
                    poisonCurrentDamage = entry.poisonCurrentDamage,
                    poisonTicksSinceDecay = entry.poisonTicksSinceDecay,
                    poisonTimeToNextTick = entry.poisonTimeToNextTick,
                    poisonImmunityTimer = entry.poisonImmunityTimer
                });
            }

            TryRestorePendingBuffs();
        }

        /// <summary>
        /// Attempts to replay any queued buff restore operations.
        /// </summary>
        private void TryRestorePendingBuffs()
        {
            if (pendingRestores.Count == 0)
                return;

            var target = Target;
            var service = BuffTimerService.Instance;
            if (target == null || service == null)
            {
                string ownerName = target != null ? target.name : name;
                string reason = target == null
                    ? "no target GameObject was resolved"
                    : "the BuffTimerService singleton is unavailable";
                Debug.LogWarning(
                    $"BuffStateSaveBridge deferred restoring {pendingRestores.Count} buff timer(s) for '{ownerName}' because {reason}.",
                    target != null ? (UnityEngine.Object)target : this);
                if (!restoreCoroutineRunning && isActiveAndEnabled)
                    StartCoroutine(WaitForServiceThenRestore());
                return;
            }

            SubscribeToServiceEvents(service);

            if (HasPendingPoisonRestore() && !IsPoisonControllerReady(target))
            {
                Debug.Log(
                    $"BuffStateSaveBridge deferred restoring {pendingRestores.Count} buff timer(s) for '{target.name}' because HasPendingPoisonRestore() returned true while the poison controller was not ready.",
                    target);
                if (!restoreCoroutineRunning && isActiveAndEnabled)
                    StartCoroutine(WaitForServiceThenRestore());
                return;
            }

            List<BuffRestoreRecord> deferred = null;
            int processedCount = pendingRestores.Count;

            for (int i = 0; i < pendingRestores.Count; i++)
            {
                var record = pendingRestores[i];
                var context = new BuffEventContext
                {
                    target = target,
                    definition = record.definition,
                    sourceType = record.sourceType,
                    sourceId = record.sourceId,
                    resetTimer = false
                };
                service.RestoreBuff(context, record.remainingTicks);

                if (record.definition.type == BuffType.Poison)
                {
                    bool restored = TryRestorePoisonState(target, record);
                    if (!restored)
                    {
                        deferred ??= new List<BuffRestoreRecord>();
                        deferred.Add(record);
                        Debug.LogWarning(
                            $"BuffStateSaveBridge re-queued poison state restoration for '{target.name}' because the poison controller was not ready.",
                            target);
                    }
                }
            }

            pendingRestores.Clear();

            if (deferred != null && deferred.Count > 0)
            {
                pendingRestores.AddRange(deferred);
                Debug.Log(
                    $"BuffStateSaveBridge deferred {deferred.Count} poison restore entr{(deferred.Count == 1 ? "y" : "ies")} for '{target.name}' pending poison controller readiness.",
                    target);
                if (!restoreCoroutineRunning && isActiveAndEnabled)
                    StartCoroutine(WaitForServiceThenRestore());
                return;
            }

            Debug.Log(
                $"BuffStateSaveBridge restored {processedCount} buff timer(s) immediately for '{target.name}'.",
                target);
            restoreCoroutineRunning = false;
        }

        /// <summary>
        /// Waits until the timer service exists before replaying queued restores.
        /// </summary>
        private IEnumerator WaitForServiceThenRestore()
        {
            restoreCoroutineRunning = true;
            Debug.Log(
                $"BuffStateSaveBridge wait routine started for '{(Target != null ? Target.name : name)}' with {pendingRestores.Count} pending buff timer(s).",
                this);
            bool loggedServiceWait = false;
            bool loggedPoisonWait = false;
            while (isActiveAndEnabled)
            {
                var service = BuffTimerService.Instance;
                var target = Target;
                if (service == null || target == null)
                {
                    if (!loggedServiceWait)
                    {
                        string reason = service == null
                            ? "BuffTimerService.Instance is null"
                            : "the target GameObject resolved to null";
                        Debug.LogWarning(
                            $"BuffStateSaveBridge wait routine yielding because {reason} while {pendingRestores.Count} buff timer(s) remain queued for '{(target != null ? target.name : name)}'.",
                            this);
                        loggedServiceWait = true;
                        loggedPoisonWait = false;
                    }
                    yield return null;
                    continue;
                }

                if (HasPendingPoisonRestore() && !IsPoisonControllerReady(target))
                {
                    if (!loggedPoisonWait)
                    {
                        Debug.Log(
                            $"BuffStateSaveBridge wait routine yielding because HasPendingPoisonRestore() returned true but the poison controller on '{target.name}' is not ready. Pending entries: {pendingRestores.Count}.",
                            target);
                        loggedPoisonWait = true;
                        loggedServiceWait = false;
                    }
                    yield return null;
                    continue;
                }

                break;
            }

            SubscribeToServiceEvents();
            restoreCoroutineRunning = false;
            Debug.Log(
                $"BuffStateSaveBridge wait routine resuming restoration for '{(Target != null ? Target.name : name)}'. Pending entries: {pendingRestores.Count}, HasPendingPoisonRestore(): {HasPendingPoisonRestore()}.",
                this);
            TryRestorePendingBuffs();
        }

        /// <summary>
        /// Captures snapshots whenever buffs on the tracked target start or update.
        /// </summary>
        /// <param name="instance">Instance associated with the service event.</param>
        private void HandleBuffStateChanged(BuffTimerInstance instance)
        {
            if (instance == null)
                return;

            var target = Target;
            if (target == null || instance.Target != target)
                return;

            var service = subscribedService ?? BuffTimerService.Instance;
            CaptureSnapshot(service, target);
        }

        /// <summary>
        /// Captures snapshots when buffs on the tracked target end so cached data mirrors reality.
        /// </summary>
        /// <param name="instance">Instance that finished.</param>
        /// <param name="reason">Reason reported by the timer service.</param>
        private void HandleBuffEnded(BuffTimerInstance instance, BuffEndReason reason)
        {
            if (instance == null)
                return;

            var target = Target;
            if (target == null || instance.Target != target)
                return;

            var service = subscribedService ?? BuffTimerService.Instance;
            CaptureSnapshot(service, target);
        }

        /// <summary>
        /// Rebuilds the cached ignore lookup whenever the inspector values change.
        /// </summary>
        private void RebuildIgnoredTypeSet()
        {
            ignoredTypeSet.Clear();
            if (ignoredBuffTypes == null)
                return;

            for (int i = 0; i < ignoredBuffTypes.Length; i++)
                ignoredTypeSet.Add(ignoredBuffTypes[i]);
        }

        /// <summary>
        /// Copies the active poison effect state into the supplied save entry.
        /// </summary>
        /// <param name="instance">Buff timer instance currently tracked by the service.</param>
        /// <param name="entry">Destination entry being serialised.</param>
        private static void CapturePoisonState(BuffTimerInstance instance, BuffSaveEntry entry)
        {
            if (instance?.Target == null)
                return;

            var controller = instance.Target.GetComponent<PoisonController>();
            if (controller == null)
                return;

            entry.poisonImmunityTimer = Mathf.Max(0f, controller.ImmunityTimer);

            var effect = controller.ActiveEffect;
            if (effect == null || !effect.IsActive)
            {
                entry.poisonCurrentDamage = 0;
                entry.poisonTicksSinceDecay = 0;
                entry.poisonTimeToNextTick = 0f;
                return;
            }

            entry.poisonCurrentDamage = effect.CurrentDamage;
            entry.poisonTicksSinceDecay = effect.TicksSinceDecay;

            var cfg = effect.Config;
            float interval = cfg != null ? Mathf.Max(0f, cfg.tickIntervalSeconds) : 0f;
            float timeRemaining = interval > 0f ? Mathf.Clamp(interval - effect.TickTimer, 0f, interval) : 0f;
            entry.poisonTimeToNextTick = timeRemaining;

            if (cfg == null || string.IsNullOrEmpty(cfg.Id))
                return;

            entry.sourceId = cfg.Id;
        }

        /// <summary>
        /// Restores poison controller state using data persisted alongside the buff timer.
        /// </summary>
        /// <param name="target">GameObject that owns the poison controller.</param>
        /// <param name="record">Serialized poison payload.</param>
        /// <returns>True when the poison state was successfully restored.</returns>
        private bool TryRestorePoisonState(GameObject target, BuffRestoreRecord record)
        {
            string targetName = target != null ? target.name : "<null>";
            if (target == null)
            {
                Debug.LogWarning(
                    $"BuffStateSaveBridge could not restore poison state because the resolved target GameObject was null for source id '{(string.IsNullOrWhiteSpace(record.sourceId) ? "<missing>" : record.sourceId)}'.",
                    this);
                return false;
            }

            var controller = target.GetComponent<PoisonController>();
            if (controller == null)
            {
                Debug.Log(
                    $"BuffStateSaveBridge skipped poison state restoration for '{targetName}' because no PoisonController component was present. The buff timer has already been restored by the service.",
                    target);
                return true;
            }

            if (!controller.isActiveAndEnabled)
            {
                Debug.LogWarning(
                    $"BuffStateSaveBridge deferred poison state restoration for '{targetName}' because the PoisonController component is disabled.",
                    controller);
                return false;
            }

            if (!controller.HasAliveTarget)
            {
                Debug.LogWarning(
                    $"BuffStateSaveBridge deferred poison state restoration for '{targetName}' because the PoisonController reports no alive target.",
                    controller);
                return false;
            }

            if (!string.Equals(record.sourceId, DefaultPoisonConfigId, StringComparison.OrdinalIgnoreCase))
            {
                string savedId = string.IsNullOrWhiteSpace(record.sourceId) ? "<missing>" : record.sourceId;
                Debug.LogWarning(
                    $"BuffStateSaveBridge encountered unexpected poison config id '{savedId}' while restoring '{target.name}'. Defaulting to '{DefaultPoisonConfigId}'.",
                    target);
            }

            var config = ResolvePoisonConfig();
            float savedImmunity = Mathf.Max(0f, record.poisonImmunityTimer);

            if (config == null)
            {
                controller.ImmunityTimer = savedImmunity;
                controller.RefreshTickCountdown();
                if (controller.HasCombatController)
                    controller.ResyncBuffTimerWithState();
                Debug.LogWarning(
                    $"BuffStateSaveBridge restored only poison immunity timers for '{targetName}' because the default poison configuration could not be resolved.",
                    controller);
                return true;
            }

            controller.ImmunityTimer = 0f;
            controller.ApplyPoison(config);

            var effect = controller.ActiveEffect;
            bool effectRestored = false;
            int finalDamage = 0;
            int finalTicksSinceDecay = 0;
            float finalTimer = 0f;
            if (effect != null && effect.IsActive)
            {
                finalDamage = record.poisonCurrentDamage > 0
                    ? record.poisonCurrentDamage
                    : config.startDamagePerTick;
                finalTicksSinceDecay = Mathf.Max(0, record.poisonTicksSinceDecay);
                if (config.hitsPerDecayStep > 0)
                    finalTicksSinceDecay = Mathf.Clamp(finalTicksSinceDecay, 0, config.hitsPerDecayStep - 1);

                float interval = Mathf.Max(0f, config.tickIntervalSeconds);
                float remaining = Mathf.Max(0f, record.poisonTimeToNextTick);
                finalTimer = 0f;
                if (interval > 0f)
                {
                    float clampedRemaining = Mathf.Clamp(remaining, 0f, interval);
                    finalTimer = Mathf.Clamp(interval - clampedRemaining, 0f, interval);
                }

                effect.RestoreState(finalDamage, finalTicksSinceDecay, finalTimer);
                effectRestored = true;
            }

            controller.ImmunityTimer = savedImmunity;
            controller.RefreshTickCountdown();
            if (controller.HasCombatController)
                controller.ResyncBuffTimerWithState();

            Debug.Log(
                $"BuffStateSaveBridge successfully restored poison state for '{targetName}'. Effect restored: {effectRestored}, damage: {finalDamage}, ticksSinceDecay: {finalTicksSinceDecay}, timer: {finalTimer:0.###}, immunity: {savedImmunity:0.###}.",
                controller);
            return true;
        }

        /// <summary>
        /// Resolves the singleton poison configuration so restores always use the canonical asset.
        /// </summary>
        /// <returns>Matching configuration instance when found; otherwise <c>null</c>.</returns>
        private static PoisonConfig ResolvePoisonConfig()
        {
            if (cachedPoisonConfig != null)
                return cachedPoisonConfig;

            if (!attemptedPoisonConfigLoad)
            {
                attemptedPoisonConfigLoad = true;
                cachedPoisonConfig = Resources.Load<PoisonConfig>(DefaultPoisonConfigResourcePath);

                if (cachedPoisonConfig == null)
                    Debug.LogError($"BuffStateSaveBridge could not load default poison configuration at '{DefaultPoisonConfigResourcePath}'.");
                else
                    Debug.Log($"BuffStateSaveBridge resolved default poison configuration '{cachedPoisonConfig.name}'.", cachedPoisonConfig);
            }

            return cachedPoisonConfig;
        }

        /// <summary>
        /// Captures the current buff state for the configured target and caches the result for future saves.
        /// </summary>
        /// <param name="service">Service that owns the runtime buff instances.</param>
        /// <param name="target">Target whose buffs should be persisted.</param>
        /// <returns>True when the snapshot contains at least one persisted entry.</returns>
        private bool CaptureSnapshot(BuffTimerService service, GameObject target)
        {
            if (target == null)
            {
                ClearCachedSnapshot();
                return false;
            }

            if (service == null)
                return hasCachedSaveData && cachedSaveData != null;

            runtimeBuffer.Clear();
            service.GetBuffsFor(target, runtimeBuffer);
            if (runtimeBuffer.Count == 0)
            {
                runtimeBuffer.Clear();
                ClearCachedSnapshot();
                return false;
            }

            var entries = new List<BuffSaveEntry>(runtimeBuffer.Count);
            foreach (var instance in runtimeBuffer)
            {
                if (instance == null)
                    continue;
                if (ignoredTypeSet.Contains(instance.Definition.type))
                    continue;

                var entry = new BuffSaveEntry
                {
                    definition = instance.Definition,
                    sourceType = instance.SourceType,
                    sourceId = instance.SourceId,
                    remainingTicks = instance.RemainingTicks
                };

                if (entry.definition.type == BuffType.Poison)
                    CapturePoisonState(instance, entry);

                entries.Add(entry);
            }

            runtimeBuffer.Clear();

            if (entries.Count == 0)
            {
                ClearCachedSnapshot();
                return false;
            }

            cachedSaveData = new BuffSaveData { entries = entries.ToArray() };
            hasCachedSaveData = true;
            return true;
        }

        /// <summary>
        /// Clears the cached snapshot so the save system knows no buff state is pending persistence.
        /// </summary>
        private void ClearCachedSnapshot()
        {
            cachedSaveData = null;
            hasCachedSaveData = false;
        }

        /// <summary>
        /// Returns true when any pending restore entries correspond to poison effects.
        /// </summary>
        private bool HasPendingPoisonRestore()
        {
            for (int i = 0; i < pendingRestores.Count; i++)
            {
                if (pendingRestores[i].definition.type == BuffType.Poison)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the poison controller is ready to accept restored state.
        /// </summary>
        /// <param name="target">GameObject that should host the controller.</param>
        private static bool IsPoisonControllerReady(GameObject target)
        {
            if (target == null)
                return false;

            var controller = target.GetComponent<PoisonController>();
            if (controller == null)
                return true;

            if (!controller.isActiveAndEnabled)
                return false;

            return controller.HasAliveTarget;
        }
    }
}
