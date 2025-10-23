using System.Text;
using Companions;
using Companions.Conversation;
using Player.Ranks;

namespace Player.Commands
{
    /// <summary>
    /// Developer-only helper that forces the companion conversation service to ask a proactive question.
    /// </summary>
    public sealed class TestCompanionQuestionCommand : IPlayerCommand
    {
        /// <inheritdoc />
        public string Name => "testcompquestion";

        /// <inheritdoc />
        public string Description => "Forces your companion to ask a proactive question for debugging.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Developer;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            // Developers must have an active companion in the world before attempting to prompt it.
            if (!CompanionManager.HasActiveCompanion)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "You must have a companion summoned before using ::testcompquestion.");
            }

            // Resolve the conversation service singleton that coordinates companion dialogue output.
            var conversationService = CompanionConversationService.Instance;
            if (conversationService == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Companion conversation service is not initialised yet.");
            }

            string overrideContext = ResolveOverrideContext(context);
            if (!conversationService.TryForceDeveloperQuestion(overrideContext, out string failureReason))
            {
                // Relay the specific reason returned by the service when a question cannot be generated.
                string reason = string.IsNullOrWhiteSpace(failureReason)
                    ? "Companion could not assemble a question."
                    : failureReason;

                return PlayerCommandResult.Failure(PlayerCommandFailureReason.ExecutionError, reason);
            }

            if (string.IsNullOrWhiteSpace(overrideContext))
                return PlayerCommandResult.Success("Companion will ask you a question momentarily.");

            return PlayerCommandResult.Success($"Companion will ask about: {overrideContext}.");
        }

        /// <summary>
        /// Collapses the supplied command arguments into a single descriptive string that can seed the question.
        /// </summary>
        /// <param name="context">Command context containing the parsed argument tokens.</param>
        private static string ResolveOverrideContext(PlayerCommandContext context)
        {
            if (context.Arguments == null || context.Arguments.Count == 0)
                return string.Empty;

            // Combine all argument tokens while trimming excess whitespace so developers can pass natural sentences.
            var builder = new StringBuilder();
            for (int i = 0; i < context.Arguments.Count; i++)
            {
                string token = context.Arguments[i];
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                if (builder.Length > 0)
                    builder.Append(' ');

                builder.Append(token.Trim());
            }

            return builder.ToString().Trim();
        }
    }
}
