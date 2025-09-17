using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.Save;

namespace Status.Poison
{
    /// <summary>
    /// Persists <see cref="PoisonController"/> state using the <see cref="SaveManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class PoisonSaveBridge : MonoBehaviour, ISaveable
    {
        [SerializeField] private PoisonController controller;

        /// <summary>
        /// Cached poison configurations loaded from <c>Resources/Status/Poison</c>.
        /// </summary>
        private static readonly Dictionary<string, PoisonConfig> CachedConfigs = new Dictionary<string, PoisonConfig>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks whether the poison configuration cache has been populated for the current domain reload.
        /// </summary>
        private static bool cacheBuilt;

        [System.Serializable]
        private class PoisonSaveData
        {
            public bool isPoisoned;
            public string configId;
            public int currentDamage;
            public int ticksSinceDecay;
            public float timeToNextTick;
            public float immunityTimer;
        }

        /// <summary>
        /// Cached snapshot of the active poison effect so we can persist even if the controller clears it first.
        /// </summary>
        private PoisonSaveData snapshot;

        /// <summary>
        /// Tracks whether <see cref="snapshot"/> currently represents an active poison effect.
        /// </summary>
        private bool hasSnapshot;

        /// <summary>
        /// Handle to a coroutine scheduled during load to delay HUD resync until the controller resolves dependencies.
        /// </summary>
        private Coroutine pendingResyncRoutine;

        private string SaveKey => $"poison_{gameObject.name}";

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<PoisonController>();
        }

        private void OnEnable()
        {
            SaveManager.Register(this);
            SubscribeToControllerEvents();
        }

        private void OnDisable()
        {
            Save();
            UnsubscribeFromControllerEvents();
            if (pendingResyncRoutine != null)
            {
                StopCoroutine(pendingResyncRoutine);
                pendingResyncRoutine = null;
            }
            SaveManager.Unregister(this);
        }

        private void LateUpdate()
        {
            CaptureSnapshotFromController();
        }

        /// <inheritdoc />
        public void Save()
        {
            var data = new PoisonSaveData
            {
                immunityTimer = controller != null ? controller.ImmunityTimer : 0f
            };
            var effect = controller != null ? controller.ActiveEffect : null;
            if (effect != null && effect.IsActive)
            {
                data.isPoisoned = true;
                var cfg = effect.Config;
                data.configId = cfg != null ? cfg.Id : null;
                data.currentDamage = effect.CurrentDamage;
                data.ticksSinceDecay = effect.TicksSinceDecay;
                data.timeToNextTick = cfg != null ? cfg.tickIntervalSeconds - effect.TickTimer : 0f;
            }
            else if (hasSnapshot && snapshot != null)
            {
                data.isPoisoned = snapshot.isPoisoned;
                data.configId = snapshot.configId;
                data.currentDamage = snapshot.currentDamage;
                data.ticksSinceDecay = snapshot.ticksSinceDecay;
                data.timeToNextTick = snapshot.timeToNextTick;
                data.immunityTimer = Mathf.Max(data.immunityTimer, snapshot.immunityTimer);
            }
            SaveManager.Save(SaveKey, data);
        }

        /// <inheritdoc />
        public void Load()
        {
            var data = SaveManager.Load<PoisonSaveData>(SaveKey);
            if (data == null || controller == null)
                return;

            controller.ImmunityTimer = data.immunityTimer;
            if (data.isPoisoned && !string.IsNullOrEmpty(data.configId))
            {
                var cfg = ResolveConfig(data.configId);
                if (cfg != null)
                {
                    float savedImmune = controller.ImmunityTimer;
                    controller.ImmunityTimer = 0f;
                    controller.ApplyPoison(cfg);
                    controller.ActiveEffect?.RestoreState(
                        data.currentDamage,
                        data.ticksSinceDecay,
                        cfg.tickIntervalSeconds - data.timeToNextTick);
                    controller.RefreshTickCountdown();
                    ScheduleBuffTimerResync();
                    controller.ImmunityTimer = savedImmune;
                }
            }
        }

        /// <summary>
        /// Ensures the local cache contains every <see cref="PoisonConfig"/> under <c>Resources/Status/Poison</c>.
        /// </summary>
        private static void BuildCacheIfNeeded()
        {
            if (cacheBuilt)
                return;

            cacheBuilt = true;
            CachedConfigs.Clear();
            var configs = Resources.LoadAll<PoisonConfig>("Status/Poison");
            foreach (var config in configs)
            {
                if (config == null || string.IsNullOrWhiteSpace(config.Id))
                    continue;

                // Case-insensitive dictionary prevents casing differences from blocking lookups.
                if (!CachedConfigs.ContainsKey(config.Id))
                    CachedConfigs.Add(config.Id, config);
            }
        }

