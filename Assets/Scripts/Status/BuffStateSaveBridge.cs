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
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            Save();
            SaveManager.Unregister(this);
            pendingRestores.Clear();
            restoreCoroutineRunning = false;
        }

        /// <summary>
        /// Serialises the currently active buff timers for the target object.
        /// </summary>
        public void Save()
        {
            var target = Target;
            if (target == null)
            {
                cachedSaveData = null;
                hasCachedSaveData = false;
                SaveManager.Delete(SaveKey);
                return;
            }

            var service = BuffTimerService.Instance;
            if (service == null)
            {
                if (hasCachedSaveData && cachedSaveData != null)
                    SaveManager.Save(SaveKey, cachedSaveData);
                return;
            }

            runtimeBuffer.Clear();
            service.GetBuffsFor(target, runtimeBuffer);
            if (runtimeBuffer.Count == 0)
            {
                SaveManager.Delete(SaveKey);
                runtimeBuffer.Clear();
                cachedSaveData = null;
                hasCachedSaveData = false;
                return;
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
                SaveManager.Delete(SaveKey);
                cachedSaveData = null;
                hasCachedSaveData = false;
                return;
            }

            var data = new BuffSaveData { entries = entries.ToArray() };
            cachedSaveData = data;
            hasCachedSaveData = true;
            SaveManager.Save(SaveKey, data);
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
                if (!restoreCoroutineRunning && isActiveAndEnabled)
                    StartCoroutine(WaitForServiceThenRestore());
                return;
            }

            if (HasPendingPoisonRestore() && !IsPoisonControllerReady(target))
            {
                if (!restoreCoroutineRunning && isActiveAndEnabled)
                    StartCoroutine(WaitForServiceThenRestore());
                return;
            }

            List<BuffRestoreRecord> deferred = null;

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
                    }
                }
            }

            pendingRestores.Clear();

            if (deferred != null && deferred.Count > 0)
            {
                pendingRestores.AddRange(deferred);
                if (!restoreCoroutineRunning && isActiveAndEnabled)
                    StartCoroutine(WaitForServiceThenRestore());
                return;
            }

            restoreCoroutineRunning = false;
        }

        /// <summary>
        /// Waits until the timer service exists before replaying queued restores.
        /// </summary>
        private IEnumerator WaitForServiceThenRestore()
        {
            restoreCoroutineRunning = true;
            while (isActiveAndEnabled)
            {
                if (BuffTimerService.Instance == null || Target == null)
                {
                    yield return null;
                    continue;
                }

                if (HasPendingPoisonRestore() && !IsPoisonControllerReady(Target))
                {
                    yield return null;
                    continue;
                }

                break;
            }

            restoreCoroutineRunning = false;
            TryRestorePendingBuffs();
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
            if (target == null)
                return false;

            var controller = target.GetComponent<PoisonController>();
            if (controller == null)
                return true;

            if (!controller.isActiveAndEnabled)
                return false;

            if (!controller.HasAliveTarget)
                return false;

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
                return true;
            }

            controller.ImmunityTimer = 0f;
            controller.ApplyPoison(config);

            var effect = controller.ActiveEffect;
            if (effect != null && effect.IsActive)
            {
                int restoredDamage = record.poisonCurrentDamage > 0
                    ? record.poisonCurrentDamage
                    : config.startDamagePerTick;
                int restoredTicksSinceDecay = Mathf.Max(0, record.poisonTicksSinceDecay);
                if (config.hitsPerDecayStep > 0)
                    restoredTicksSinceDecay = Mathf.Clamp(restoredTicksSinceDecay, 0, config.hitsPerDecayStep - 1);

                float interval = Mathf.Max(0f, config.tickIntervalSeconds);
                float remaining = Mathf.Max(0f, record.poisonTimeToNextTick);
                float restoredTimer = 0f;
                if (interval > 0f)
                {
                    float clampedRemaining = Mathf.Clamp(remaining, 0f, interval);
                    restoredTimer = Mathf.Clamp(interval - clampedRemaining, 0f, interval);
                }

                effect.RestoreState(restoredDamage, restoredTicksSinceDecay, restoredTimer);
            }

            controller.ImmunityTimer = savedImmunity;
            controller.RefreshTickCountdown();
            if (controller.HasCombatController)
                controller.ResyncBuffTimerWithState();

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
            }

            return cachedPoisonConfig;
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
