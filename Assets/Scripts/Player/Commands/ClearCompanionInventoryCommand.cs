using System;
using Companions;
using Player.Ranks;
using InventoryComponent = Inventory.Inventory;

namespace Player.Commands
{
    /// <summary>
    /// Developer-facing command that wipes every item from the active companion's backpack so QA can
    /// quickly reset mining runs or diagnose storage-related bugs.
    /// </summary>
    public sealed class ClearCompanionInventoryCommand : IPlayerCommand
    {
        private const string PrimaryCommandName = "clearcompanioninv";

        /// <summary>Canonical command token used when no alias override is supplied.</summary>
        private readonly string commandName;

        /// <summary>When true the current instance represents an alias for the primary command token.</summary>
        private readonly bool isAlias;

        /// <summary>
        /// Creates a command instance bound to the canonical <c>::clearcompanioninv</c> token.
        /// </summary>
        public ClearCompanionInventoryCommand()
            : this(PrimaryCommandName, false)
        {
        }

        /// <summary>
        /// Creates a command instance using the supplied token. Intended for registering aliases that
        /// should reuse the same execution logic without duplicating the implementation.
        /// </summary>
        /// <param name="token">Command token that will trigger the inventory clear.</param>
        /// <param name="alias">True when the supplied token should be treated as an alias.</param>
        private ClearCompanionInventoryCommand(string token, bool alias)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Command token must contain visible characters.", nameof(token));

            commandName = token.Trim();
            isAlias = alias;
        }

        /// <summary>
        /// Factory helper that builds an alias instance reusing the core inventory clearing logic.
        /// </summary>
        /// <param name="aliasToken">Alternate token that should trigger the command.</param>
        public static ClearCompanionInventoryCommand CreateAlias(string aliasToken)
        {
            return new ClearCompanionInventoryCommand(aliasToken, true);
        }

        /// <inheritdoc />
        public string Name => commandName;

        /// <inheritdoc />
        public string Description => isAlias
            ? $"Alias for ::{PrimaryCommandName}."
            : "Clears every item from the active companion's inventory.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Admin;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            // Ensure the execution path fails fast when no companion is currently active.
            if (!CompanionManager.HasActiveCompanion)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "No companion is currently active, so there is no inventory to clear.");
            }

            CompanionInventory wrapper = CompanionManager.CompanionInventory;
            if (wrapper == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The active companion does not expose an inventory component.");
            }

            InventoryComponent inventory = wrapper.InventoryComponent;
            if (inventory == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Companion inventory component could not be located.");
            }

            // Acquire the slot count before mutating so feedback can reference the configured size.
            int slotCount = 0;
            var model = inventory.Model;
            if (model != null)
                slotCount = model.Size;

            bool clearedAny = inventory.ClearAllSlots();
            if (!clearedAny)
            {
                string alreadyMessage = "Companion inventory is already empty.";
                return PlayerCommandResult.Success(alreadyMessage);
            }

            string message = slotCount > 0
                ? $"Cleared {slotCount}-slot companion inventory."
                : "Cleared companion inventory.";
            return PlayerCommandResult.Success(message);
        }
    }
}