        /// <summary>
        /// Resolves the poison configuration matching the persisted <paramref name="configId"/>.
        /// </summary>
        /// <param name="configId">Identifier persisted in the save data.</param>
        /// <returns>The matching configuration if found; otherwise <c>null</c>.</returns>
        private static PoisonConfig ResolveConfig(string configId)
        {
            BuildCacheIfNeeded();
            if (string.IsNullOrWhiteSpace(configId))
                return null;

            CachedConfigs.TryGetValue(configId, out var config);
            return config;
        }

        /// <summary>
        /// Subscribes to poison controller events so we know when to refresh or clear the cached snapshot.
        /// </summary>
        private void SubscribeToControllerEvents()
        {
            if (controller == null)
                return;

            controller.OnPoisonTick += HandlePoisonTick;
            controller.OnPoisonEnd += HandlePoisonEnded;
        }

        /// <summary>
        /// Unsubscribes from any events the bridge previously attached to.
        /// </summary>
        private void UnsubscribeFromControllerEvents()
        {
            if (controller == null)
                return;

            controller.OnPoisonTick -= HandlePoisonTick;
            controller.OnPoisonEnd -= HandlePoisonEnded;
        }

        /// <summary>
        /// Polls the controller for live poison data so the snapshot mirrors the current runtime state.
        /// </summary>
        private void CaptureSnapshotFromController()
        {
            if (controller == null)
                return;

            var effect = controller.ActiveEffect;
            EnsureSnapshot();
            snapshot.immunityTimer = controller.ImmunityTimer;

            if (effect != null && effect.IsActive)
            {
                snapshot.isPoisoned = true;
                var cfg = effect.Config;
                snapshot.configId = cfg != null ? cfg.Id : null;
                snapshot.currentDamage = effect.CurrentDamage;
                snapshot.ticksSinceDecay = effect.TicksSinceDecay;
                float interval = cfg != null ? Mathf.Max(0f, cfg.tickIntervalSeconds) : 0f;
                float timeRemaining = interval > 0f ? Mathf.Clamp(interval - effect.TickTimer, 0f, interval) : 0f;
                snapshot.timeToNextTick = timeRemaining;
                hasSnapshot = true;
            }
            else
            {
                if (controller == null || controller.enabled)
                    ClearSnapshotEffectState();
            }
        }

        /// <summary>
        /// Ensures the snapshot container exists before attempting to write to it.
        /// </summary>
        private void EnsureSnapshot()
        {
            if (snapshot == null)
                snapshot = new PoisonSaveData();
        }

        /// <summary>
        /// Called whenever a poison tick occurs so we capture the newly progressed state immediately.
        /// </summary>
        /// <param name="damage">Damage dealt by the tick (unused).</param>
        private void HandlePoisonTick(int damage)
        {
            CaptureSnapshotFromController();
        }

        /// <summary>
        /// Handles poison ending. If the controller is still enabled this represents an actual cure, so clear the snapshot.
        /// </summary>
        private void HandlePoisonEnded()
        {
            if (controller != null && controller.enabled)
            {
                ClearSnapshotEffectState();
            }
        }

        /// <summary>
        /// Ensures the buff timer resync runs only after the controller has resolved its combat dependency.
        /// </summary>
        private void ScheduleBuffTimerResync()
        {
            if (pendingResyncRoutine != null)
                StopCoroutine(pendingResyncRoutine);

            pendingResyncRoutine = StartCoroutine(ResyncWhenControllerReady());
        }

        /// <summary>
        /// Waits for the next frame (and until the controller reports readiness) before issuing the HUD resync.
        /// </summary>
        private IEnumerator ResyncWhenControllerReady()
        {
            // Allow at least one frame so PoisonController.Awake can run before resync executes.
            yield return null;

            while (controller != null && controller.isActiveAndEnabled && !controller.HasCombatController)
                yield return null;

            controller?.ResyncBuffTimerWithState();
            pendingResyncRoutine = null;
        }

        /// <summary>
        /// Clears the poison-specific portion of the cached snapshot so cured poison is not resurrected on load.
        /// </summary>
        private void ClearSnapshotEffectState()
        {
            if (snapshot == null)
                return;

            snapshot.isPoisoned = false;
            snapshot.configId = null;
            snapshot.currentDamage = 0;
            snapshot.ticksSinceDecay = 0;
            snapshot.timeToNextTick = 0f;
            hasSnapshot = false;
        }
    }
}
