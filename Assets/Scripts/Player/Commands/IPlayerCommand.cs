using Player.Ranks;

namespace Player.Commands
{
    /// <summary>
    /// Contract implemented by gameplay chat commands exposed through <see cref="PlayerCommandService"/>.
    /// Commands validate their own arguments and return feedback describing the outcome.
    /// </summary>
    public interface IPlayerCommand
    {
        /// <summary>
        /// Canonical command token that the player types after the <c>::</c> prefix.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Localised description displayed in tooling or help output.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Minimum rank required to execute the command.
        /// </summary>
        PlayerRank RequiredRank { get; }

        /// <summary>
        /// Executes the command against the supplied context and returns the outcome.
        /// </summary>
        PlayerCommandResult Execute(PlayerCommandContext context);
    }
}
