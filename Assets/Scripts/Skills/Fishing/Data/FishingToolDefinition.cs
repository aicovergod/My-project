using UnityEngine;

namespace Skills.Fishing
{
    [CreateAssetMenu(menuName = "Skills/Fishing/Tool Definition")]
    public class FishingToolDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Requirements")]
        [SerializeField] private int requiredLevel = 1;

        [Header("Stats")]
        [SerializeField] private float swingSpeedMultiplier = 1f;
        [SerializeField] private int catchBonus = 0;

        [Header("Visuals")]
        [SerializeField] private Sprite icon;

        public string Id => id;
        public string DisplayName => displayName;
        public int RequiredLevel => requiredLevel;
        public float SwingSpeedMultiplier => swingSpeedMultiplier;
        public int CatchBonus => catchBonus;
        public Sprite Icon => icon;
    }
}
