using UnityEngine;

namespace Skills.Woodcutting
{
    [CreateAssetMenu(menuName = "Skills/Woodcutting/Axe Definition")]
    public class AxeDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        [Header("Requirements")]
        [SerializeField] private int requiredWoodcuttingLevel = 1;

        [Header("Stats")]
        [SerializeField] private float swingSpeedMultiplier = 1f;
        [SerializeField] private int power = 0;

        [Header("Visuals")]
        [SerializeField] private Sprite icon;

        public string Id => id;
        public string DisplayName => displayName;
        public int RequiredWoodcuttingLevel => requiredWoodcuttingLevel;
        public float SwingSpeedMultiplier => swingSpeedMultiplier;
        public int Power => power;
        public Sprite Icon => icon;
    }
}
