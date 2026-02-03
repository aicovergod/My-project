using System.Globalization;
using Player.Movement;
using Player.Ranks;
using UnityEngine;

namespace Player.Commands
{
    /// <summary>
    /// Teleports the active player (and their pet) to a set of world coordinates while persisting the new position.
    /// </summary>
    public sealed class TeleportCommand : IPlayerCommand
    {
        /// <inheritdoc />
        public string Name => "teleport";

        /// <inheritdoc />
        public string Description => "Teleports the player to the supplied world coordinates.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Moderator;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            var args = context.Arguments;
            if (args.Count < 2)
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.InvalidSyntax, "Usage: ::teleport <x> <y>");

            if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.InvalidSyntax, "Coordinates must be numeric values (use a period for decimals).");
            }

            Vector3 target = new Vector3(x, y, 0f);
            if (!PlayerTeleportUtility.TryTeleportPlayer(target, out string error))
            {
                string message = string.IsNullOrEmpty(error) ? "Teleport failed due to an unexpected error." : error;
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.ExecutionError, message);
            }

            string formattedX = x.ToString("0.##", CultureInfo.InvariantCulture);
            string formattedY = y.ToString("0.##", CultureInfo.InvariantCulture);
            return PlayerCommandResult.Success($"Teleported to ({formattedX}, {formattedY}).");
        }
    }
}
