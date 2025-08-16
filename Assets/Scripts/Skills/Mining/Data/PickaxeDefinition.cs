using UnityEngine;

namespace Skills.Mining
{
    [CreateAssetMenu(menuName = "Skills/Mining/Pickaxe Definition")]
    public class PickaxeDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Stats")]
        [SerializeField] private int tier = 1;
        [SerializeField] private int levelRequirement = 1;
        [SerializeField] private float miningRollBonus = 0f;
        [SerializeField] private int swingSpeedTicks = 5;

        public string Id => id;
        public string DisplayName => displayName;
        public int Tier => tier;
        public int LevelRequirement => levelRequirement;
        public float MiningRollBonus => miningRollBonus;
        public int SwingSpeedTicks => swingSpeedTicks;
    }
}
