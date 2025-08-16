using UnityEngine;

namespace Skills.Mining
{
    [CreateAssetMenu(menuName = "Skills/Mining/Ore Definition")]
    public class OreDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Requirements")]
        [SerializeField] private int levelRequirement = 1;

        [Header("Rewards")]
        [SerializeField] private int xpPerOre = 1;

        public string Id => id;
        public string DisplayName => displayName;
        public int LevelRequirement => levelRequirement;
        public int XpPerOre => xpPerOre;
    }
}
