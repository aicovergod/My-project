namespace Player.Commands
{
    /// <summary>
    /// Represents the outcome of executing a player chat command.
    /// </summary>
    public readonly struct PlayerCommandResult
    {
        /// <summary>
        /// Preconfigured success result used when a command completes without issues.
        /// </summary>
        public static PlayerCommandResult Success(string message) => new PlayerCommandResult(true, PlayerCommandFailureReason.None, message);

        /// <summary>
        /// Helper used by commands to describe why execution failed.
        /// </summary>
        public static PlayerCommandResult Failure(PlayerCommandFailureReason reason, string message) => new PlayerCommandResult(false, reason, message);

        private PlayerCommandResult(bool success, PlayerCommandFailureReason reason, string message)
        {
            Success = success;
            FailureReason = reason;
            Message = message;
        }

        /// <summary>
        /// Indicates whether the command executed successfully.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Categorises why a command failed. <see cref="PlayerCommandFailureReason.None"/> when successful.
        /// </summary>
        public PlayerCommandFailureReason FailureReason { get; }

        /// <summary>
        /// User-facing text describing what happened.
        /// </summary>
        public string Message { get; }
    }

    /// <summary>
    /// Enumerates the possible failure reasons surfaced by <see cref="PlayerCommandResult"/>.
    /// </summary>
    public enum PlayerCommandFailureReason
    {
        /// <summary>
        /// Command completed without errors.
        /// </summary>
        None = 0,
        /// <summary>
        /// Input arguments did not meet the command's expected syntax.
        /// </summary>
        InvalidSyntax = 1,
        /// <summary>
        /// Command execution raised an exception or hit an unexpected state.
        /// </summary>
        ExecutionError = 2,
    }
}
