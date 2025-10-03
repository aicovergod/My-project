using System;
using UnityEngine;

namespace Skills.Common
{
    /// <summary>
    ///     Utility that tracks progress for tick-based actions and exposes
    ///     events so skills can react uniformly when an action advances or
    ///     completes.
    /// </summary>
    [Serializable]
    public sealed class TickProgressTracker
    {
        private int requiredTicks;
        private int progressTicks;

        /// <summary>
        ///     Raised whenever <see cref="Reset"/> is called so listeners can
        ///     respond to requirement changes (e.g., HUD progress bars).
        /// </summary>
        public event Action<int> ProgressReset;

        /// <summary>
        ///     Raised after <see cref="Advance"/> increments the tracker. The
        ///     callback receives the current tick progress and the required
        ///     tick count for the active action.
        /// </summary>
        public event Action<int, int> TickAdvanced;

        /// <summary>
        ///     Raised when <see cref="Advance"/> reaches the required tick
        ///     count. Listeners can use this to emit debug traces or trigger
        ///     side effects without re-implementing completion checks.
        /// </summary>
        public event Action<int, int> TickCompleted;

        /// <summary>
        ///     Gets the number of ticks required to complete the current
        ///     action.
        /// </summary>
        public int RequiredTicks => requiredTicks;

        /// <summary>
        ///     Gets how many ticks have elapsed for the current action.
        /// </summary>
        public int ProgressTicks => progressTicks;

        /// <summary>
        ///     Provides a 0..1 representation of progress for UI bindings using
        ///     the configured tick requirement as the denominator.
        /// </summary>
        public float ProgressRatio
        {
            get
            {
                if (requiredTicks <= 0)
                    return 0f;

                return Mathf.Clamp01((float)progressTicks / requiredTicks);
            }
        }

        /// <summary>
        ///     Resets the tracker to start a new action with the supplied tick
        ///     requirement.
        /// </summary>
        /// <param name="newRequiredTicks">Number of ticks needed to complete the action.</param>
        public void Reset(int newRequiredTicks)
        {
            requiredTicks = Mathf.Max(0, newRequiredTicks);
            progressTicks = 0;
            ProgressReset?.Invoke(requiredTicks);
        }

        /// <summary>
        ///     Advances the tracker by a single tick.
        /// </summary>
        /// <returns><c>true</c> when the action has reached completion.</returns>
        public bool Advance()
        {
            if (requiredTicks <= 0)
                return true;

            progressTicks = Mathf.Min(progressTicks + 1, requiredTicks);
            TickAdvanced?.Invoke(progressTicks, requiredTicks);

            if (progressTicks >= requiredTicks)
            {
                TickCompleted?.Invoke(progressTicks, requiredTicks);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Clears any accumulated progress without modifying the required
        ///     tick count. Useful when cancelling actions mid-channel.
        /// </summary>
        public void ClearProgress()
        {
            progressTicks = 0;
        }
    }
}

