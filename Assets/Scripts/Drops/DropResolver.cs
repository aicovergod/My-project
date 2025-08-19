using System.Collections.Generic;
using Inventory;
using UnityEngine;

namespace MyGame.Drops
{
    /// <summary>
    /// Resolves drop tables into concrete items.
    /// </summary>
    public static class DropResolver
    {
        /// <summary>
        /// Resolves a drop table for a single kill.
        /// </summary>
        /// <param name="table">Table to resolve.</param>
        /// <param name="luckMultiplier">Luck multiplier (&gt;=1).</param>
        /// <param name="seedOverride">Optional deterministic seed.</param>
        public static List<ResolvedDrop> Resolve(DropTable table, float luckMultiplier, System.Random seedOverride = null)
        {
            var finalDrops = new List<ResolvedDrop>();
            if (table == null)
            {
                return finalDrops;
            }

            luckMultiplier = Mathf.Max(1f, luckMultiplier);
            System.Random rng = seedOverride;

            var working = new List<ResolvedDrop>();

            // Always-drop entries
            foreach (var entry in table.mainTable)
            {
                if (!entry.alwaysDrop || entry.item == null)
                {
                    continue;
                }

                int qty = GetQuantity(entry.qty, rng);
                working.Add(new ResolvedDrop(entry.item, qty));
            }

            // Uniques
            bool uniqueHit = false;
            foreach (var unique in table.uniques)
            {
                if (unique.denominator < 1)
                {
                    Debug.LogWarning($"DropTable {table.tableName}: unique denominator < 1 for {unique.item?.name}");
                    continue;
                }

                if (RollOneIn(unique.denominator, unique.affectedByLuck, luckMultiplier, rng))
                {
                    uniqueHit = true;
                    int qty = GetQuantity(unique.qty, rng);
                    working.Add(new ResolvedDrop(unique.item, qty));
                }
            }

            bool skipMain = uniqueHit && table.stopOnUnique;

            // Main table
            if (!skipMain)
            {
                var candidates = new List<WeightedDropEntry>();
                var weights = new List<int>();
                var flags = new List<bool>();

                foreach (var entry in table.mainTable)
                {
                    if (entry.alwaysDrop)
                    {
                        continue;
                    }

                    if (entry.weight <= 0)
                    {
                        Debug.LogWarning($"DropTable {table.tableName}: weight <= 0 for entry {entry.item?.name ?? "RDT"}");
                        continue;
                    }

                    candidates.Add(entry);
                    weights.Add(entry.weight);
                    flags.Add(table.mainAffectedByLuck && entry.affectedByLuck);
                }

                for (int r = 0; r < table.rollsPerKill; r++)
                {
                    int index = WeightedPickIndex(weights, flags, luckMultiplier, rng);
                    if (index < 0 || index >= candidates.Count)
                    {
                        continue;
                    }

                    var picked = candidates[index];
                    if (picked.item != null)
                    {
                        int qty = GetQuantity(picked.qty, rng);
                        working.Add(new ResolvedDrop(picked.item, qty));
                    }
                    else
                    {
                        if (table.rareDropTable == null)
                        {
                            Debug.LogWarning($"DropTable {table.tableName}: RDT placeholder hit but no RareDropTable assigned.");
                            continue;
                        }

                        var rare = RollRare(table.rareDropTable, luckMultiplier, rng);
                        if (rare != null && rare.item != null)
                        {
                            int qty = GetQuantity(rare.qty, rng);
                            working.Add(new ResolvedDrop(rare.item, qty));
                        }
                    }
                }
            }

            // Tertiaries
            foreach (var tertiary in table.tertiaries)
            {
                if (tertiary.denominator < 1)
                {
                    Debug.LogWarning($"DropTable {table.tableName}: tertiary denominator < 1 for {tertiary.item?.name}");
                    continue;
                }

                if (RollOneIn(tertiary.denominator, tertiary.affectedByLuck, luckMultiplier, rng))
                {
                    int qty = GetQuantity(tertiary.qty, rng);
                    working.Add(new ResolvedDrop(tertiary.item, qty));
                }
            }

            // Merge stacks
            var map = new Dictionary<ItemData, int>();
            foreach (var drop in working)
            {
                if (drop.item == null)
                {
                    continue;
                }

                if (map.TryGetValue(drop.item, out int existing))
                {
                    map[drop.item] = existing + drop.quantity;
                }
                else
                {
                    map.Add(drop.item, drop.quantity);
                }
            }

            foreach (var kv in map)
            {
                finalDrops.Add(new ResolvedDrop(kv.Key, kv.Value));
            }

            return finalDrops;
        }

        private static int GetQuantity(DropQuantity qty, System.Random rng)
        {
            if (qty == null)
            {
                return 0;
            }

            return qty.Get(rng);
        }

        private static bool RollOneIn(int denominator, bool affected, float luckMultiplier, System.Random rng)
        {
            float luck = affected ? luckMultiplier : 1f;
            if (rng != null)
            {
                int effective = Mathf.Max(1, Mathf.RoundToInt(denominator / Mathf.Max(1f, luck)));
                return rng.Next(effective) == 0;
            }

            return DropRng.RollOneIn(denominator, luck);
        }

        private static int WeightedPickIndex(IReadOnlyList<int> weights, IReadOnlyList<bool> affected, float luckMultiplier, System.Random rng)
        {
            if (rng == null)
            {
                return DropRng.WeightedPickIndex(weights, luckMultiplier, affected);
            }

            if (weights == null || weights.Count == 0)
            {
                return -1;
            }

            double total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                int w = Mathf.Max(0, weights[i]);
                float factor = (affected != null && affected.Count > i && affected[i]) ? luckMultiplier : 1f;
                total += w * factor;
            }

            if (total <= 0)
            {
                return -1;
            }

            double roll = rng.NextDouble() * total;
            double acc = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                int w = Mathf.Max(0, weights[i]);
                float factor = (affected != null && affected.Count > i && affected[i]) ? luckMultiplier : 1f;
                acc += w * factor;
                if (roll < acc)
                {
                    return i;
                }
            }

            return weights.Count - 1;
        }

        private static WeightedDropEntry RollRare(RareDropTable table, float luckMultiplier, System.Random rng)
        {
            if (table == null || table.entries == null || table.entries.Count == 0)
            {
                return null;
            }

            var weights = new List<int>(table.entries.Count);
            var flags = new List<bool>(table.entries.Count);
            foreach (var e in table.entries)
            {
                weights.Add(e.weight);
                flags.Add(e.affectedByLuck);
            }

            int index = WeightedPickIndex(weights, flags, luckMultiplier, rng);
            if (index < 0 || index >= table.entries.Count)
            {
                return null;
            }

            return table.entries[index];
        }
    }
}
