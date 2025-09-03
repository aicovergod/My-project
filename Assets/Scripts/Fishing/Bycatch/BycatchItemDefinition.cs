using UnityEngine;

namespace Fishing.Bycatch
{
    [CreateAssetMenu(menuName = "Fishing/Bycatch Item", fileName = "BycatchItem")]
    public class BycatchItemDefinition : ScriptableObject
    {
        [Header("Identification")]
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;

        [Header("Category")]
        [SerializeField] private Rarity rarity;
        [SerializeField] private float baseWeight = 1f;
        [EnumFlags]
        [SerializeField] private WaterType allowedWaterTypes = WaterType.Any;

        [Header("Requirements")]
        [SerializeField] private int minFishingLevel = 1;
        [SerializeField] private int maxFishingLevel = 99;
        [SerializeField] private bool requiresBait = false;
        [SerializeField] private FishingTool requiredTool = FishingTool.Any;

        [Header("Tuning")]
        [SerializeField] private AnimationCurve levelWeightCurve = AnimationCurve.Linear(1, 1, 99, 1);
        [SerializeField] private Vector2Int stackRange = new Vector2Int(1, 1);
        [SerializeField] private int baseGoldValue = 0;

        [Header("Visuals")]
        [SerializeField] private Sprite icon;
        [SerializeField] private GameObject worldPrefab;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public Rarity Rarity => rarity;
        public float BaseWeight => baseWeight;
        public WaterType AllowedWaterTypes => allowedWaterTypes;
        public int MinFishingLevel => minFishingLevel;
        public int MaxFishingLevel => maxFishingLevel;
        public bool RequiresBait => requiresBait;
        public FishingTool RequiredTool => requiredTool;
        public AnimationCurve LevelWeightCurve => levelWeightCurve;
        public Vector2Int StackRange => stackRange;
        public int BaseGoldValue => baseGoldValue;
        public Sprite Icon => icon;
        public GameObject WorldPrefab => worldPrefab;
    }
}
