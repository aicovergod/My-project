using System.Collections.Generic;
using UnityEngine;

namespace Skills.Thieving.Data
{
    /// <summary>
    ///     ScriptableObject describing an interactable thieving object such as a stall or chest. Stores the level gate,
    ///     XP, loot table and respawn timings so runtime components can execute OSRS-style theft behaviour.
    /// </summary>
    [CreateAssetMenu(menuName = "Skills/Thieving/Object Definition", fileName = "NewThievingObjectDefinition")]
    public class ThievingObjectDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Tooltip("Unique identifier used for database lookups.")]
        private string id = string.Empty;

        [SerializeField, Tooltip("Display name shown in UI popups.")]
        private string displayName = string.Empty;

        [Header("Requirements")]
        [SerializeField, Tooltip("Thieving level required to steal from the object.")]
        private int requiredLevel = 1;

        [SerializeField, Tooltip("Base XP awarded when the theft succeeds.")]
        private float baseXp = 10f;

        [Header("Loot")]
        [SerializeField, Tooltip("Guaranteed coin payout range (set both values to 0 to disable coins).")]
        private Vector2Int coinRange = new Vector2Int(0, 0);

        [SerializeField, Tooltip("Weighted loot table used for additional rewards.")]
        private List<ThievingLootTableEntry> lootTable = new List<ThievingLootTableEntry>();

        [SerializeField, Tooltip("Base number of loot rolls performed per theft.")]
        private int baseLootRolls = 1;

        [SerializeField, Tooltip("Bonus loot rolls applied when the player wears the full Rogue outfit.")]
        private int rogueOutfitBonusRolls = 0;

        [SerializeField, Tooltip("1-in-N chance to roll the Rocky pet when stealing from this object.")]
        private int petRollDenominator = 0;

        [Header("Timing")]
        [SerializeField, Tooltip("Number of ticks required to complete the theft interaction.")]
        private int interactionTicks = 3;

        [SerializeField, Tooltip("Number of ticks the object remains depleted after a successful theft.")]
        private int depletionTicks = 2;

        [SerializeField, Tooltip("Additional ticks before the object respawns once the depletion timer ends.")]
        private int respawnTicks = 4;

        /// <summary>
        ///     Unique identifier used by the runtime database.
        /// </summary>
        public string Id => id;

        /// <summary>
        ///     Display name for UI feedback.
        /// </summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        /// <summary>
        ///     Level required to interact with the object.
        /// </summary>
        public int RequiredLevel => Mathf.Max(1, requiredLevel);

        /// <summary>
        ///     Base XP granted on success.
        /// </summary>
        public float BaseXp => Mathf.Max(0f, baseXp);

        /// <summary>
        ///     Guaranteed coin payout range.
        /// </summary>
        public Vector2Int CoinRange => coinRange;

        /// <summary>
        ///     Weighted loot entries resolved on success.
        /// </summary>
        public IReadOnlyList<ThievingLootTableEntry> LootTable => lootTable;

        /// <summary>
        ///     Base number of loot rolls executed each theft.
        /// </summary>
        public int BaseLootRolls => Mathf.Max(1, baseLootRolls);

        /// <summary>
        ///     Additional loot rolls granted with the Rogue outfit bonus active.
        /// </summary>
        public int RogueOutfitBonusRolls => Mathf.Max(0, rogueOutfitBonusRolls);

        /// <summary>
        ///     1-in-N pet roll chance (0 disables the roll).
        /// </summary>
        public int PetRollDenominator => Mathf.Max(0, petRollDenominator);

        /// <summary>
        ///     Number of ticks required to perform the theft.
        /// </summary>
        public int InteractionTicks => Mathf.Max(1, interactionTicks);

        /// <summary>
        ///     Number of ticks the object remains unavailable after theft.
        /// </summary>
        public int DepletionTicks => Mathf.Max(0, depletionTicks);

        /// <summary>
        ///     Additional ticks before the object becomes available once depletion ends.
        /// </summary>
        public int RespawnTicks => Mathf.Max(0, respawnTicks);
    }
}
