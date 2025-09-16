using System;
using UnityEngine;
using Util;

namespace Status
{
    /// <summary>
    /// Identifiers for buff timers displayed on the HUD. New effects should be added here so
    /// downstream systems can rely on a consistent key when communicating with the timer service.
    /// </summary>
    public enum BuffType
    {
        Poison,
        Venom,
        Antifire,
        SuperAntifire,
        Overload,
        Freeze,
        Stamina,
        PrayerRenewal,
        Custom
    }

    /// <summary>
    /// Indicates how an <see cref="ItemBuffEffect"/> is triggered.
    /// </summary>
    public enum BuffTrigger
    {
        OnConsume,
        OnEquip,
        OnUnequip,
        Manual
    }

    /// <summary>
    /// High level category for the source that produced a buff effect. This allows UI systems to
    /// customise presentation depending on whether the buff was granted by combat, equipment or
    /// other channels.
    /// </summary>
    public enum BuffSourceType
    {
        Combat,
        Potion,
        Equipment,
        Skill,
        Environment,
        Scripted
    }

    /// <summary>
    /// Reason provided when a buff ends so listeners can differentiate between natural expiry and
    /// manual removal.
    /// </summary>
    public enum BuffEndReason
    {
        Manual,
        Expired
    }

    /// <summary>
    /// Serialised description of a buff timer. Item data, combat abilities and scripted events all
    /// populate this structure before sending it to <see cref="BuffTimerService"/> via
    /// <see cref="BuffEvents"/>.
    /// </summary>
    [Serializable]
    public struct BuffTimerDefinition
    {
        [Tooltip("Logical identifier for this buff.")]
        public BuffType type;

        [Tooltip("Optional readable name shown on the HUD. Defaults to the enum name when empty.")]
        public string displayName;

        [Tooltip("Sprite name located under Resources/UI/Buffs used as the infobox icon.")]
        public string iconId;

        [Tooltip("Total lifetime of the effect in seconds. Set to 0 for indefinite buffs.")]
        public float durationSeconds;

        [Tooltip("Tick interval for recurring effects (seconds). Leave at 0 to reuse durationSeconds.")]
        public float recurringIntervalSeconds;

        [Tooltip("True when the timer should continuously loop instead of expiring.")]
        public bool isRecurring;

        [Tooltip("Emit an expiry warning when the remaining ticks reach the configured threshold.")]
        public bool showExpiryWarning;

        [Tooltip("Explicit warning threshold in ticks. When zero the service derives a sensible default.")]
        public int expiryWarningTicks;

        /// <summary>
        /// Converts the configured duration to OSRS ticks.
        /// </summary>
        public int GetDurationTicks()
        {
            if (durationSeconds <= 0f)
                return -1;
            return Mathf.Max(1, Mathf.CeilToInt(durationSeconds / Ticker.TickDuration));
        }

        /// <summary>
        /// Converts the recurring interval to OSRS ticks. Falls back to <see cref="durationSeconds"/>
        /// when <see cref="recurringIntervalSeconds"/> is not configured.
        /// </summary>
        public int GetIntervalTicks()
        {
            float seconds = recurringIntervalSeconds > 0f ? recurringIntervalSeconds : durationSeconds;
            if (seconds <= 0f)
                return 1;
            return Mathf.Max(1, Mathf.CeilToInt(seconds / Ticker.TickDuration));
        }

        /// <summary>
        /// Provides a default display name when none is specified.
        /// </summary>
        public string ResolveDisplayName()
        {
            return string.IsNullOrEmpty(displayName) ? type.ToString() : displayName;
        }
    }

    /// <summary>
    /// Item driven buff configuration. Items expose an array of these so the inventory and
    /// equipment systems can automatically raise the appropriate buff events when the player
    /// consumes or equips them.
    /// </summary>
    [Serializable]
    public struct ItemBuffEffect
    {
        [Tooltip("Determines when this buff should be applied or removed.")]
        public BuffTrigger trigger;

        [Tooltip("Timer definition that will be forwarded to BuffTimerService.")]
        public BuffTimerDefinition timer;

        [Tooltip("Optional unique identifier for scripted stacking rules.")]
        public string customSourceId;

        [Tooltip("True when reapplying the effect should reset the timer duration.")]
        public bool refreshOnReapply;

