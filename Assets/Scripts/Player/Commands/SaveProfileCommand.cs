using System;
using Core.Save;
using Player.Ranks;

namespace Player.Commands
{
    /// <summary>
    /// Developer chat command that forces the save system to flush the currently active profile
    /// to disk. Useful when QA needs to guarantee a manual save before closing the client.
    /// </summary>
    public sealed class SaveProfileCommand : IPlayerCommand
    {
        /// <inheritdoc />
        public string Name => "save";

        /// <inheritdoc />
        public string Description => "Flushes the active profile to disk using the SaveManager.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Developer;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            try
            {
                // Trigger the global save routine which writes all registered saveables and
                // flushes the active profile to disk.
                SaveManager.SaveAll();
            }
            catch (Exception ex)
            {
                // Surface the failure so the caller knows the save did not complete and why.
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Failed to save the active profile: {ex.Message}");
            }

            return PlayerCommandResult.Success("Profile saved successfully.");
        }
    }
}
