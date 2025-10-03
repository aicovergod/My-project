using System;
using System.Collections.Generic;
using System.Linq;
using Core.Save;
using UnityEngine;

namespace Skills.Outfits
{
    /// <summary>
    /// Tracks owned outfit pieces for a skill and persists via the SaveManager.
    /// </summary>
    public class SkillingOutfitProgress : ISaveable
    {
        /// <summary>
        /// When enabled, skilling outfit roll attempts will be logged to the console.
        /// Controlled via the F2 debug menu.
        /// </summary>
        public static bool DebugChance { get; set; }

        private static readonly List<SkillingOutfitProgress> activeProgressTrackers = new List<SkillingOutfitProgress>();

        /// <summary>
        /// Provides read-only access to the currently active outfit progress trackers.
        /// Used by debug tooling to surface which outfits are registered at runtime.
        /// </summary>
        public static IReadOnlyList<SkillingOutfitProgress> ActiveProgressTrackers => activeProgressTrackers;

        private readonly SkillingOutfitDefinition definition;
        private readonly string[] allPieceIds;
        private readonly string saveKey;

        /// <summary>
        /// Tracks the set of item IDs representing outfit pieces the player already owns.
        /// </summary>
        public HashSet<string> owned;

        /// <summary>
        /// Definition backing this progress tracker. May be null when constructed without data.
        /// </summary>
        public SkillingOutfitDefinition Definition => definition;

        /// <summary>
        /// Ordered list of the unique outfit piece IDs the tracker manages.
        /// </summary>
        public IReadOnlyList<string> AllPieceIds => allPieceIds;

        /// <summary>
        /// Save key used when persisting progress.
        /// </summary>
        public string SaveKey => saveKey;

        /// <summary>
        /// Constructs a new progress tracker driven by the supplied outfit definition.
        /// </summary>
        /// <param name="definition">ScriptableObject describing the outfit.</param>
        public SkillingOutfitProgress(SkillingOutfitDefinition definition)
        {
            this.definition = definition;
            owned = new HashSet<string>(StringComparer.Ordinal);

            if (definition == null)
            {
                allPieceIds = Array.Empty<string>();
                saveKey = string.Empty;
                Debug.LogWarning("SkillingOutfitProgress constructed without a definition. Outfit rolls will be disabled.");
            }
            else
            {
                saveKey = definition.SaveKey != null ? definition.SaveKey.Trim() : string.Empty;
                if (string.IsNullOrEmpty(saveKey))
                    Debug.LogWarning($"SkillingOutfitDefinition '{definition.name}' is missing a save key. Progress will not persist.");

                allPieceIds = definition.PieceItemIds != null
                    ? definition.PieceItemIds
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => id.Trim())
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                    : Array.Empty<string>();
            }

            RegisterTracker(this);
            SaveManager.Register(this);
        }

        /// <summary>
        /// Loads owned outfit pieces from the save system.
        /// </summary>
        public void Load()
        {
            if (owned == null)
                owned = new HashSet<string>(StringComparer.Ordinal);

            if (string.IsNullOrEmpty(saveKey))
            {
                owned.Clear();
                return;
            }

            var saved = SaveManager.Load<string[]>(saveKey);
            owned = saved != null
                ? new HashSet<string>(
                    saved.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                    StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Persists the owned outfit pieces via the save system.
        /// </summary>
        public void Save()
        {
            if (string.IsNullOrEmpty(saveKey) || owned == null)
                return;

            var sanitized = owned
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            SaveManager.Save(saveKey, sanitized);
        }

        /// <summary>
        /// Removes the supplied tracker from the save system and active registry.
        /// </summary>
        /// <param name="progress">Tracker that should be unregistered.</param>
        public static void Unregister(SkillingOutfitProgress progress)
        {
            if (progress == null)
                return;

            SaveManager.Unregister(progress);
            activeProgressTrackers.Remove(progress);
        }

        private static void RegisterTracker(SkillingOutfitProgress progress)
        {
            if (progress == null || activeProgressTrackers.Contains(progress))
                return;

            activeProgressTrackers.Add(progress);
        }
    }
}