        [Tooltip("If true and the trigger is OnEquip the timer is removed automatically on unequip.")]
        public bool removeOnUnequip;
    }

    /// <summary>
    /// Payload broadcast through <see cref="BuffEvents"/> whenever a buff is added, refreshed or
    /// removed.
    /// </summary>
    public struct BuffEventContext
    {
        public GameObject target;
        public BuffTimerDefinition definition;
        public BuffSourceType sourceType;
        public string sourceId;
        public bool resetTimer;
    }

    /// <summary>
    /// Unique lookup key for active buff timers.
    /// </summary>
    public readonly struct BuffKey : IEquatable<BuffKey>
    {
        private readonly int instanceId;
        private readonly BuffType type;

        public BuffKey(GameObject target, BuffType buffType)
        {
            instanceId = target != null ? target.GetInstanceID() : 0;
            type = buffType;
        }

        public bool Equals(BuffKey other) => instanceId == other.instanceId && type == other.type;

        public override bool Equals(object obj) => obj is BuffKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(instanceId, type);
    }

    /// <summary>
    /// Runtime state describing a tracked buff.
    /// </summary>
    public sealed class BuffTimerInstance
    {
        public BuffKey Key { get; }
        public GameObject Target { get; }
        public BuffTimerDefinition Definition { get; private set; }
        public BuffSourceType SourceType { get; private set; }
        public string SourceId { get; private set; }
        public int RemainingTicks { get; internal set; }
        public int DurationTicks { get; private set; }
        public int IntervalTicks { get; private set; }
        public int WarningTicks { get; private set; }
        public int SequenceId { get; internal set; }

        public bool HasDuration => DurationTicks > 0;
        public bool IsIndefinite => DurationTicks < 0 && !Definition.isRecurring;
        public bool IsRecurring => Definition.isRecurring;
        public string DisplayName => Definition.ResolveDisplayName();
        public bool CanWarn => Definition.showExpiryWarning && WarningTicks > 0;

        public BuffTimerInstance(BuffEventContext ctx, int sequenceId)
        {
            Target = ctx.target;
            SequenceId = sequenceId;
            Key = new BuffKey(ctx.target, ctx.definition.type);
            ApplyContext(ctx, true);
        }

        /// <summary>
        /// Updates the cached definition and optionally resets the countdown.
        /// </summary>
        public void ApplyContext(BuffEventContext ctx, bool initial = false)
        {
            Definition = ctx.definition;
            SourceType = ctx.sourceType;
            SourceId = string.IsNullOrEmpty(ctx.sourceId) ? Definition.type.ToString() : ctx.sourceId;
            DurationTicks = Definition.GetDurationTicks();
            IntervalTicks = Definition.isRecurring ? Definition.GetIntervalTicks() : 0;
            WarningTicks = ResolveWarningTicks();

            if (initial || ctx.resetTimer)
            {
                ResetTimer();
            }
            else if (Definition.isRecurring && (RemainingTicks <= 0 || RemainingTicks > IntervalTicks))
            {
                RemainingTicks = Mathf.Max(1, IntervalTicks);
            }
        }

        /// <summary>
        /// Resets the countdown based on the current definition.
        /// </summary>
        public void ResetTimer()
        {
            if (Definition.isRecurring)
            {
                RemainingTicks = Mathf.Max(1, IntervalTicks);
            }
            else if (DurationTicks > 0)
            {
                RemainingTicks = DurationTicks;
            }
            else
            {
                RemainingTicks = -1;
            }
        }

        /// <summary>
        /// Returns a normalised progress value for UI animations.
        /// </summary>
        public float GetProgress01()
        {
            if (!HasDuration)
                return 0f;
            if (DurationTicks <= 0)
                return 0f;
            return Mathf.Clamp01(1f - (float)RemainingTicks / DurationTicks);
        }

        private int ResolveWarningTicks()
        {
            if (!Definition.showExpiryWarning)
                return 0;
            if (Definition.expiryWarningTicks > 0)
                return Definition.expiryWarningTicks;
            if (DurationTicks <= 0)
                return 0;
            // Default to 10% of the duration with sensible caps for short buffs.
            return Mathf.Clamp(DurationTicks / 10, 1, Mathf.Max(1, DurationTicks - 1));
        }
    }
}
