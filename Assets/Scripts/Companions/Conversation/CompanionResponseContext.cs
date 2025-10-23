using System;
using System.Collections.Generic;
using Skills;

namespace Companions.Conversation
{
    /// <summary>
    /// Captures the ambient state used when selecting a companion dialogue response. The context
    /// is populated immediately before querying the response catalog so guard predicates can
    /// inspect the current world conditions.
    /// </summary>
    public readonly struct CompanionResponseContext
    {
        /// <summary>
        /// Creates a new response context snapshot.
        /// </summary>
        /// <param name="requestTimeUtc">UTC timestamp representing when the selection is occurring.</param>
        /// <param name="timeOfDayLabel">Human-readable description of the current time of day.</param>
        /// <param name="playerInCombat">True if the player currently has an active combat target.</param>
        /// <param name="companionInCombat">True if the companion is presently engaged in combat.</param>
        /// <param name="recentSkillActions">Ordered collection of recent player skill actions.</param>
        /// <param name="pendingResponseCount">Number of responses already queued before this selection.</param>
        /// <param name="suggestedSkill">Optional skill the companion is nudging the player toward.</param>
        /// <param name="suggestedSkillName">Display name for the suggested skill.</param>
        /// <param name="suggestedSkillAction">Description of the underlying skill action.</param>
        /// <param name="suggestedSkillAge">How long ago the skill event occurred.</param>
        /// <param name="suggestedSkillRecency">Human-readable label describing the skill recency.</param>
        public CompanionResponseContext(
            DateTime requestTimeUtc,
            string timeOfDayLabel,
            bool playerInCombat,
            bool companionInCombat,
            IReadOnlyList<string> recentSkillActions,
            int pendingResponseCount,
            SkillType? suggestedSkill,
            string suggestedSkillName,
            string suggestedSkillAction,
            TimeSpan? suggestedSkillAge,
            string suggestedSkillRecency)
        {
            RequestTimeUtc = requestTimeUtc;
            TimeOfDayLabel = timeOfDayLabel ?? string.Empty;
            PlayerInCombat = playerInCombat;
            CompanionInCombat = companionInCombat;
            RecentSkillActions = recentSkillActions ?? Array.Empty<string>();
            PendingResponseCount = Math.Max(0, pendingResponseCount);
            SuggestedSkill = suggestedSkill;
            SuggestedSkillName = suggestedSkillName ?? string.Empty;
            SuggestedSkillActionDescription = suggestedSkillAction ?? string.Empty;
            SuggestedSkillAge = suggestedSkillAge;
            SuggestedSkillRecency = suggestedSkillRecency ?? string.Empty;

            CombatStateDescriptor = ResolveCombatStateDescriptor(playerInCombat, companionInCombat);
        }

        /// <summary>UTC timestamp describing when the response selection occurs.</summary>
        public DateTime RequestTimeUtc { get; }

        /// <summary>Human-readable descriptor for the current time of day (e.g. morning, dusk).</summary>
        public string TimeOfDayLabel { get; }

        /// <summary>True when the player currently has an active combat target.</summary>
        public bool PlayerInCombat { get; }

        /// <summary>True when the companion is currently engaged in combat.</summary>
        public bool CompanionInCombat { get; }

        /// <summary>Ordered collection of recent player skill actions (newest first).</summary>
        public IReadOnlyList<string> RecentSkillActions { get; }

        /// <summary>Number of responses already queued prior to this selection.</summary>
        public int PendingResponseCount { get; }

        /// <summary>Textual description of the combined combat state.</summary>
        public string CombatStateDescriptor { get; }

        /// <summary>True when any skill actions have been recorded recently.</summary>
        public bool HasRecentSkillActions => RecentSkillActions.Count > 0;

        /// <summary>True when additional responses were already queued before selecting a template.</summary>
        public bool HasPendingResponses => PendingResponseCount > 0;

        /// <summary>Most recent recorded skill action or an empty string when none exist.</summary>
        public string LatestSkillAction => HasRecentSkillActions ? RecentSkillActions[0] : string.Empty;

        /// <summary>Skill suggested by the scheduler when prompting proactive dialogue.</summary>
        public SkillType? SuggestedSkill { get; }

        /// <summary>Display name for the suggested skill.</summary>
        public string SuggestedSkillName { get; }

        /// <summary>Describes the recent skill action associated with the suggestion.</summary>
        public string SuggestedSkillActionDescription { get; }

        /// <summary>Elapsed time since the suggestion's source event occurred.</summary>
        public TimeSpan? SuggestedSkillAge { get; }

        /// <summary>Human-readable description of how recent the skill event was.</summary>
        public string SuggestedSkillRecency { get; }

        /// <summary>True when the context has a concrete skill suggestion.</summary>
        public bool HasSuggestedSkill => SuggestedSkill.HasValue;

        /// <summary>True when a skill action description accompanies the suggestion.</summary>
        public bool HasSuggestedSkillAction => !string.IsNullOrWhiteSpace(SuggestedSkillActionDescription);

        /// <summary>True when a recency label is available for the suggested skill.</summary>
        public bool HasSuggestedSkillRecency => !string.IsNullOrWhiteSpace(SuggestedSkillRecency);

        private static string ResolveCombatStateDescriptor(bool playerInCombat, bool companionInCombat)
        {
            if (playerInCombat && companionInCombat)
                return "both of us locked in";
            if (playerInCombat)
                return "covering you in combat";
            if (companionInCombat)
                return "keeping foes busy";
            return "standing down";
        }
    }
}

