using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skills.Thieving.Data
{
    /// <summary>
    ///     ScriptableObject that captures the configuration for a pickpocket target. Designers can author values that
    ///     mirror the Old School RuneScape wiki and runtime systems can query the definition for level gates, loot tables
    ///     and failure behaviour.
    /// </summary>
    [CreateAssetMenu(menuName = "Skills/Thieving/NPC Definition", fileName = "NewThievingNpcDefinition")]
    public class ThievingNpcDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Tooltip("Unique identifier used for lookups.")]
        private string id = string.Empty;

        [SerializeField, Tooltip("Display name surfaced in UI elements.")]
        private string displayName = string.Empty;

        [Header("Requirements")]
        [SerializeField, Tooltip("Thieving level required to attempt this pickpocket.")]
        private int requiredLevel = 1;

        [SerializeField, Tooltip("Base XP awarded for a successful pickpocket.")]
        private float baseXp = 8f;

        [Header("Success Thresholds")]
        [SerializeField, Range(0, 255), Tooltip("Success threshold at Thieving level 1 (0-255 roll).")]
        private int lowSuccessThreshold = 128;

        [SerializeField, Range(0, 255), Tooltip("Success threshold at Thieving level 99 (0-255 roll).")]
        private int highSuccessThreshold = 240;

        [SerializeField, Tooltip("Optional threshold gain applied per level beyond 99. Allows post-99 scaling.")]
        private int post99ThresholdGain = 0;

        [Header("Loot Configuration")]
        [SerializeField, Tooltip("Guaranteed coin payout range (min/max inclusive). Set x and y to 0 to disable coins.")]
        private Vector2Int coinRange = new Vector2Int(0, 0);

        [SerializeField, Tooltip("Additional loot entries rolled per successful pickpocket.")]
        private List<ThievingLootTableEntry> lootTable = new List<ThievingLootTableEntry>();

        [SerializeField, Tooltip("Base number of loot rolls performed when resolving the table.")]
        private int baseLootRolls = 1;

        [SerializeField, Tooltip("Bonus loot rolls granted when the player wears the full Rogue outfit.")]
        private int rogueOutfitBonusRolls = 0;

        [Header("Failure Behaviour")]
        [SerializeField, Tooltip("Damage dealt to the player when the pickpocket fails.")]
        private int damageOnFail = 1;

        [SerializeField, Tooltip("Number of ticks the player is stunned on failure (0.6s per tick).")]
        private int stunTicks = 4;

        [SerializeField, Tooltip("Cooldown applied to the NPC after repeated failures (in ticks).")]
        private int cooldownTicks = 12;

        [SerializeField, Tooltip("Number of consecutive failures required before triggering the cooldown.")]
        private int failuresBeforeCooldown = 3;

        [Header("Optional Pet Roll")]
        [SerializeField, Tooltip("1-in-N chance to roll the Rocky pet on success. 0 disables the roll.")]
        private int petRollDenominator = 0;

        [Header("Timing")]
        [SerializeField, Tooltip("Number of ticks required to perform a pickpocket attempt.")]
        private int interactionTicks = 2;

        /// <summary>
        ///     Unique definition identifier used for lookups.
        /// </summary>
        public string Id => id;

        /// <summary>
        ///     Display name shown in UI and floating text.
        /// </summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        /// <summary>
        ///     Minimum Thieving level required to interact with the NPC.
        /// </summary>
        public int RequiredLevel => Mathf.Max(1, requiredLevel);

        /// <summary>
        ///     Base XP granted on success.
        /// </summary>
        public float BaseXp => Mathf.Max(0f, baseXp);

        /// <summary>
        ///     Inclusive coin range rolled for guaranteed payouts.
        /// </summary>
        public Vector2Int CoinRange => coinRange;

        /// <summary>
        ///     Weighted loot table used for additional rewards.
        /// </summary>
        public IReadOnlyList<ThievingLootTableEntry> LootTable => lootTable;

        /// <summary>
        ///     Base number of loot rolls performed on success.
        /// </summary>
        public int BaseLootRolls => Mathf.Max(1, baseLootRolls);

        /// <summary>
        ///     Additional loot rolls granted when the Rogue outfit bonus is active.
        /// </summary>
        public int RogueOutfitBonusRolls => Mathf.Max(0, rogueOutfitBonusRolls);

        /// <summary>
        ///     Damage dealt to the player on failure.
        /// </summary>
        public int DamageOnFail => Mathf.Max(0, damageOnFail);

        /// <summary>
        ///     Duration of the stun applied on failure.
        /// </summary>
        public int StunTicks => Mathf.Max(0, stunTicks);

        /// <summary>
        ///     Cooldown duration (in ticks) applied after consecutive failures.
        /// </summary>
        public int CooldownTicks => Mathf.Max(0, cooldownTicks);

        /// <summary>
        ///     Number of consecutive failures required before applying the cooldown.
        /// </summary>
        public int FailuresBeforeCooldown => Mathf.Max(1, failuresBeforeCooldown);

        /// <summary>
        ///     1-in-N chance used when rolling the Rocky pet. 0 disables the roll entirely.
        /// </summary>
        public int PetRollDenominator => Mathf.Max(0, petRollDenominator);

        /// <summary>
        ///     Number of ticks required to complete a pickpocket attempt.
        /// </summary>
        public int InteractionTicks => Mathf.Max(1, interactionTicks);

        /// <summary>
        ///     Calculates the success threshold for the supplied thieving level. The roll uses a 0-255 space
        ///     to match RuneScape's pickpocket maths: the player's level is interpolated between the low/high
        ///     thresholds for levels 1-99 (as per https://oldschool.runescape.wiki/) and levels above 99 gain
        ///     additional threshold points capped at 255.
        /// </summary>
        /// <param name="thievingLevel">Current thieving level.</param>
        /// <returns>Threshold value clamped to the 0-255 range.</returns>
        public int GetSuccessThreshold(int thievingLevel)
        {
            int clampedLevel = Mathf.Clamp(thievingLevel, 1, 255);

            if (clampedLevel <= 1)
                return Mathf.Clamp(lowSuccessThreshold, 0, 255);

            if (clampedLevel >= 99)
            {
                int baseThreshold = Mathf.Clamp(highSuccessThreshold, 0, 255);
                int extraLevels = Mathf.Max(0, clampedLevel - 99);
                int extra = Mathf.Max(0, post99ThresholdGain) * extraLevels;
                return Mathf.Clamp(baseThreshold + extra, 0, 255);
            }

            float t = (clampedLevel - 1f) / 98f;
            float interpolated = Mathf.Lerp(lowSuccessThreshold, highSuccessThreshold, t);
            return Mathf.Clamp(Mathf.RoundToInt(interpolated), 0, 255);
        }
    }
}
