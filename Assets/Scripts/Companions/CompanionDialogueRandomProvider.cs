using System;

namespace Companions
{
    /// <summary>
    /// Provides a thread-safe RNG source for companion dialogue libraries so their sampling
    /// behaviour stays consistent across different skills.
    /// </summary>
    internal static class CompanionDialogueRandomProvider
    {
        /// <summary>
        /// Shared <see cref="System.Random"/> instance used for deterministic dialogue sampling.
        /// </summary>
        private static readonly Random RandomSource = new Random();

        /// <summary>
        /// Lock used to guard access to <see cref="RandomSource"/> because <see cref="Random"/> is not thread-safe.
        /// </summary>
        private static readonly object RandomLock = new object();

        /// <summary>
        /// Samples an index within the provided exclusive upper bound using the shared RNG.
        /// </summary>
        /// <param name="exclusiveUpperBound">Upper bound (exclusive) for the sampled index.</param>
        /// <returns>An index in the range [0, exclusiveUpperBound).</returns>
        public static int SampleIndex(int exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 0)
                return 0;

            lock (RandomLock)
            {
                return RandomSource.Next(exclusiveUpperBound);
            }
        }
    }
}
