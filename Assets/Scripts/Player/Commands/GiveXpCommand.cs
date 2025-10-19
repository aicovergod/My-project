using System;
using System.Globalization;
using Player.Ranks;
using Skills;
using Player;

namespace Player.Commands
{
    /// <summary>
    /// Developer-only chat command that injects raw XP into a chosen skill for rapid progression testing.
    /// </summary>
    public sealed class GiveXpCommand : IPlayerCommand
    {
        private const string Usage = "Usage: ::givexp <skillName> <amount>";

        /// <inheritdoc />
        public string Name => "givexp";

        /// <inheritdoc />
        public string Description => "Awards XP to a specific skill and persists the change immediately.";

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

            if (!float.TryParse(context.Arguments[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float amount) || amount <= 0f)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Amount must be a positive number of XP.");
            }

            if (!PlayerLocator.TryFindPlayer(out var player) || player == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Unable to locate the active player. Enter the world before using ::givexp.");
            }

            var skillManager = player.GetComponent<SkillManager>();
            if (skillManager == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The player does not have a SkillManager component, so XP cannot be awarded.");
            }

            int newLevel = skillManager.AddXP(skillType, amount);
            skillManager.Save();
            float newXp = skillManager.GetXp(skillType);

            string formattedAmount = amount.ToString("N2", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            string formattedTotal = newXp.ToString("N2", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            return PlayerCommandResult.Success(
                $"Awarded {formattedAmount} XP to {skillType}. New level: {newLevel} (total XP {formattedTotal}).");
        }
    }
}
