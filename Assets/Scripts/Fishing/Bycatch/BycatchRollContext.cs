namespace Fishing.Bycatch
{
    public struct BycatchRollContext
    {
        public int playerLevel;
        public bool hasBait;
        public WaterType waterType;
        public FishingTool tool;
        public float luck;
        public float spotRarityMultiplier;
        public int noRareStreakForThisWater;
        public int playerIdHash;
        public int nodeHash;
        public int rollIndex;
    }
}
