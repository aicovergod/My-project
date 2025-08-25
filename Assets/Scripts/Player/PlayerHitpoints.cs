using System.Collections;
using UnityEngine;
using Skills;
using Skills.Mining; // reuse XpTable
using Core.Save;

namespace Player
{
    /// <summary>
    /// Handles hitpoints skill, HP logic and XP integration.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHitpoints : MonoBehaviour
    {
        [SerializeField] private XpTable xpTable;
        [SerializeField] private bool passiveRegenEnabled;
        [SerializeField] private MonoBehaviour saveProvider; // optional custom save provider

        private IHitpointsSave save;

        private float xp;
        private int level;
        private int currentHp;

        private Coroutine regenRoutine;
        private Coroutine saveRoutine;

        public event System.Action<int, int> OnHealthChanged; // current, max
        public event System.Action<int> OnHitpointsLevelChanged; // new level

        public int Level => level;
        public float Xp => xp;
        public int CurrentHp => currentHp;
        public int MaxHp => level;
        public bool PassiveRegenEnabled
        {
            get => passiveRegenEnabled;
            set
            {
                if (passiveRegenEnabled == value)
                    return;
                passiveRegenEnabled = value;
                if (enabled)
                {
                    if (passiveRegenEnabled)
                        StartRegen();
                    else
                        StopRegen();
                }
            }
        }

        private void Awake()
        {
            save = saveProvider as IHitpointsSave ?? new SaveManagerHitpointsSave();
            xp = save.LoadXp();
            if (xp <= 0f && xpTable != null)
                xp = xpTable.GetXpForLevel(10);
            level = xpTable != null ? xpTable.GetLevel(Mathf.FloorToInt(xp)) : 10;
            currentHp = save.LoadCurrentHp();
            if (currentHp <= 0)
                currentHp = level;
            currentHp = Mathf.Clamp(currentHp, 0, level);
            OnHealthChanged?.Invoke(currentHp, MaxHp);
        }

        private void OnEnable()
        {
            saveRoutine = StartCoroutine(SaveLoop());
            if (passiveRegenEnabled)
                StartRegen();
        }

        private void OnDisable()
        {
            if (saveRoutine != null)
                StopCoroutine(saveRoutine);
            StopRegen();
            Save();
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private IEnumerator SaveLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(10f);
                Save();
            }
        }

        private void Save()
        {
            save.SaveXp(xp);
            save.SaveCurrentHp(currentHp);
        }

        private void StartRegen()
        {
            if (regenRoutine == null)
                regenRoutine = StartCoroutine(Regenerate());
        }

        private void StopRegen()
        {
            if (regenRoutine != null)
            {
                StopCoroutine(regenRoutine);
                regenRoutine = null;
            }
        }

        private IEnumerator Regenerate()
        {
            var wait = new WaitForSeconds(100f);
            while (true)
            {
                yield return wait;
                Heal(1);
            }
        }

        public void ApplyDamage(int amount)
        {
            if (amount <= 0)
                return;
            currentHp = Mathf.Max(0, currentHp - amount);
            OnHealthChanged?.Invoke(currentHp, MaxHp);
        }

        public void Heal(int amount)
        {
            if (amount <= 0)
                return;
            currentHp = Mathf.Min(MaxHp, currentHp + amount);
            OnHealthChanged?.Invoke(currentHp, MaxHp);
        }

        public void GainHitpointsXP(float amount)
        {
            if (amount <= 0f || xpTable == null)
                return;
            xp += amount;
            int newLevel = xpTable.GetLevel(Mathf.FloorToInt(xp));
            if (newLevel > level)
            {
                level = newLevel;
                if (currentHp > MaxHp)
                {
                    currentHp = MaxHp;
                    OnHealthChanged?.Invoke(currentHp, MaxHp);
                }
                OnHitpointsLevelChanged?.Invoke(level);
                OnHealthChanged?.Invoke(currentHp, MaxHp);
            }
        }

        public void OnPlayerDealtDamage(int damage)
        {
            GainHitpointsXP(damage * 1.33f);
        }

        public void OnEnemyDealtDamage(int damage)
        {
            ApplyDamage(damage);
        }

        /// <summary>
        /// Debug helper to directly set the hitpoints level. Adjusts XP and
        /// clamps the current HP to the new maximum.
        /// </summary>
        public void DebugSetLevel(int newLevel)
        {
            if (xpTable == null)
                return;

            newLevel = Mathf.Clamp(newLevel, 1, 99);
            xp = xpTable.GetXpForLevel(newLevel);
            level = newLevel;
            if (currentHp > MaxHp)
                currentHp = MaxHp;
            OnHitpointsLevelChanged?.Invoke(level);
            OnHealthChanged?.Invoke(currentHp, MaxHp);
        }

#if UNITY_EDITOR
        public void DebugDealDamage(int dmg)
        {
            OnPlayerDealtDamage(dmg);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.J))
                ApplyDamage(1);
            if (Input.GetKeyDown(KeyCode.H))
                Heal(1);
        }
#endif
    }

    public interface IHitpointsSave
    {
        float LoadXp();
        int LoadCurrentHp();
        void SaveXp(float xp);
        void SaveCurrentHp(int hp);
    }

    public class SaveManagerHitpointsSave : IHitpointsSave
    {
        private const string XpKey = "hitpoints_xp";
        private const string HpKey = "hitpoints_hp";

        public float LoadXp()
        {
            return SaveManager.Load<float>(XpKey);
        }

        public int LoadCurrentHp()
        {
            return SaveManager.Load<int>(HpKey);
        }

        public void SaveXp(float xp)
        {
            SaveManager.Save(XpKey, xp);
        }

        public void SaveCurrentHp(int hp)
        {
            SaveManager.Save(HpKey, hp);
        }
    }
}
