using Pets;

namespace Companions
{
    /// <summary>
    /// Provides shared helper methods for generating player-facing companion labels.
    /// Moves the display-name, action label, and pronoun formatting logic out of
    /// <see cref="CompanionManager"/> so UI and chat flows can reuse the behaviour
    /// without depending on the manager's broader responsibilities.
    /// </summary>
    public static class CompanionDisplayUtility
    {
        /// <summary>
        /// Resolves the formatted display name for the supplied companion definition.
        /// Falls back to a generic label when a definition is not available so UI and
        /// chat lines remain polished during edge cases (e.g. while despawned).
        /// </summary>
        /// <param name="definition">Definition that describes the active companion.</param>
        /// <returns>Display name that should be shown to players.</returns>
        public static string GetDisplayName(PetDefinition definition)
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.displayName))
                return definition.displayName;

            return "Companion";
        }

        /// <summary>
        /// Determines which possessive pronoun the supplied definition prefers.
        /// Defaults to a neutral pronoun to ensure all generated sentences remain
        /// grammatically correct, even when older definitions have not been updated
        /// with bespoke localisation data yet.
        /// </summary>
        /// <param name="definition">Definition that describes the active companion.</param>
        /// <returns>Possessive pronoun ready for lower-case insertion into chat lines.</returns>
        public static string GetPossessivePronoun(PetDefinition definition)
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.possessivePronoun))
                return definition.possessivePronoun.ToLowerInvariant();

            return "their";
        }

        /// <summary>
        /// Translates an active companion action into a short label suitable for HUDs
        /// and debug menus. Idle actions collapse down to a concise neutral value so
        /// stop buttons and tooltips do not surface awkward phrasing.
        /// </summary>
        /// <param name="action">Action that should be described.</param>
        /// <returns>Human-readable action description.</returns>
        public static string GetActionDisplayName(CompanionActiveAction action)
        {
            switch (action)
            {
                case CompanionActiveAction.Combat:
                    return "Combat";
                case CompanionActiveAction.Fishing:
                    return "Fishing";
                case CompanionActiveAction.Mining:
                    return "Mining";
                case CompanionActiveAction.Cooking:
                    return "Cooking";
                case CompanionActiveAction.Woodcutting:
                    return "Chopping";
                default:
                    return "Idle";
            }
        }

        /// <summary>
        /// Generates the stop-action button label that should be displayed for the
        /// supplied companion action. The mapping is centralised here so menus and
        /// tooltips stay perfectly aligned with future action additions.
        /// </summary>
        /// <param name="action">Action that players may wish to cancel.</param>
        /// <returns>Fully formatted button label.</returns>
        public static string GetStopActionLabel(CompanionActiveAction action)
        {
            switch (action)
            {
                case CompanionActiveAction.Combat:
                    return "Stop Combat";
                case CompanionActiveAction.Fishing:
                    return "Stop Fishing";
                case CompanionActiveAction.Mining:
                    return "Stop Mining";
                case CompanionActiveAction.Cooking:
                    return "Stop Cooking";
                case CompanionActiveAction.Woodcutting:
                    return "Stop Chopping";
                default:
                    return "Stop";
            }
        }
    }
}
