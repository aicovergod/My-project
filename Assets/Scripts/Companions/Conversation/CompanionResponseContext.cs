using System;
using System.Collections.Generic;

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
        public CompanionResponseContext(
            DateTime requestTimeUtc,
            string timeOfDayLabel,
            bool playerInCombat,
            bool companionInCombat,
            IReadOnlyList<string> recentSkillActions,
            int pendingResponseCount)
        {
            RequestTimeUtc = requestTimeUtc;
            TimeOfDayLabel = timeOfDayLabel ?? string.Empty;
            PlayerInCombat = playerInCombat;
            CompanionInCombat = companionInCombat;
            RecentSkillActions = recentSkillActions ?? Array.Empty<string>();
            PendingResponseCount = Math.Max(0, pendingResponseCount);

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

