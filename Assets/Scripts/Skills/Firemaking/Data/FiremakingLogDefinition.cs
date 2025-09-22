using UnityEngine;

namespace Skills.Firemaking
{
    [CreateAssetMenu(menuName = "Skills/Firemaking/Log Definition", fileName = "NewFiremakingLog")]
    public sealed class FiremakingLogDefinition : ScriptableObject
    {
        [Header("Inventory")]
        [Tooltip("Item id of the log that this definition represents.")]
        public string logItemId;
        [Tooltip("Human-readable name shown in floating text.")]
        public string displayName;

        [Header("Requirements & Rewards")]
        [Tooltip("Firemaking level required to attempt lighting this log.")]
        public int requiredLevel = 1;
        [Tooltip("XP granted when the log successfully burns.")]
        public float xp = 0f;
        [Tooltip("Default 1-in-N pet roll used when this log lights. Leave <= 0 to skip the roll.")]
        public int phoenixPetRoll = 5000;

        [Header("Ignition Behaviour")]
        [Tooltip("Number of OSRS ticks required for one lighting attempt at the required level.")]
        public int baseIgnitionTicks = 4;
        [Tooltip("How many ticks are removed per level above the requirement. Clamped so at least one tick remains.")]
        public float ignitionTickReductionPerLevel = 0.1f;
        [Range(0f, 1f)]
        [Tooltip("Chance to succeed at the minimum required level.")]
        public float baseIgnitionChance = 0.35f;
        [Tooltip("Additional success chance per Firemaking level above the requirement.")]
        public float ignitionChancePerLevel = 0.01f;
        [Range(0f, 1f)]
        [Tooltip("Flat success chance bonus when adding the log to an existing bonfire.")]
        public float bonfireIgnitionBonus = 0.15f;

        [Header("Fire Lifetime")]
        [Tooltip("Lifetime in ticks when this log is the first fuel in the fire.")]
        public int baseFireLifetimeTicks = 50;
        [Tooltip("Extra ticks granted when feeding an existing fire.")]
        public int bonusLifetimeWhenAdding = 25;
        [Tooltip("Hard cap for a fire's lifetime when repeatedly fed. Set to <= 0 to allow unlimited stacking.")]
        public int maxLifetimeTicks = 150;

        [Header("Loot & Effects")]
        [Tooltip("Optional item id to drop as ashes when the fire expires.")]
        public string ashesItemId;
        [Tooltip("Prefab spawned when this log creates a new fire. Falls back to the skill's default prefab when null.")]
        public GameObject firePrefab;
        [Tooltip("Sound played at the fire location when the log lights.")]
        public AudioClip igniteSound;
        [Tooltip("Sound played when the fire dies out.")]
        public AudioClip extinguishSound;

        /// <summary>
        /// Calculates the ignition duration in ticks after accounting for the player's level.
        /// </summary>
        public int GetIgnitionTicks(int playerLevel)
        {
            if (playerLevel <= requiredLevel)
                return Mathf.Max(1, baseIgnitionTicks);
            float reduction = (playerLevel - requiredLevel) * ignitionTickReductionPerLevel;
            return Mathf.Max(1, Mathf.RoundToInt(baseIgnitionTicks - reduction));
        }

        /// <summary>
        /// Returns the chance (0..1) that the current attempt succeeds.
        /// </summary>
        public float GetSuccessChance(int playerLevel, bool addingToExistingFire)
        {
            float chance = baseIgnitionChance;
            if (playerLevel > requiredLevel)
                chance += (playerLevel - requiredLevel) * ignitionChancePerLevel;
            if (addingToExistingFire)
                chance += bonfireIgnitionBonus;
            return Mathf.Clamp01(chance);
        }

        /// <summary>
        /// Calculates how many ticks the fire should gain if this log succeeds.
        /// </summary>
        public int GetLifetimeContribution(bool addingToExistingFire)
        {
            return addingToExistingFire
                ? Mathf.Max(0, bonusLifetimeWhenAdding)
                : Mathf.Max(0, baseFireLifetimeTicks);
        }
    }
}
