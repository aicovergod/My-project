using System;
using UnityEngine;
using Core.Save;

namespace Pets
{
    /// <summary>
    /// Tracks XP and level for an active pet and handles stat scaling/evolution.
    /// </summary>
    [DisallowMultipleComponent]
    public class PetExperience : MonoBehaviour
    {
        public const int MaxLevel = 99;

        public PetDefinition definition;

        [SerializeField] private float xp;
        [SerializeField] private int level = 1;

        private SpriteRenderer spriteRenderer;
        private float currentPpu;
        private float spritePpu = 64f;

        public event Action<int> OnLevelChanged;

        private static readonly int[] LevelXp = GenerateXpTable(MaxLevel);

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null && spriteRenderer.sprite != null)
                spritePpu = spriteRenderer.sprite.pixelsPerUnit;
            Load();
            UpdateEvolution();
        }

        private void OnDisable()
        {
            Save();
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private void Load()
        {
            if (definition == null) return;
            xp = SaveManager.Load<float>(XpKey(definition.id));
            level = GetLevel(xp);
        }

        private void Save()
        {
            if (definition == null) return;
            SaveManager.Save(XpKey(definition.id), xp);
        }

        private static string XpKey(string id) => $"petxp_{id}";

        public int Level => level;
        public float Xp => xp;
        public string TierName { get; private set; }

        public int GetXpToNextLevel()
        {
            if (level >= MaxLevel)
                return 0;
            return LevelXp[level] - Mathf.FloorToInt(xp);
        }

        public void AddXp(float amount)
        {
            if (amount <= 0f || level >= MaxLevel)
                return;
            xp += amount;
            int newLevel = GetLevel(xp);
            if (newLevel != level)
            {
                level = Mathf.Clamp(newLevel, 1, MaxLevel);
                OnLevelChanged?.Invoke(level);
                UpdateEvolution();
            }
        }

        private static int GetLevel(float xp)
        {
            int xpInt = Mathf.FloorToInt(xp);
            for (int i = LevelXp.Length - 1; i >= 0; i--)
            {
                if (xpInt >= LevelXp[i])
                    return i + 1;
            }
            return 1;
        }

        private static int[] GenerateXpTable(int maxLevel)
        {
            int[] xp = new int[maxLevel];
            int points = 0;
            for (int level = 1; level <= maxLevel; level++)
            {
                if (level == 1)
                {
                    xp[0] = 0;
                    continue;
                }
                points += Mathf.FloorToInt(level + 300f * Mathf.Pow(2f, level / 7f));
                xp[level - 1] = points / 4;
            }
            return xp;
        }

        private void UpdateEvolution()
        {
            if (definition == null)
                return;

            float ppu = definition.pixelsPerUnit;
            string name = string.Empty;
            if (definition.evolutionTiers != null)
            {
                foreach (var tier in definition.evolutionTiers)
                {
                    if (level >= tier.level)
                    {
                        ppu = tier.pixelsPerUnit;
                        if (!string.IsNullOrEmpty(tier.tierName))
                            name = tier.tierName;
                    }
                }
            }
            TierName = name;
            if (spriteRenderer == null)
                return;
            if (Mathf.Approximately(ppu, currentPpu))
                return;
            currentPpu = ppu;
            float scale = spritePpu / ppu;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        public static void AddPetXp(float xp)
        {
            if (xp <= 0f) return;
            var pet = PetDropSystem.ActivePetObject;
            if (pet == null) return;
            var exp = pet.GetComponent<PetExperience>();
            if (exp == null) return;
            exp.AddXp(xp);
        }

        public static float GetStatMultiplier(int level)
        {
            if (level >= 50) return 1f;
            if (level >= 25) return 0.75f;
            return 0.5f;
        }
    }
}

