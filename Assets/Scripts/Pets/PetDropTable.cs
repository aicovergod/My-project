using System.Collections.Generic;
using UnityEngine;

namespace Pets
{
    /// <summary>
    /// Scriptable list of potential pet drops.
    /// </summary>
    [CreateAssetMenu(menuName = "Pets/Pet Drop Table")]
    public class PetDropTable : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public PetDefinition pet;
            [Tooltip("Chance denominator (1 in N) for the drop.")]
            public int oneInN = 100;
            [Tooltip("Source identifier such as \"mining\" or \"woodcutting\".")]
            public string sourceId = "";
        }

        public List<Entry> entries = new();
    }
}