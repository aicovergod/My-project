using System.Collections.Generic;
using Player.Ranks;
using UI.Chat;

namespace Player.Commands
{
    /// <summary>
    /// Immutable payload supplied to command implementations when executed through <see cref="PlayerCommandService"/>.
    /// </summary>
    public readonly struct PlayerCommandContext
    {
        public PlayerCommandContext(
            string rawInput,
            string commandName,
            IReadOnlyList<string> arguments,
            string sender,
            PlayerRank senderRank,
            ChatService chatService,
            PlayerRankService rankService)
        {
            RawInput = rawInput;
            CommandName = commandName;
            Arguments = arguments;
            Sender = sender;
            SenderRank = senderRank;
            ChatService = chatService;
            RankService = rankService;
        }

        /// <summary>
        /// Full chat input supplied by the player, including the command prefix.
        /// </summary>
        public string RawInput { get; }

        /// <summary>
        /// Canonical command token that was resolved from the chat input.
        /// </summary>
        public string CommandName { get; }

        /// <summary>
        /// Tokenised arguments supplied after the command name.
        /// </summary>
        public IReadOnlyList<string> Arguments { get; }

        /// <summary>
        /// Username associated with the chat sender.
        /// </summary>
        public string Sender { get; }

        /// <summary>
        /// Rank resolved for the sender at the time of execution.
        /// </summary>
        public PlayerRank SenderRank { get; }

        /// <summary>
        /// Chat service used to surface feedback into the HUD.
        /// </summary>
        public ChatService ChatService { get; }

        /// <summary>
        /// Rank service used for auxiliary permission checks.
        /// </summary>
        public PlayerRankService RankService { get; }
    }
}
