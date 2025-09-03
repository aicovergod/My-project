using System;
using UnityEngine;

namespace Fishing.Bycatch
{
    [Serializable]
    public struct BycatchCategoryWeights
    {
        [Range(0f, 1f)] public float common;
        [Range(0f, 1f)] public float uncommon;
        [Range(0f, 1f)] public float rare;
        [Range(0f, 1f)] public float ultra;

        public float luckBonusPerPoint;
        public int pityAfterRolls;
        public float pityRareStep;
        public float pityUltraStep;

        public void Normalize()
        {
            float sum = common + uncommon + rare + ultra;
            if (sum <= 0f)
            {
                common = 1f;
                uncommon = rare = ultra = 0f;
                return;
            }
            common /= sum;
            uncommon /= sum;
            rare /= sum;
            ultra /= sum;
        }

        public static BycatchCategoryWeights Default => new BycatchCategoryWeights
        {
            common = 0.70f,
            uncommon = 0.20f,
            rare = 0.09f,
            ultra = 0.01f,
            luckBonusPerPoint = 0.01f,
            pityAfterRolls = 50,
            pityRareStep = 0.005f,
            pityUltraStep = 0.001f
        };
    }
}
