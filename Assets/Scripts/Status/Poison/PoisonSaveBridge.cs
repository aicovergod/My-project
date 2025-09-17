using System;
using System.Collections;
using UnityEngine;
using Core.Save;

namespace Status.Poison
{
    /// <summary>
    /// Persists <see cref="PoisonController"/> state using the <see cref="SaveManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [Obsolete("PoisonSaveBridge has been superseded by BuffStateSaveBridge. Enable legacy save only when required for debugging.")]
    public class PoisonSaveBridge : MonoBehaviour, ISaveable
    {
        [SerializeField] private PoisonController controller;

        [SerializeField, Tooltip("Enable deprecated SaveManager integration. Leave disabled so BuffStateSaveBridge persists poison state.")]
        private bool enableLegacySave;

        /// <summary>
        /// Resource path used to load the canonical poison configuration.
        /// </summary>
        private const string DefaultPoisonConfigResourcePath = "Status/Poison/Poison_p";

        /// <summary>
        /// Identifier recorded by the canonical poison configuration.
        /// </summary>
        private const string DefaultPoisonConfigId = "poison_p";

        /// <summary>
        /// Cached reference to the singleton poison configuration.
        /// </summary>
        private static PoisonConfig cachedPoisonConfig;

        /// <summary>
        /// Tracks whether a load attempt has been made so we avoid redundant error logging.
        /// </summary>
        private static bool attemptedPoisonConfigLoad;

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
        /// Handle to a coroutine scheduled during load to defer poison restoration and HUD resync until dependencies resolve.
        /// </summary>
        private Coroutine pendingResyncRoutine;

        /// <summary>
        /// Saved poison payload awaiting restoration once the controller and combat target are ready.
        /// </summary>
        private PoisonSaveData pendingRestoreData;

        /// <summary>
        /// Configuration associated with <see cref="pendingRestoreData"/>.
        /// </summary>
        private PoisonConfig pendingRestoreConfig;

        private string SaveKey => $"poison_{gameObject.name}";

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<PoisonController>();
        }

        private void OnEnable()
        {
            if (!enableLegacySave)
                return;

            SaveManager.Register(this);
            SubscribeToControllerEvents();
        }

        private void OnDisable()
        {
            if (!enableLegacySave)
                return;

            Save();
            UnsubscribeFromControllerEvents();
            CancelPendingRestore();
            SaveManager.Unregister(this);
        }

        private void LateUpdate()
        {
            if (!enableLegacySave)
                return;

            CaptureSnapshotFromController();
        }

        /// <inheritdoc />
        public void Save()
        {
            if (!enableLegacySave)
                return;

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
            if (!enableLegacySave)
                return;

            CancelPendingRestore();

            var data = SaveManager.Load<PoisonSaveData>(SaveKey);
            if (controller == null || data == null)
                return;

            controller.ImmunityTimer = data.immunityTimer;
            if (!data.isPoisoned)
                return;

            var cfg = ResolveConfig(data.configId);
            if (cfg == null)
                return;

            pendingRestoreData = data;
            pendingRestoreConfig = cfg;
            SchedulePendingRestore();
        }

        /// <summary>
        /// Resolves the canonical poison configuration regardless of the saved identifier.
        /// </summary>
        /// <param name="configId">Identifier persisted in the save data.</param>
        /// <returns>The matching configuration if found; otherwise <c>null</c>.</returns>
        private static PoisonConfig ResolveConfig(string configId)
        {
            if (!string.Equals(configId, DefaultPoisonConfigId, StringComparison.OrdinalIgnoreCase))
            {
                string savedId = string.IsNullOrWhiteSpace(configId) ? "<missing>" : configId;
                Debug.LogWarning($"PoisonSaveBridge encountered unexpected poison config id '{savedId}'. Defaulting to '{DefaultPoisonConfigId}'.");
            }

            if (cachedPoisonConfig != null)
                return cachedPoisonConfig;

            if (!attemptedPoisonConfigLoad)
            {
                attemptedPoisonConfigLoad = true;
                cachedPoisonConfig = Resources.Load<PoisonConfig>(DefaultPoisonConfigResourcePath);

                if (cachedPoisonConfig == null)
                    Debug.LogError($"PoisonSaveBridge could not load default poison configuration at '{DefaultPoisonConfigResourcePath}'.");
            }

            return cachedPoisonConfig;
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
        /// Ensures poison restoration occurs only after the controller resolves its dependencies.
        /// </summary>
        private void SchedulePendingRestore()
        {
            if (pendingResyncRoutine != null)
                StopCoroutine(pendingResyncRoutine);

            pendingResyncRoutine = StartCoroutine(RestoreWhenControllerReady());
        }

        /// <summary>
        /// Waits for controller readiness before reapplying poison and synchronizing HUD countdowns.
        /// </summary>
        private IEnumerator RestoreWhenControllerReady()
        {
            // Allow at least one frame so PoisonController.Awake can run before restoration executes.
            yield return null;

            while (controller != null)
            {
                if (!controller.isActiveAndEnabled)
                {
                    yield return null;
                    continue;
                }

                if (controller.HasAliveTarget)
                    break;

                yield return null;
            }

            if (controller == null || !controller.isActiveAndEnabled)
            {
                pendingRestoreData = null;
                pendingRestoreConfig = null;
                pendingResyncRoutine = null;
                yield break;
            }

            var data = pendingRestoreData;
            var config = pendingRestoreConfig;
            pendingRestoreData = null;
            pendingRestoreConfig = null;

            if (data != null && config != null)
            {
                float savedImmune = controller.ImmunityTimer;
                controller.ImmunityTimer = 0f;
                controller.ApplyPoison(config);

                var effect = controller.ActiveEffect;
                if (effect != null && effect.IsActive)
                {
                    float restoredTimer = Mathf.Max(0f, config.tickIntervalSeconds - data.timeToNextTick);
                    effect.RestoreState(data.currentDamage, data.ticksSinceDecay, restoredTimer);
                }

                if (controller != null && controller.isActiveAndEnabled)
                {
                    controller.RefreshTickCountdown();

                    if (controller.HasCombatController)
                        controller.ResyncBuffTimerWithState();
                }

                if (controller != null)
                    controller.ImmunityTimer = savedImmune;
            }
            else if (controller != null && controller.isActiveAndEnabled)
            {
                controller.RefreshTickCountdown();

                if (controller.HasCombatController)
                    controller.ResyncBuffTimerWithState();
            }

            pendingResyncRoutine = null;
        }

        /// <summary>
        /// Cancels any pending restoration coroutine and clears cached payload data.
        /// </summary>
        private void CancelPendingRestore()
        {
            if (pendingResyncRoutine != null)
            {
                StopCoroutine(pendingResyncRoutine);
                pendingResyncRoutine = null;
            }

            pendingRestoreData = null;
            pendingRestoreConfig = null;
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
