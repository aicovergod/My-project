using UnityEngine;
using Core.Save;

namespace Status.Poison
{
    /// <summary>
    /// Persists <see cref="PoisonController"/> state using the <see cref="SaveManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PoisonSaveBridge : MonoBehaviour, ISaveable
    {
        [SerializeField] private PoisonController controller;

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
                var cfg = Resources.Load<PoisonConfig>($"Status/Poison/{data.configId}");
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
                    controller.ImmunityTimer = savedImmune;
                }
            }
        }
    }
}
