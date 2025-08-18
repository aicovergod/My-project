using System.Collections.Generic;
using UnityEngine;
using Inventory;

namespace MyGame.Drops
{
    /// <summary>
    /// Scriptable object defining an NPC's drop behaviour.
    /// </summary>
    [CreateAssetMenu(menuName = "Drops/Drop Table")]
    public class DropTable : ScriptableObject
    {
        /// <summary>Name used for logging.</summary>
        public string tableName;

        /// <summary>If true, main table rolls are skipped when a unique drops.</summary>
        public bool stopOnUnique;

        /// <summary>Number of times to roll the main table per kill.</summary>
        public int rollsPerKill = 1;

        /// <summary>List of unique drops.</summary>
        public List<UniqueDropEntry> uniques = new List<UniqueDropEntry>();

        /// <summary>Main weighted table, may include an RDT placeholder.</summary>
        public List<WeightedDropEntry> mainTable = new List<WeightedDropEntry>();

        /// <summary>Tertiary 1/N drops.</summary>
        public List<TertiaryDropEntry> tertiaries = new List<TertiaryDropEntry>();

        /// <summary>Global toggle for whether weights are affected by luck.</summary>
        public bool mainAffectedByLuck = true;

        /// <summary>Shared rare drop table used when the placeholder is rolled.</summary>
        public RareDropTable rareDropTable;

        /// <summary>Placeholder entry representing the rare drop table.</summary>
        public WeightedDropEntry rdtPlaceholder = new RareDropPlaceholder();
    }
}
