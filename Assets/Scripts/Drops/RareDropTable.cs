using System.Collections.Generic;
using UnityEngine;
using Inventory;

namespace MyGame.Drops
{
    /// <summary>
    /// Shared rare drop table asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Drops/Rare Drop Table")]
    public class RareDropTable : ScriptableObject
    {
        /// <summary>Name used for logging.</summary>
        public string tableName;

        /// <summary>Weighted entries of the rare table.</summary>
        public List<WeightedDropEntry> entries = new List<WeightedDropEntry>();
    }
}
