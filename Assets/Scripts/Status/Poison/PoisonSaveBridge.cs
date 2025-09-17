using System;
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

        private string SaveKey => $"poison_{gameObject.name}";

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<PoisonController>();
        }

        private void OnEnable()
        {
            SaveManager.Register(this);
        }

        private void OnDisable()
        {
            Save();
            SaveManager.Unregister(this);
        }

        /// <inheritdoc />
        public void Save()
        {
            var data = new PoisonSaveData { immunityTimer = controller != null ? controller.ImmunityTimer : 0f };
            var effect = controller != null ? controller.ActiveEffect : null;
            if (effect != null && effect.IsActive)
            {
                data.isPoisoned = true;
                data.configId = effect.Config != null ? effect.Config.Id : null;
                data.currentDamage = effect.CurrentDamage;
                data.ticksSinceDecay = effect.TicksSinceDecay;
                data.timeToNextTick = effect.Config.tickIntervalSeconds - effect.TickTimer;
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
                    controller.ResyncBuffTimerWithState();
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
    }
}
