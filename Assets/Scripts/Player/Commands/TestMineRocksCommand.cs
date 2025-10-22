using Companions;
using Player.Ranks;

namespace Player.Commands
{
    /// <summary>
    /// Developer-only helper that triggers the companion mining routine as if the Mine Rocks UI button was clicked.
    /// </summary>
    public sealed class TestMineRocksCommand : IPlayerCommand
    {
        /// <inheritdoc />
        public string Name => "testminerocks";

        /// <inheritdoc />
        public string Description => "Orders the active companion to start mining nearby rocks for debugging.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Developer;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            // Ensure the developer has an active companion available to receive the mining command.
            if (!CompanionManager.HasActiveCompanion)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "You must have a companion summoned before using ::testminerocks.");
            }

            // Attempt to begin the same area-mining workflow invoked by the CompanionCommandMenu Mine Rocks button.
            bool accepted = CompanionManager.TryCommandMineNearby();
            if (!accepted)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Companion could not start mining nearby rocks. Check debug logs for details.");
            }

            return PlayerCommandResult.Success("Companion is mining nearby rocks.");
        }
    }
}
