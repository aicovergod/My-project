using System;
using Skills;
using UI.Chat;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Centralises helper logic for skill-related cooldown timers so gathering commands can
    /// share consistent throttling, messaging, and persistence behaviour.
    /// </summary>
    public static class CompanionSkillCooldownTimers
    {
        /// <summary>Default cooldown duration (in minutes) applied after a mining refusal.</summary>
        public const float MiningCooldownMinutes = 5f;

        /// <summary>Time span representation of <see cref="MiningCooldownMinutes"/>.</summary>
        public static readonly TimeSpan MiningCooldownDuration = TimeSpan.FromMinutes(MiningCooldownMinutes);

        /// <summary>
        /// Checks whether a mining command should be rejected because the cooldown is still active.
        /// When the cooldown is active a flavour line is published and <paramref name="failureReason"/>
        /// is set to <see cref="CompanionMiningCommandResult.Declined"/>.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="failureReason">Failure reason populated when a decline occurs.</param>
        /// <returns><c>true</c> when the request should be rejected due to an active cooldown.</returns>
        public static bool ShouldDeclineMiningRequest(
            CompanionSkillCooldownTracker tracker,
            out CompanionMiningCommandResult failureReason)
        {
            failureReason = CompanionMiningCommandResult.Accepted;

            if (tracker == null)
                return false;

            if (!tracker.TryGetRemaining(SkillType.Mining, out var remaining) || remaining <= TimeSpan.Zero)
                return false;

            PublishMiningCooldownMessage(remaining);
            failureReason = CompanionMiningCommandResult.Declined;
            return true;
        }

        /// <summary>
        /// Starts or refreshes the mining cooldown using the shared default duration.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        public static void StartMiningCooldown(CompanionSkillCooldownTracker tracker)
        {
            tracker?.StartCooldown(SkillType.Mining, MiningCooldownDuration);
        }

        /// <summary>
        /// Clears any active mining cooldown so new commands can be processed immediately.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        public static void ClearMiningCooldown(CompanionSkillCooldownTracker tracker)
        {
            tracker?.ClearCooldown(SkillType.Mining);
        }

        /// <summary>
        /// Publishes a companion chat line describing how long remains on the mining cooldown.
        /// </summary>
        private static void PublishMiningCooldownMessage(TimeSpan remaining)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;
            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();

            double totalMinutes = Math.Max(0d, remaining.TotalMinutes);
            int minutes = Mathf.Max(1, (int)Math.Ceiling(totalMinutes));

            string message = CompanionChatLibrary.GetRandomMiningDeclineCooldownLine(safePlayerName, minutes);
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
        }
    }
}
