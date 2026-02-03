using System;
using System.Globalization;
using Player.Ranks;
using Skills;
using UnityEngine;
using Player;

namespace Player.Commands
{
    /// <summary>
    /// Developer-only command that overrides a specific skill level without granting XP ticks.
    /// Useful for QA scenarios that require a precise level without grinding.
    /// </summary>
    public sealed class SetSkillLevelCommand : IPlayerCommand
    {
        private const string Usage = "Usage: ::setskill <skillName> <level>";

        /// <inheritdoc />
        public string Name => "setskill";

        /// <inheritdoc />
        public string Description => "Directly sets a skill's level (clamped between 1 and 99).";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Developer;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            if (context.Arguments.Count != 2)
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.InvalidSyntax, Usage);

            string skillToken = context.Arguments[0];
            if (!Enum.TryParse(skillToken, true, out SkillType skillType))
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    $"Unknown skill '{skillToken}'. {Usage}");
            }

            if (!int.TryParse(context.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int requestedLevel))
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Level must be a whole number between 1 and 99.");
            }

            if (!PlayerLocator.TryFindPlayer(out var player) || player == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Unable to locate the active player. Enter the world before using ::setskill.");
            }

            var skillManager = player.GetComponent<SkillManager>();
            if (skillManager == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The player does not have a SkillManager component, so skills cannot be modified.");
            }

            int clampedLevel = Mathf.Clamp(requestedLevel, 1, 99);
            skillManager.DebugSetLevel(skillType, clampedLevel);
            skillManager.Save();

            int resolvedLevel = skillManager.GetLevel(skillType);
            if (resolvedLevel != clampedLevel)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Failed to apply level {clampedLevel} to {skillType}. Check that the XP table is configured correctly.");
            }

            string suffix = requestedLevel == clampedLevel
                ? string.Empty
                : $" (requested {requestedLevel}, clamped to {clampedLevel})";
            return PlayerCommandResult.Success($"Set {skillType} level to {resolvedLevel}{suffix}.");
        }
    }
}
