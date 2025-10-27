using System;
using System.Globalization;
using Player;
using Player.Movement;
using Player.Ranks;
using UnityEngine;

namespace Player.Commands
{
    /// <summary>
    /// Developer-only chat command that adjusts the player's movement speed using predefined presets.
    /// Useful for rapidly testing world traversal, encounter setups, or long-distance content without
    /// modifying inspector defaults.
    /// </summary>
    public sealed class SetSpeedCommand : IPlayerCommand
    {
        private const float SlowSpeed = 2.25f;
        private const float FastSpeed = 5.5f;
        private const float UltraSpeed = 7.5f;
        private const float NormalSpeedFallback = 3.5f;

        // Cache the movement controller's initial speed so the normal preset restores the inspector-configured value.
        private static float? cachedDefaultSpeed;

        /// <inheritdoc />
        public string Name => "setspeed";

        /// <inheritdoc />
        public string Description => "Adjusts the active player's movement speed preset.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Developer;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            var args = context.Arguments;
            if (args.Count != 1)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Usage: ::setspeed <slow|normal|fast|ultra>");
            }

            string presetToken = args[0];
            if (string.IsNullOrWhiteSpace(presetToken))
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Usage: ::setspeed <slow|normal|fast|ultra>");
            }

            if (!Player.PlayerLocator.TryFindPlayer(out var playerObject) || playerObject == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Unable to locate the active player object.");
            }

            var mover = playerObject.GetComponent<PlayerMover>();
            var controller = mover != null ? mover.MovementController : playerObject.GetComponent<PlayerMovementController>();
            if (controller == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The player object does not expose a movement controller.");
            }

            CacheDefaultSpeed(controller);

            if (!TryResolvePreset(presetToken, out float targetSpeed, out bool restoreDefault, out string error))
            {
                string message = string.IsNullOrEmpty(error)
                    ? "Usage: ::setspeed <slow|normal|fast|ultra>"
                    : error;
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.InvalidSyntax, message);
            }

            if (restoreDefault)
            {
                float baseSpeed = cachedDefaultSpeed.HasValue && cachedDefaultSpeed.Value > 0f
                    ? cachedDefaultSpeed.Value
                    : NormalSpeedFallback;
                controller.MoveSpeed = baseSpeed;
                string formatted = baseSpeed.ToString("0.##", CultureInfo.InvariantCulture);
                return PlayerCommandResult.Success($"Movement speed reset to normal ({formatted} units/s).");
            }

            controller.MoveSpeed = Mathf.Max(0f, targetSpeed);
            string presetName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(presetToken.ToLowerInvariant());
            string formattedSpeed = controller.MoveSpeed.ToString("0.##", CultureInfo.InvariantCulture);
            return PlayerCommandResult.Success($"Movement speed set to {presetName} ({formattedSpeed} units/s).");
        }

        private static void CacheDefaultSpeed(PlayerMovementController controller)
        {
            if (controller == null)
                return;

            if (!cachedDefaultSpeed.HasValue || cachedDefaultSpeed.Value <= 0f)
                cachedDefaultSpeed = Mathf.Max(controller.MoveSpeed, 0.01f);
        }

        private static bool TryResolvePreset(string token, out float speed, out bool restoreDefault, out string error)
        {
            speed = 0f;
            restoreDefault = false;
            error = string.Empty;

            if (string.Equals(token, "slow", StringComparison.OrdinalIgnoreCase))
            {
                speed = SlowSpeed;
                return true;
            }

            if (string.Equals(token, "normal", StringComparison.OrdinalIgnoreCase))
            {
                restoreDefault = true;
                return true;
            }

            if (string.Equals(token, "fast", StringComparison.OrdinalIgnoreCase))
            {
                speed = FastSpeed;
                return true;
            }

            if (string.Equals(token, "ultra", StringComparison.OrdinalIgnoreCase))
            {
                speed = UltraSpeed;
                return true;
            }

            error = $"Unknown speed preset '{token}'.";
            return false;
        }
    }
}
