using System;
using System.Collections.Generic;
using System.Text;
using Player.Ranks;
using UI.Chat;
using UnityEngine;
using World;

namespace Player.Commands
{
    /// <summary>
    /// Scene-persistent dispatcher that inspects chat messages for privileged commands and routes
    /// them through registered <see cref="IPlayerCommand"/> implementations.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCommandService : SceneGatedSingletonBehaviour<PlayerCommandService>
    {
        private const string CommandPrefix = "::";

        private readonly Dictionary<string, IPlayerCommand> commandLookup = new Dictionary<string, IPlayerCommand>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> tokenBuffer = new List<string>(8);

        private PlayerRankService rankService;
        private ChatService chatService;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateSingleton);
        }

        private static PlayerCommandService CreateSingleton()
        {
            var go = new GameObject(nameof(PlayerCommandService));
            return go.AddComponent<PlayerCommandService>();
        }

        /// <inheritdoc />
        protected override void OnSingletonAwake()
        {
            base.OnSingletonAwake();

            EnsureServices();
            RegisterBuiltInCommands();
        }

        /// <inheritdoc />
        protected override void OnSingletonDestroyed()
        {
            commandLookup.Clear();
            tokenBuffer.Clear();
            base.OnSingletonDestroyed();
        }

        /// <summary>
        /// Registers a command implementation with the dispatcher.
        /// </summary>
        public void RegisterCommand(IPlayerCommand command)
        {
            if (command == null)
                return;

            commandLookup[command.Name] = command;
        }

        /// <summary>
        /// Inspects the supplied chat message and executes it as a command when appropriate.
        /// </summary>
        public PlayerCommandHandleResult ProcessChatMessage(string sender, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return PlayerCommandHandleResult.NotACommand();

            string trimmed = message.Trim();
            if (!trimmed.StartsWith(CommandPrefix, StringComparison.Ordinal))
                return PlayerCommandHandleResult.NotACommand();

            EnsureServices();

            if (rankService == null || chatService == null)
            {
                string unavailable = "Command services are not initialised yet.";
                PublishGameMessage(unavailable);
                return new PlayerCommandHandleResult(true, false, PlayerCommandServiceError.ServiceUnavailable, unavailable);
            }

            string payload = trimmed.Substring(CommandPrefix.Length);
            if (string.IsNullOrWhiteSpace(payload))
            {
                const string feedback = "No command was specified after the prefix.";
                PublishGameMessage(feedback);
                return new PlayerCommandHandleResult(true, false, PlayerCommandServiceError.InvalidSyntax, feedback);
            }

            if (!TryTokenize(payload, tokenBuffer, out string parseError))
            {
                string feedback = string.IsNullOrEmpty(parseError) ? "Failed to parse command arguments." : parseError;
                PublishGameMessage(feedback);
                tokenBuffer.Clear();
                return new PlayerCommandHandleResult(true, false, PlayerCommandServiceError.InvalidSyntax, feedback);
            }

            if (tokenBuffer.Count == 0)
            {
                const string feedback = "Command input contained no tokens.";
                PublishGameMessage(feedback);
                return new PlayerCommandHandleResult(true, false, PlayerCommandServiceError.InvalidSyntax, feedback);
            }

            string commandToken = tokenBuffer[0];
            tokenBuffer.RemoveAt(0);

            if (!commandLookup.TryGetValue(commandToken, out var command))
            {
                string feedback = $"Unknown command '{commandToken}'.";
                PublishGameMessage(feedback);
                tokenBuffer.Clear();
                return new PlayerCommandHandleResult(true, false, PlayerCommandServiceError.UnknownCommand, feedback);
            }

            var arguments = new List<string>(tokenBuffer);
            tokenBuffer.Clear();

            string resolvedSender = string.IsNullOrWhiteSpace(sender) ? chatService.ActiveUsername : sender.Trim();
            PlayerRank senderRank = rankService.GetRankForUsername(resolvedSender);
            if (!rankService.HasPermission(senderRank, command.RequiredRank))
            {
                string feedback = $"You must be at least {command.RequiredRank} rank to use ::{command.Name}.";
                PublishGameMessage(feedback);
                return new PlayerCommandHandleResult(true, false, PlayerCommandServiceError.Unauthorized, feedback);
            }

            var context = new PlayerCommandContext(message, command.Name, arguments, resolvedSender, senderRank, chatService, rankService);

            PlayerCommandResult commandResult;
            try
            {
                commandResult = command.Execute(context);
            }
            catch (Exception ex)
            {
                Debug.LogError($"PlayerCommandService: Command '{command.Name}' threw an exception.\n{ex}");
                string feedback = $"::{command.Name} failed with an unexpected error.";
                PublishGameMessage(feedback);
                return new PlayerCommandHandleResult(true, false, PlayerCommandServiceError.ExecutionError, feedback);
            }

            string response = string.IsNullOrEmpty(commandResult.Message)
                ? $"::{command.Name} completed."
                : commandResult.Message;

            if (commandResult.Success)
            {
                PublishGameMessage(response);
                return new PlayerCommandHandleResult(true, true, PlayerCommandServiceError.None, response);
            }

            PlayerCommandServiceError error = commandResult.FailureReason switch
            {
                PlayerCommandFailureReason.InvalidSyntax => PlayerCommandServiceError.InvalidSyntax,
                _ => PlayerCommandServiceError.ExecutionError,
            };

            PublishGameMessage(response);
            return new PlayerCommandHandleResult(true, false, error, response);
        }

        private void RegisterBuiltInCommands()
        {
            RegisterCommand(new BankCommand());
            RegisterCommand(new ClearBankCommand());
            RegisterCommand(new ClearInventoryCommand());
            RegisterCommand(new MaxStatsCommand());
            RegisterCommand(new TeleportCommand());
        }

        private void EnsureServices()
        {
            if (rankService == null)
                rankService = PlayerRankService.Instance;
            if (chatService == null)
                chatService = ChatService.Instance;
        }

        private void PublishGameMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (chatService != null)
                chatService.PublishGameMessage(message);
            else
                Debug.LogWarning($"PlayerCommandService: {message}");
        }

        private static bool TryTokenize(string payload, List<string> tokens, out string error)
        {
            tokens.Clear();

            var builder = new StringBuilder(payload.Length);
            bool inQuotes = false;
            bool escaping = false;

            for (int i = 0; i < payload.Length; i++)
            {
                char c = payload[i];

                if (escaping)
                {
                    builder.Append(c);
                    escaping = false;
                    continue;
                }

                if (c == '\\' && inQuotes)
                {
                    escaping = true;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (builder.Length > 0)
                    {
                        tokens.Add(builder.ToString());
                        builder.Length = 0;
                    }
                    continue;
                }

                builder.Append(c);
            }

            if (escaping)
            {
                error = "Command input ended with an incomplete escape sequence.";
                return false;
            }

            if (inQuotes)
            {
                error = "Command input is missing a closing quote.";
                return false;
            }

            if (builder.Length > 0)
                tokens.Add(builder.ToString());

            error = string.Empty;
            return true;
        }
    }
}
