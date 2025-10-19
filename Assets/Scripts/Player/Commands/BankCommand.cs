using BankSystem;
using Beastmaster;
using Player.Ranks;

namespace Player.Commands
{
    /// <summary>
    /// Opens the bank interface for the issuing player when executed by an authorised account.
    /// </summary>
    public sealed class BankCommand : IPlayerCommand
    {
        /// <inheritdoc />
        public string Name => "bank";

        /// <inheritdoc />
        public string Description => "Opens the bank interface for the issuing player.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Admin;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            var bank = BankUI.Instance;
            if (bank == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The bank interface is not available in this scene.");
            }

            if (BankUI.IsBankModalActive || bank.IsOpen)
                return PlayerCommandResult.Success("The bank is already open.");

            if (PetMergeController.Instance != null && PetMergeController.Instance.IsMerged)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "You cannot open the bank while merged with your pet.");
            }

            bank.Open();

            if (!bank.IsOpen)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Failed to open the bank. Ensure the UI manager is available and no other modals are blocking it.");
            }

            return PlayerCommandResult.Success("Bank opened.");
        }
    }
}
