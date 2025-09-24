using UnityEngine;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Coordinates the shared failure message used when a hybrid cooking/bonfire object detects
    ///     that the player lacks both cookable ingredients and logs. Ensures only a single message
    ///     is shown per interaction frame so duplicate floating text is avoided.
    /// </summary>
    public static class BonfireCookingMessageUtility
    {
        private const string CombinedFailureMessage = "You don't have any raw fish to cook, or logs to add";
        private static int lastFrameIssued = -1;

        /// <summary>
        ///     Attempts to reserve the combined failure message for the current frame.
        /// </summary>
        /// <param name="message">Outputs the message when it has not been issued this frame.</param>
        /// <returns>
        ///     <c>true</c> when the caller should present feedback to the player. Subsequent calls in
        ///     the same frame receive <c>false</c> so duplicate popups can be suppressed.
        /// </returns>
        public static bool TryAcquireCombinedMessage(out string message)
        {
            int currentFrame = Time.frameCount;
            if (lastFrameIssued == currentFrame)
            {
                message = string.Empty;
                return false;
            }

            lastFrameIssued = currentFrame;
            message = CombinedFailureMessage;
            return true;
        }
    }
}
