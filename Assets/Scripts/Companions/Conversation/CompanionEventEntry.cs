using System;
using Skills;
using UnityEngine;

namespace Companions.Conversation
{
    /// <summary>
    /// Enumerates the categories of gameplay events that the companion can reference in dialogue.
    /// </summary>
    public enum CompanionEventType
    {
        Combat = 0,
        Loot = 1,
        Gathering = 2,
        Crafting = 3,
        Quest = 4,
        Exploration = 5,
        Other = 6
    }

    /// <summary>
    /// Optional metadata attached to an event so dialogue can reference actors, skills, and locations.
    /// </summary>
    public readonly struct CompanionEventMetadata
    {
        /// <summary>Creates a metadata payload with the supplied optional parameters.</summary>
        public CompanionEventMetadata(
            string primaryActor,
            string secondaryActor,
            string locationName,
            Vector3? worldPosition,
            SkillType? skill,
            string additionalContext)
        {
            PrimaryActor = primaryActor ?? string.Empty;
            SecondaryActor = secondaryActor ?? string.Empty;
            LocationName = locationName ?? string.Empty;
            WorldPosition = worldPosition;
            Skill = skill;
            AdditionalContext = additionalContext ?? string.Empty;
        }

        /// <summary>Display name of the main actor involved in the event.</summary>
        public string PrimaryActor { get; }

        /// <summary>Optional secondary actor such as a target or assisting NPC.</summary>
        public string SecondaryActor { get; }

        /// <summary>User-friendly location label associated with the event.</summary>
        public string LocationName { get; }

        /// <summary>World position where the event occurred.</summary>
        public Vector3? WorldPosition { get; }

        /// <summary>Skill associated with the event when applicable.</summary>
        public SkillType? Skill { get; }

        /// <summary>Additional context that should be appended to the formatted summary.</summary>
        public string AdditionalContext { get; }

        /// <summary>Convenience accessor returning an empty metadata payload.</summary>
        public static CompanionEventMetadata Empty => new CompanionEventMetadata(null, null, null, null, null, null);

        /// <summary>Helper for building a payload using optional arguments.</summary>
        public static CompanionEventMetadata Create(
            string primaryActor = null,
            string secondaryActor = null,
            string locationName = null,
            Vector3? worldPosition = null,
            SkillType? skill = null,
            string additionalContext = null)
        {
            return new CompanionEventMetadata(primaryActor, secondaryActor, locationName, worldPosition, skill, additionalContext);
        }
    }

    /// <summary>
    /// Runtime representation of a single gameplay event that the companion can mention.
    /// </summary>
    public readonly struct CompanionEventEntry
    {
        public CompanionEventEntry(
            string summary,
            CompanionEventType eventType,
            DateTime timestampUtc,
            CompanionEventMetadata metadata)
        {
            Summary = string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();
            EventType = eventType;
            TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
            Metadata = metadata;
        }

        /// <summary>Short summary describing what happened.</summary>
        public string Summary { get; }

        /// <summary>High level category of the event.</summary>
        public CompanionEventType EventType { get; }

        /// <summary>Timestamp recorded when the event was registered.</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>Optional metadata that helps format the event for dialogue.</summary>
        public CompanionEventMetadata Metadata { get; }

        /// <summary>Determines whether the entry has exceeded the provided retention window.</summary>
        /// <param name="retention">Maximum age allowed for the event.</param>
        /// <param name="nowUtc">Current UTC time used for comparisons.</param>
        public bool IsExpired(TimeSpan retention, DateTime nowUtc)
        {
            return nowUtc - TimestampUtc > retention;
        }
    }
}
