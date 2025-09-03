using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fishing.Bycatch
{
    public class BycatchManager : MonoBehaviour
    {
        public BycatchTable bycatchTable;
        public bool useDailySeed = true;

        private readonly Dictionary<WaterType, int> _noRareStreak = new();
        private readonly Dictionary<WaterType, int> _lastSeedDay = new();

        public int GetStreak(WaterType wt)
        {
            int today = DateTime.UtcNow.Date.GetHashCode();
            if (!_lastSeedDay.TryGetValue(wt, out int last) || last != today)
            {
                _lastSeedDay[wt] = today;
                _noRareStreak[wt] = 0;
            }
            return _noRareStreak.TryGetValue(wt, out int streak) ? streak : 0;
        }

        public void ApplyStreakResult(WaterType wt, BycatchResult res)
        {
            int today = DateTime.UtcNow.Date.GetHashCode();
            _lastSeedDay[wt] = today;
            bool isRare = !res.IsNone && (res.Rarity == Rarity.Rare || res.Rarity == Rarity.UltraRare);
            if (isRare)
                _noRareStreak[wt] = 0;
            else
                _noRareStreak[wt] = GetStreak(wt) + 1;
        }

        public BycatchResult Roll(in BycatchRollContext ctx)
        {
            if (bycatchTable == null || bycatchTable.entries == null || bycatchTable.entries.Length == 0)
                return BycatchResult.None;

            var rng = CreateRng(ctx);

            float c = bycatchTable.categories.common;
            float u = bycatchTable.categories.uncommon;
            float r = bycatchTable.categories.rare;
            float ur = bycatchTable.categories.ultra;

            float luckBonus = ctx.luck * bycatchTable.categories.luckBonusPerPoint;
            u += luckBonus;
            r += luckBonus;
            ur += luckBonus;

            if (ctx.noRareStreakForThisWater >= bycatchTable.categories.pityAfterRolls)
            {
                int extra = ctx.noRareStreakForThisWater - bycatchTable.categories.pityAfterRolls + 1;
                r += extra * bycatchTable.categories.pityRareStep;
                ur += extra * bycatchTable.categories.pityUltraStep;
            }

            float sumCat = c + u + r + ur;
            if (sumCat <= 0f)
                return BycatchResult.None;
            c /= sumCat;
            u /= sumCat;
            r /= sumCat;
            ur /= sumCat;

            double roll = rng.NextDouble();
            Rarity cat;
            if (roll < c)
                cat = Rarity.Common;
            else if (roll < c + u)
                cat = Rarity.Uncommon;
            else if (roll < c + u + r)
                cat = Rarity.Rare;
            else
                cat = Rarity.UltraRare;

            var eligible = new List<(BycatchTable.Entry entry, float weight)>();
            foreach (var entry in bycatchTable.entries)
            {
                var item = entry.item;
                if (item == null || item.Rarity != cat)
                    continue;
                int minLvl = entry.minLevelOverride > 0 ? entry.minLevelOverride : item.MinFishingLevel;
                int maxLvl = entry.maxLevelOverride > 0 ? entry.maxLevelOverride : item.MaxFishingLevel;
                if (ctx.playerLevel < minLvl || ctx.playerLevel > maxLvl)
                    continue;
                if ((item.AllowedWaterTypes & ctx.waterType) == 0)
                    continue;
                if (item.RequiresBait && !ctx.hasBait)
                    continue;
                if (item.RequiredTool != FishingTool.Any && item.RequiredTool != ctx.tool)
                    continue;

                float weight = entry.baseWeight * item.BaseWeight;
                float levelMul = item.LevelWeightCurve != null ? item.LevelWeightCurve.Evaluate(ctx.playerLevel) : 1f;
                weight *= levelMul;
                weight *= ctx.hasBait ? bycatchTable.withBaitMul : bycatchTable.withoutBaitMul;
                if (item.RequiredTool != FishingTool.Any && item.RequiredTool == ctx.tool)
                    weight *= bycatchTable.correctToolMul;
                weight *= Mathf.Max(0.01f, ctx.spotRarityMultiplier);

                if (weight > 0f)
                    eligible.Add((entry, weight));
            }

            if (eligible.Count == 0)
                return BycatchResult.None;

            float totalWeight = 0f;
            foreach (var e in eligible) totalWeight += e.weight;
            double pick = rng.NextDouble() * totalWeight;
            foreach (var e in eligible)
            {
                if (pick <= e.weight)
                {
                    var item = e.entry.item;
                    int qty = rng.Next(item.StackRange.x, item.StackRange.y + 1);
                    return new BycatchResult(item, qty, item.Rarity);
                }
                pick -= e.weight;
            }

            return BycatchResult.None;
        }

        public float GetItemFinalChanceApprox(in BycatchRollContext ctx, BycatchItemDefinition target)
        {
            if (bycatchTable == null || target == null)
                return 0f;

            float c = bycatchTable.categories.common;
            float u = bycatchTable.categories.uncommon;
            float r = bycatchTable.categories.rare;
            float ur = bycatchTable.categories.ultra;

            float luckBonus = ctx.luck * bycatchTable.categories.luckBonusPerPoint;
            u += luckBonus;
            r += luckBonus;
            ur += luckBonus;

            if (ctx.noRareStreakForThisWater >= bycatchTable.categories.pityAfterRolls)
            {
                int extra = ctx.noRareStreakForThisWater - bycatchTable.categories.pityAfterRolls + 1;
                r += extra * bycatchTable.categories.pityRareStep;
                ur += extra * bycatchTable.categories.pityUltraStep;
            }

            float sumCat = c + u + r + ur;
            if (sumCat <= 0f)
                return 0f;
            c /= sumCat;
            u /= sumCat;
            r /= sumCat;
            ur /= sumCat;

            float categoryWeight = target.Rarity switch
            {
                Rarity.Common => c,
                Rarity.Uncommon => u,
                Rarity.Rare => r,
                Rarity.UltraRare => ur,
                _ => 0f
            };

            float total = 0f;
            float targetWeight = 0f;
            foreach (var entry in bycatchTable.entries)
            {
                var item = entry.item;
                if (item == null || item.Rarity != target.Rarity)
                    continue;
                int minLvl = entry.minLevelOverride > 0 ? entry.minLevelOverride : item.MinFishingLevel;
                int maxLvl = entry.maxLevelOverride > 0 ? entry.maxLevelOverride : item.MaxFishingLevel;
                if (ctx.playerLevel < minLvl || ctx.playerLevel > maxLvl)
                    continue;
                if ((item.AllowedWaterTypes & ctx.waterType) == 0)
                    continue;
                if (item.RequiresBait && !ctx.hasBait)
                    continue;
                if (item.RequiredTool != FishingTool.Any && item.RequiredTool != ctx.tool)
                    continue;

                float weight = entry.baseWeight * item.BaseWeight;
                float levelMul = item.LevelWeightCurve != null ? item.LevelWeightCurve.Evaluate(ctx.playerLevel) : 1f;
                weight *= levelMul;
                weight *= ctx.hasBait ? bycatchTable.withBaitMul : bycatchTable.withoutBaitMul;
                if (item.RequiredTool != FishingTool.Any && item.RequiredTool == ctx.tool)
                    weight *= bycatchTable.correctToolMul;
                weight *= Mathf.Max(0.01f, ctx.spotRarityMultiplier);

                if (weight <= 0f)
                    continue;
                total += weight;
                if (item == target)
                    targetWeight = weight;
            }

            if (targetWeight <= 0f || total <= 0f)
                return 0f;
            return categoryWeight * (targetWeight / total);
        }

        private System.Random CreateRng(in BycatchRollContext ctx)
        {
            if (!useDailySeed)
                return new System.Random();
            int seed = DateTime.UtcNow.Date.GetHashCode();
            seed = HashCombine(seed, ctx.playerIdHash);
            seed = HashCombine(seed, ctx.nodeHash);
            seed = HashCombine(seed, ctx.rollIndex);
            return new System.Random(seed);
        }

        private static int HashCombine(int a, int b)
        {
            unchecked
            {
                return (a * 397) ^ b;
            }
        }
    }
}
