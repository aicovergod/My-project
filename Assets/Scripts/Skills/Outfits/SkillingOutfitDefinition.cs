using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skills.Outfits
{
    /// <summary>
    /// Defines the serializable data for a skilling outfit so skills can share
    /// a single source of truth for the outfit piece IDs and persistence key.
    /// </summary>
    [CreateAssetMenu(menuName = "Skills/Outfits/Skilling Outfit Definition", fileName = "NewSkillingOutfitDefinition")]
    public class SkillingOutfitDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("Unique save key used to persist owned outfit pieces.")]
        private string saveKey = string.Empty;

        [SerializeField, Tooltip("Item IDs for every piece in the outfit set.")]
        private string[] pieceItemIds = Array.Empty<string>();

        [Header("Optional Metadata")]
        [SerializeField, Tooltip("Friendly display name used by debug tooling. Defaults to the asset name when empty.")]
        private string displayName = string.Empty;

        [SerializeField, Tooltip("Optional pet identifier that synergises with this outfit. Used for debug context only.")]
        private string associatedPetId = string.Empty;

        [SerializeField, TextArea, Tooltip("Optional description of any passive bonuses granted by the outfit.")]
        private string bonusDescription = string.Empty;

        /// <summary>
        /// Unique save key consumed by <see cref="Core.Save.SaveManager"/>.
        /// </summary>
        public string SaveKey => saveKey;

        /// <summary>
        /// Ordered list of outfit piece item IDs.
        /// </summary>
        public IReadOnlyList<string> PieceItemIds => pieceItemIds ?? Array.Empty<string>();

        /// <summary>
        /// Display name surfaced in debug tooling.
        /// </summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        /// <summary>
        /// Optional identifier for the pet associated with the outfit (if any).
        /// </summary>
        public string AssociatedPetId => associatedPetId;

        /// <summary>
        /// Optional description of passive bonuses provided by the outfit.
        /// </summary>
        public string BonusDescription => bonusDescription;
    }
}
