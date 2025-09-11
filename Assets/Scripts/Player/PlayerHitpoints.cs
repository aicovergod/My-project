using System.Collections;
using UnityEngine;
using Skills;
using Core.Save;

namespace Player
{
    /// <summary>
    /// Handles hitpoints skill, HP logic and XP integration.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHitpoints : MonoBehaviour, ISaveable
    {
        [SerializeField] private bool passiveRegenEnabled;
        [SerializeField] private MonoBehaviour saveProvider; // optional custom save provider

        private IHitpointsSave save;
        private SkillManager skills;
        private int currentHp;

        private Coroutine regenRoutine;

        public event System.Action<int, int> OnHealthChanged; // current, max
        public event System.Action<int> OnHitpointsLevelChanged; // new level

        public int Level => skills != null ? skills.GetLevel(SkillType.Hitpoints) : 1;
        public float Xp => skills != null ? skills.GetXp(SkillType.Hitpoints) : 0f;
        public int CurrentHp => currentHp;
        public int MaxHp => Level;
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
            skills = GetComponent<SkillManager>();
            if (skills != null && skills.GetXp(SkillType.Hitpoints) <= 0f)
                skills.DebugSetLevel(SkillType.Hitpoints, 10);
        }

        private void OnEnable()
        {
            SaveManager.Register(this);
            if (passiveRegenEnabled)
                StartRegen();
        }

        private void OnDisable()
        {
            StopRegen();
            Save();
            SaveManager.Unregister(this);
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        public void Save()
        {
            save.SaveCurrentHp(currentHp);
        }

        public void Load()
        {
            currentHp = save.LoadCurrentHp();
            int level = Level;
            if (currentHp <= 0)
                currentHp = level;
            currentHp = Mathf.Clamp(currentHp, 0, level);
            OnHealthChanged?.Invoke(currentHp, MaxHp);
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
            if (amount <= 0f || skills == null)
                return;
            int oldLevel = skills.GetLevel(SkillType.Hitpoints);
            int newLevel = skills.AddXP(SkillType.Hitpoints, amount);
            if (newLevel > oldLevel)
            {
                if (currentHp > MaxHp)
                {
                    currentHp = MaxHp;
                    OnHealthChanged?.Invoke(currentHp, MaxHp);
                }
                OnHitpointsLevelChanged?.Invoke(newLevel);
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
        /// Debug helper to directly set the hitpoints level via the SkillManager
        /// and clamp current HP to the new maximum.
        /// </summary>
        public void DebugSetLevel(int newLevel)
        {
            skills?.DebugSetLevel(SkillType.Hitpoints, Mathf.Clamp(newLevel, 1, 99));
            if (currentHp > MaxHp)
                currentHp = MaxHp;
            OnHitpointsLevelChanged?.Invoke(Level);
            OnHealthChanged?.Invoke(currentHp, MaxHp);
        }

        /// <summary>
        /// Debug helper to directly set the current hitpoints. Optionally bypasses
        /// the max hitpoint clamp, allowing values such as 99999 for godmode.
        /// </summary>
        public void DebugSetCurrentHp(int hp, bool clampToMax = true)
        {
            currentHp = clampToMax ? Mathf.Clamp(hp, 0, MaxHp) : Mathf.Max(0, hp);
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
        int LoadCurrentHp();
        void SaveCurrentHp(int hp);
    }

    public class SaveManagerHitpointsSave : IHitpointsSave
    {
        private const string HpKey = "hitpoints_hp";

        public int LoadCurrentHp()
        {
            return SaveManager.Load<int>(HpKey);
        }

        public void SaveCurrentHp(int hp)
        {
            SaveManager.Save(HpKey, hp);
        }
    }
}
