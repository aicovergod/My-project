using System.Collections;
using System.Collections.Generic;
using Core.Save;
using UnityEngine;

namespace Status
{
    /// <summary>
    /// Bridges <see cref="BuffTimerService"/> with the save system so timed effects survive
    /// scene transitions and application restarts. The bridge serialises the active buff
    /// metadata for the configured target, then restores each timer after load so the HUD
    /// and gameplay logic resume in a consistent state.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class BuffStateSaveBridge : MonoBehaviour, ISaveable
    {
        [SerializeField, Tooltip("Optional explicit object that owns the buffs. Defaults to this GameObject when unset.")]
        private GameObject targetOverride;

        [SerializeField, Tooltip("Buff categories that are persisted by bespoke systems and should be ignored here.")]
        private BuffType[] ignoredBuffTypes = { BuffType.Poison };

        /// <summary>Reusable buffer for querying the service.</summary>
        private readonly List<BuffTimerInstance> runtimeBuffer = new();

        /// <summary>Lookup table mirroring <see cref="ignoredBuffTypes"/> for quick runtime checks.</summary>
        private readonly HashSet<BuffType> ignoredTypeSet = new();

        /// <summary>Queued buff data awaiting restoration once the timer service becomes available.</summary>
        private readonly List<BuffRestoreRecord> pendingRestores = new();

        /// <summary>Tracks whether a coroutine is already waiting for the timer service.</summary>
        private bool restoreCoroutineRunning;

        /// <summary>Structure describing the serialised state of a buff timer.</summary>
        [System.Serializable]
        private sealed class BuffSaveEntry
        {
            public BuffTimerDefinition definition;
            public BuffSourceType sourceType;
            public string sourceId;
            public int remainingTicks;
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
        }

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
                SaveManager.Delete(SaveKey);
                return;
            }

            var service = BuffTimerService.Instance;
            if (service == null)
            {
                SaveManager.Delete(SaveKey);
                return;
            }

            runtimeBuffer.Clear();
            service.GetBuffsFor(target, runtimeBuffer);
            if (runtimeBuffer.Count == 0)
            {
                SaveManager.Delete(SaveKey);
                runtimeBuffer.Clear();
                return;
            }

            var entries = new List<BuffSaveEntry>(runtimeBuffer.Count);
            foreach (var instance in runtimeBuffer)
            {
                if (instance == null)
                    continue;
                if (ignoredTypeSet.Contains(instance.Definition.type))
                    continue;

                entries.Add(new BuffSaveEntry
                {
                    definition = instance.Definition,
                    sourceType = instance.SourceType,
                    sourceId = instance.SourceId,
                    remainingTicks = instance.RemainingTicks
                });
            }

            runtimeBuffer.Clear();

            if (entries.Count == 0)
            {
                SaveManager.Delete(SaveKey);
                return;
            }

            var data = new BuffSaveData { entries = entries.ToArray() };
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
                    remainingTicks = entry.remainingTicks
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
            }

            pendingRestores.Clear();
            restoreCoroutineRunning = false;
        }

        /// <summary>
        /// Waits until the timer service exists before replaying queued restores.
        /// </summary>
        private IEnumerator WaitForServiceThenRestore()
        {
            restoreCoroutineRunning = true;
            while (isActiveAndEnabled && (BuffTimerService.Instance == null || Target == null))
                yield return null;

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
    }
}
