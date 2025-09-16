using System;

namespace Status
{
    /// <summary>
    /// Static event hub used to publish buff timer updates. Systems that generate status effects
    /// (combat, consumables, scripted encounters) raise events here so the
    /// <see cref="BuffTimerService"/> can react without tightly coupling to individual features.
    /// </summary>
    public static class BuffEvents
    {
        public static event Action<BuffEventContext> BuffApplied;
        public static event Action<BuffEventContext> BuffRemoved;
        public static event Action<BuffEventContext> BuffRefreshed;

        /// <summary>
        /// Broadcast that a buff should begin tracking.
        /// </summary>
        public static void RaiseBuffApplied(BuffEventContext context)
        {
            context.resetTimer = true;
            BuffApplied?.Invoke(context);
        }

        /// <summary>
        /// Broadcast that an existing buff should refresh its metadata without resetting the timer.
        /// </summary>
        public static void RaiseBuffRefreshed(BuffEventContext context)
        {
            context.resetTimer = false;
            BuffRefreshed?.Invoke(context);
        }

        /// <summary>
        /// Broadcast that a buff should end immediately.
        /// </summary>
        public static void RaiseBuffRemoved(BuffEventContext context)
        {
            BuffRemoved?.Invoke(context);
        }
    }
}
