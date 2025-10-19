using System.Globalization;
using Inventory;
using Player.Ranks;
using UnityEngine;
using Player;

namespace Player.Commands
{
    /// <summary>
    /// Developer-only chat command that injects items directly into the active player's inventory.
    /// Supports optional stack quantities so QA can reproduce late-game loadouts quickly.
    /// </summary>
    public sealed class GiveItemCommand : IPlayerCommand
    {
        private const string Usage = "Usage: ::giveitem <itemId> [quantity]";

        /// <inheritdoc />
        public string Name => "giveitem";

        /// <inheritdoc />
        public string Description => "Adds an item to the player's inventory with an optional quantity override.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Developer;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            if (context.Arguments.Count == 0 || context.Arguments.Count > 2)
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.InvalidSyntax, Usage);

            string itemId = context.Arguments[0];
            if (string.IsNullOrWhiteSpace(itemId))
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.InvalidSyntax, Usage);

            int quantity = 1;
            if (context.Arguments.Count >= 2)
            {
                if (!int.TryParse(context.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) || quantity <= 0)
                {
                    return PlayerCommandResult.Failure(
                        PlayerCommandFailureReason.InvalidSyntax,
                        "Quantity must be a positive whole number.");
                }
            }

            if (!PlayerLocator.TryFindPlayer(out var player) || player == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Unable to locate the active player. Enter the world before using ::giveitem.");
            }

            var inventory = player.GetComponent<Inventory.Inventory>();
            if (inventory == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The player does not have an Inventory component, so items cannot be added.");
            }

            var item = ItemDatabase.GetItem(itemId);
            if (item == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    $"Item '{itemId}' was not found in the item database.");
            }

            if (!inventory.CanAddItem(item, quantity))
            {
                string display = string.IsNullOrEmpty(item.itemName) ? item.id : item.itemName;
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Inventory cannot accept {quantity} x {display}. Free up space or bank items first.");
            }

            if (!inventory.AddItem(item, quantity))
            {
                string display = string.IsNullOrEmpty(item.itemName) ? item.id : item.itemName;
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Failed to add {quantity} x {display} to the inventory due to an unexpected error.");
            }

            string itemDisplay = string.IsNullOrEmpty(item.itemName) ? item.id : item.itemName;
            string quantityPrefix = quantity == 1 ? string.Empty : $"{quantity} x ";
            return PlayerCommandResult.Success($"Added {quantityPrefix}{itemDisplay} to your inventory.");
        }
    }
}
