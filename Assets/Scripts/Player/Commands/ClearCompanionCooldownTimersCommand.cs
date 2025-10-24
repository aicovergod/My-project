using Companions;
using Player.Ranks;

namespace Player.Commands
{
    /// <summary>
    /// Developer command that clears every active companion skill cooldown timer so QA can immediately
    /// retry declined orders without waiting for the natural countdown to expire.
    /// </summary>
    public sealed class ClearCompanionCooldownTimersCommand : IPlayerCommand
    {
        /// <inheritdoc />
        public string Name => "clearccdt";

        /// <inheritdoc />
        public string Description => "Clears all active companion skill decline countdown timers.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Admin;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            if (!CompanionManager.HasActiveCompanion)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "No companion is currently active, so there are no cooldown timers to clear.");
            }

            CompanionSkillCooldownTracker tracker = CompanionManager.CompanionSkillCooldowns;
            if (tracker == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The active companion does not expose a cooldown tracker component.");
            }

            int clearedCount = tracker.ClearAllCooldowns();
            if (clearedCount <= 0)
            {
                return PlayerCommandResult.Success("Companion has no active skill cooldown timers.");
            }

            string message = clearedCount == 1
                ? "Cleared 1 companion skill cooldown timer."
                : $"Cleared {clearedCount} companion skill cooldown timers.";

            return PlayerCommandResult.Success(message);
        }
    }
}
