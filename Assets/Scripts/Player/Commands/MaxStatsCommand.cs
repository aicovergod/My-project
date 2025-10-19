using System;
using Player.Ranks;
using Skills;
using UnityEngine;

namespace Player.Commands
{
    /// <summary>
    /// Developer-only command that boosts every skill to level 99 and fully restores hitpoints.
    /// This provides a fast way to validate late-game content without manual training.
    /// </summary>
    public sealed class MaxStatsCommand : IPlayerCommand
    {
        private const int TargetLevel = 99;

        /// <inheritdoc />
        public string Name => "maxstats";

        /// <inheritdoc />
        public string Description => "Sets every skill to level 99 and restores the player's hitpoints.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Developer;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            // Command accepts no arguments to avoid accidental misuse (e.g. ::maxstats otherPlayer).
            if (context.Arguments.Count > 0)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Usage: ::maxstats");
            }

            if (!PlayerLocator.TryFindPlayer(out var player) || player == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Unable to locate the active player. Enter the world before using ::maxstats.");
            }

            var skillManager = player.GetComponent<SkillManager>();
            if (skillManager == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The player does not have a SkillManager component, so stats cannot be updated.");
            }

            var hitpoints = player.GetComponent<PlayerHitpoints>();

            var skillTypes = (SkillType[])Enum.GetValues(typeof(SkillType));
            int raisedSkillCount = 0;

            // Raise every skill to the configured target level through the SkillManager.
            for (int i = 0; i < skillTypes.Length; i++)
            {
                SkillType type = skillTypes[i];
                int previousLevel = skillManager.GetLevel(type);
                if (previousLevel < TargetLevel)
                {
                    skillManager.DebugSetLevel(type, TargetLevel);
                    if (skillManager.GetLevel(type) > previousLevel)
                        raisedSkillCount++;
                }
            }

            bool restoredHealth = false;
            if (hitpoints != null)
            {
                int previousHpLevel = hitpoints.Level;
                // Re-apply the hitpoints level through the specialised component so its events fire correctly.
                hitpoints.DebugSetLevel(TargetLevel);
                hitpoints.RestoreToFullHealth();
                hitpoints.Save();
                restoredHealth = true;

                if (hitpoints.Level > previousHpLevel && previousHpLevel < TargetLevel)
                    raisedSkillCount++;
            }

            // Persist XP so the boosted profile survives future loads.
            skillManager.Save();

            // Verify that every skill actually reached the target. This catches configuration issues like a missing XP table.
            for (int i = 0; i < skillTypes.Length; i++)
            {
                SkillType type = skillTypes[i];
                if (skillManager.GetLevel(type) < TargetLevel)
                {
                    return PlayerCommandResult.Failure(
                        PlayerCommandFailureReason.ExecutionError,
                        "Failed to set all skills to level 99. Ensure the SkillManager has a valid XP table reference.");
                }
            }

            string suffix = restoredHealth ? " and restored hitpoints to full." : ".";
            if (raisedSkillCount > 0)
            {
                string plural = raisedSkillCount == 1 ? string.Empty : "s";
                return PlayerCommandResult.Success(
                    $"Max stats applied. Raised {raisedSkillCount} skill{plural} to level {TargetLevel}{suffix}");
            }

            return PlayerCommandResult.Success(
                $"All skills were already level {TargetLevel}{suffix}");
        }
    }
}
