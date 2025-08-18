using System;
using UnityEngine;

namespace MyGame.Drops
{
    /// <summary>
    /// Categories used when resolving drops. Mainly for editor tooling or logging.
    /// </summary>
    public enum DropCategory
    {
        Unique,
        Main,
        Tertiary,
        RareDropTable
    }

    /// <summary>
    /// Represents an inclusive integer range.
    /// </summary>
    [Serializable]
    public struct IntRange
    {
        /// <summary>Minimum inclusive value.</summary>
        public int Min;

        /// <summary>Maximum inclusive value.</summary>
        public int Max;

        /// <summary>
        /// Returns a random value within the range using the provided RNG.
        /// </summary>
        /// <param name="random">Optional random number generator. Uses <see cref="UnityEngine.Random"/> when null.</param>
        /// <returns>Inclusive random integer.</returns>
        public int Get(System.Random random = null)
        {
            if (Min >= Max)
            {
                return Min;
            }

            if (random != null)
            {
                return random.Next(Min, Max + 1);
            }

            return UnityEngine.Random.Range(Min, Max + 1);
        }
    }
}
