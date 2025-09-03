using UnityEngine;

namespace Skills.Fishing
{
    [CreateAssetMenu(menuName = "Skills/Fishing/Bycatch Table", fileName = "BycatchTable")]
    public class BycatchTable : ScriptableObject
    {
        public BycatchCategoryWeights categories = BycatchCategoryWeights.Default;
        public float withBaitMul = 1.0f;
        public float withoutBaitMul = 0.90f;
        public float correctToolMul = 1.10f;

        [System.Serializable]
        public class Entry
        {
            public BycatchItemDefinition item;
            [Min(0f)] public float baseWeight = 1f;
            public int minLevelOverride;
            public int maxLevelOverride;
        }

        public Entry[] entries = System.Array.Empty<Entry>();
    }
}
