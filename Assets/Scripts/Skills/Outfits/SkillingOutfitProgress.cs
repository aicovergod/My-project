using System.Collections.Generic;
using System.Linq;
using Core.Save;

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

        public readonly string[] allPieceIds;
        public HashSet<string> owned;
        public readonly string saveKey;

        public SkillingOutfitProgress(string[] allPieceIds, string saveKey)
        {
            this.allPieceIds = allPieceIds;
            this.saveKey = saveKey;
            owned = new HashSet<string>();
            SaveManager.Register(this);
        }

        public void Load()
        {
            var saved = SaveManager.Load<string[]>(saveKey);
            owned = saved != null ? new HashSet<string>(saved) : new HashSet<string>();
        }

        public void Save()
        {
            SaveManager.Save(saveKey, owned.ToArray());
        }
    }
}
