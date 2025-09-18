using Pets;
using Skills;
using UnityEngine;

namespace Skills.Common
{
    /// <summary>
    /// Centralises the pet roll logic used by skilling actions so each skill can
    /// simply forward its source identifier, the player's skills, and the world
    /// location of the interaction. Handles basic validation and anchor
    /// resolution before delegating to <see cref="PetDropSystem"/>.
    /// </summary>
    public static class SkillingPetRewarder
    {
        /// <summary>
        /// Attempt to roll for a skilling pet using the player's Beastmaster
        /// level. The helper short-circuits when the override chance is null or
        /// non-positive and uses the provided transforms to derive the best
        /// world position for the drop.
        /// </summary>
        /// <param name="sourceId">Pet drop table identifier (e.g. "woodcutting").</param>
        /// <param name="skills">Player skill manager used for Beastmaster level queries.</param>
        /// <param name="preferredAnchor">Primary transform used to position the pet roll.</param>
        /// <param name="oneInNOverride">Optional 1-in-N override chance. Null or &lt;= 0 disables the roll.</param>
        /// <param name="fallbackAnchor">Optional backup transform when the preferred anchor is missing.</param>
        /// <returns>True if a pet was rolled and granted.</returns>
        public static bool TryRollPet(string sourceId, SkillManager skills, Transform preferredAnchor, int? oneInNOverride = null, Transform fallbackAnchor = null)
        {
            if (string.IsNullOrEmpty(sourceId))
                return false;
            if (!oneInNOverride.HasValue || oneInNOverride.Value <= 0)
                return false;

            Transform anchor = preferredAnchor;
            if (anchor == null)
            {
                anchor = fallbackAnchor != null ? fallbackAnchor : skills != null ? skills.transform : null;
            }

            Vector3 worldPosition;
            if (anchor != null)
            {
                worldPosition = anchor.position;
            }
            else if (skills != null)
            {
                worldPosition = skills.transform.position;
            }
            else
            {
                worldPosition = Vector3.zero;
            }

            return PetDropSystem.TryRollPet(sourceId, worldPosition, skills, oneInNOverride.Value, out _);
        }
    }
}
