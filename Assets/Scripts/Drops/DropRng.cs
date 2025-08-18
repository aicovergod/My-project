using System.Collections.Generic;
using UnityEngine;

namespace MyGame.Drops
{
    /// <summary>
    /// Random helper utilities for drop calculations.
    /// </summary>
    public static class DropRng
    {
        /// <summary>
        /// Performs a one-in-N roll, taking luck into account.
        /// </summary>
        /// <param name="denominator">The base denominator.</param>
        /// <param name="luckMultiplier">Luck multiplier (&gt;= 1).</param>
        /// <returns>True if the roll succeeds.</returns>
        public static bool RollOneIn(int denominator, float luckMultiplier = 1f)
        {
            if (denominator <= 1)
            {
                return true;
            }

            int effective = Mathf.Max(1, Mathf.RoundToInt(denominator / Mathf.Max(1f, luckMultiplier)));
            return UnityEngine.Random.Range(0, effective) == 0;
        }

        /// <summary>
        /// Picks an index from a weighted list. Weights &lt;= 0 are ignored.
        /// </summary>
        /// <param name="weights">Weights for each entry.</param>
        /// <param name="luckMultiplier">Luck multiplier.</param>
        /// <param name="affectedFlags">Optional flags indicating which weights are affected by luck.</param>
        /// <returns>Index of the chosen entry, or -1 if selection failed.</returns>
        public static int WeightedPickIndex(IReadOnlyList<int> weights, float luckMultiplier = 1f, IReadOnlyList<bool> affectedFlags = null)
        {
            if (weights == null || weights.Count == 0)
            {
                return -1;
            }

            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                int w = Mathf.Max(0, weights[i]);
                float factor = (affectedFlags != null && affectedFlags.Count > i && affectedFlags[i]) ? Mathf.Max(1f, luckMultiplier) : 1f;
                total += w * factor;
            }

            if (total <= 0f)
            {
                return -1;
            }

            float roll = UnityEngine.Random.value * total;
            float cumulative = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                int w = Mathf.Max(0, weights[i]);
                float factor = (affectedFlags != null && affectedFlags.Count > i && affectedFlags[i]) ? Mathf.Max(1f, luckMultiplier) : 1f;
                cumulative += w * factor;
                if (roll < cumulative)
                {
                    return i;
                }
            }

            return weights.Count - 1;
        }

        /// <summary>
        /// Returns a random integer in the inclusive range.
        /// </summary>
        public static int RangeInclusive(int min, int max)
        {
            if (min >= max)
            {
                return min;
            }

            return UnityEngine.Random.Range(min, max + 1);
        }
    }
}
