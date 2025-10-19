using System;
using System.Text;
using Player.Ranks;

namespace Player.Commands
{
    /// <summary>
    /// Command that surfaces a summary of all commands the issuing player has permission to use.
    /// </summary>
    public sealed class CommandsListCommand : IPlayerCommand
    {
        private const int MaxDisplayedCommands = 25;

        private readonly PlayerCommandService commandService;
        private readonly PlayerRankService rankService;

        /// <summary>
        /// Creates a new <see cref="CommandsListCommand"/> bound to the supplied services.
        /// </summary>
        /// <param name="commandService">Service responsible for resolving registered commands.</param>
        /// <param name="rankService">Service used to validate the player's rank permissions.</param>
        public CommandsListCommand(PlayerCommandService commandService, PlayerRankService rankService)
        {
            this.commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            this.rankService = rankService ?? throw new ArgumentNullException(nameof(rankService));
        }

        /// <inheritdoc />
        public string Name => "commands";

        /// <inheritdoc />
        public string Description => "Lists every command available to your current rank.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Support;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var commands = commandService.GetRegisteredCommands();
            if (commands.Count == 0)
                return PlayerCommandResult.Success("No commands are registered at this time.");

            var builder = new StringBuilder(256);
            int accessibleCount = 0;

            foreach (var command in commands)
            {
                if (!rankService.HasPermission(context.SenderRank, command.RequiredRank))
                    continue;

                accessibleCount++;
                if (accessibleCount > MaxDisplayedCommands)
                    continue;

                if (builder.Length > 0)
                    builder.AppendLine();

                builder.Append("::");
                builder.Append(command.Name);

                if (!string.IsNullOrWhiteSpace(command.Description))
                {
                    builder.Append(" - ");
                    builder.Append(command.Description);
                }
            }

            if (accessibleCount == 0)
                return PlayerCommandResult.Success("No commands are available at your rank.");

            if (builder.Length == 0)
                builder.Append("Command list truncated due to length limits.");

            int truncatedCount = Math.Max(0, accessibleCount - MaxDisplayedCommands);
            if (truncatedCount > 0)
            {
                builder.AppendLine();
                builder.Append("...and ");
                builder.Append(truncatedCount);
                builder.Append(truncatedCount == 1 ? " more command." : " more commands.");
            }

            return PlayerCommandResult.Success(builder.ToString());
        }
    }
}
