using System.Globalization;

namespace Skills
{
    /// <summary>
    /// Provides helpers for converting <see cref="SkillType"/> values into
    /// player-facing display strings for UI and chat messages.
    /// </summary>
    public static class SkillNameUtility
    {
        /// <summary>
        /// Returns a display-ready version of the supplied skill name. The method
        /// inserts spaces for compound identifiers and preserves leading capitals
        /// so the result matches OSRS-style wording used throughout the UI.
        /// </summary>
        /// <param name="skill">Skill value that should be converted.</param>
        public static string GetDisplayName(SkillType skill)
        {
            string raw = skill.ToString();
            if (string.IsNullOrEmpty(raw))
                return "Skill";

            // Handle compound enum identifiers (e.g. Future_Skill) by splitting on underscores
            // and capitalising each segment before joining them with spaces.
            if (raw.Contains("_"))
            {
                string[] parts = raw.Split('_');
                for (int i = 0; i < parts.Length; i++)
                    parts[i] = Capitalise(parts[i]);
                return string.Join(" ", parts);
            }

            return raw;
        }

        /// <summary>
        /// Returns a lowercase version of the skill name for inline sentences such as
        /// "levelled up her strength" while still honouring multi-word identifiers.
        /// </summary>
        /// <param name="skill">Skill value that should be converted.</param>
        public static string GetSentenceName(SkillType skill)
        {
            return GetDisplayName(skill).ToLower(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Capitalises the supplied text using invariant culture rules.
        /// </summary>
        private static string Capitalise(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.Length == 1)
                return value.ToUpperInvariant();

            return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
        }
    }
}
