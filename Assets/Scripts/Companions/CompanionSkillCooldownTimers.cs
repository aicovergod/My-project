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

        /// <summary>Default cooldown duration (in minutes) applied after a woodcutting refusal.</summary>
        public const float WoodcuttingCooldownMinutes = 5f;

        /// <summary>Default cooldown duration (in minutes) applied after a fishing refusal.</summary>
        public const float FishingCooldownMinutes = 5f;

        /// <summary>Default cooldown duration (in minutes) applied after a combat training refusal.</summary>
        public const float CombatDeclineCooldownMinutes = 5f;

        /// <summary>Time span representation of <see cref="MiningCooldownMinutes"/>.</summary>
        public static readonly TimeSpan MiningCooldownDuration = TimeSpan.FromMinutes(MiningCooldownMinutes);

        /// <summary>Time span representation of <see cref="WoodcuttingCooldownMinutes"/>.</summary>
        public static readonly TimeSpan WoodcuttingCooldownDuration = TimeSpan.FromMinutes(WoodcuttingCooldownMinutes);

        /// <summary>Time span representation of <see cref="FishingCooldownMinutes"/>.</summary>
        public static readonly TimeSpan FishingCooldownDuration = TimeSpan.FromMinutes(FishingCooldownMinutes);

        /// <summary>Time span representation of <see cref="CombatDeclineCooldownMinutes"/>.</summary>
        public static readonly TimeSpan CombatDeclineCooldownDuration = TimeSpan.FromMinutes(CombatDeclineCooldownMinutes);

        /// <summary>Skills that are treated as combat disciplines.</summary>
        private static readonly SkillType[] CombatSkills =
        {
            SkillType.Hitpoints,
            SkillType.Attack,
            SkillType.Strength,
            SkillType.Ranged,
            SkillType.Defence,
            SkillType.Magic
        };

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
        /// Checks whether a woodcutting command should be rejected because the cooldown is still active.
        /// When active a flavour line is published and <paramref name="failureReason"/> is set to
        /// <see cref="CompanionWoodcuttingCommandResult.Declined"/>.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="failureReason">Failure reason populated when a decline occurs.</param>
        public static bool ShouldDeclineWoodcuttingRequest(
            CompanionSkillCooldownTracker tracker,
            out CompanionWoodcuttingCommandResult failureReason)
        {
            failureReason = CompanionWoodcuttingCommandResult.Accepted;

            if (tracker == null)
                return false;

            if (!tracker.TryGetRemaining(SkillType.Woodcutting, out var remaining) || remaining <= TimeSpan.Zero)
                return false;

            PublishWoodcuttingCooldownMessage(remaining);
            failureReason = CompanionWoodcuttingCommandResult.Declined;
            return true;
        }

        /// <summary>
        /// Checks whether a fishing command should be rejected because the cooldown is still active.
        /// When active a flavour line is published and <paramref name="failureReason"/> is set to
        /// <see cref="CompanionFishingCommandResult.Declined"/>.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="failureReason">Failure reason populated when a decline occurs.</param>
        public static bool ShouldDeclineFishingRequest(
            CompanionSkillCooldownTracker tracker,
            out CompanionFishingCommandResult failureReason)
        {
            failureReason = CompanionFishingCommandResult.Accepted;

            if (tracker == null)
                return false;

            if (!tracker.TryGetRemaining(SkillType.Fishing, out var remaining) || remaining <= TimeSpan.Zero)
                return false;

            PublishFishingCooldownMessage(remaining);
            failureReason = CompanionFishingCommandResult.Declined;
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
        /// Starts or refreshes the woodcutting cooldown using the shared default duration.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        public static void StartWoodcuttingCooldown(CompanionSkillCooldownTracker tracker)
        {
            tracker?.StartCooldown(SkillType.Woodcutting, WoodcuttingCooldownDuration);
        }

        /// <summary>
        /// Starts or refreshes the fishing cooldown using the shared default duration.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        public static void StartFishingCooldown(CompanionSkillCooldownTracker tracker)
        {
            tracker?.StartCooldown(SkillType.Fishing, FishingCooldownDuration);
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
        /// Clears any active woodcutting cooldown so new commands can be processed immediately.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        public static void ClearWoodcuttingCooldown(CompanionSkillCooldownTracker tracker)
        {
            tracker?.ClearCooldown(SkillType.Woodcutting);
        }

        /// <summary>
        /// Clears any active fishing cooldown so new commands can be processed immediately.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        public static void ClearFishingCooldown(CompanionSkillCooldownTracker tracker)
        {
            tracker?.ClearCooldown(SkillType.Fishing);
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

        /// <summary>
        /// Publishes a companion chat line describing how long remains on the woodcutting cooldown.
        /// </summary>
        private static void PublishWoodcuttingCooldownMessage(TimeSpan remaining)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;
            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();

            double totalMinutes = Math.Max(0d, remaining.TotalMinutes);
            int minutes = Mathf.Max(1, (int)Math.Ceiling(totalMinutes));

            string message = CompanionChatLibrary.GetRandomWoodcuttingDeclineCooldownLine(safePlayerName, minutes);
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Publishes a companion chat line describing how long remains on the fishing cooldown.
        /// </summary>
        private static void PublishFishingCooldownMessage(TimeSpan remaining)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;
            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();

            double totalMinutes = Math.Max(0d, remaining.TotalMinutes);
            int minutes = Mathf.Max(1, (int)Math.Ceiling(totalMinutes));

            string message = CompanionChatLibrary.GetRandomFishingDeclineCooldownLine(safePlayerName, minutes);
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Checks whether a combat-training request should be rejected due to an active cooldown.
        /// </summary>
        /// <param name="requestedSkill">Combat skill the player requested to train.</param>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <returns><c>true</c> when the cooldown is still running.</returns>
        public static bool ShouldDeclineCombatRequest(
            SkillType requestedSkill,
            CompanionSkillCooldownTracker tracker)
        {
            if (!IsCombatSkill(requestedSkill))
                return false;

            if (tracker == null)
                return false;

            if (!tracker.TryGetRemaining(requestedSkill, out var remaining) || remaining <= TimeSpan.Zero)
                return false;

            PublishCombatCooldownMessage(remaining);
            return true;
        }

        /// <summary>
        /// Determines whether the shared combat-decline cooldown is still active, optionally
        /// publishing a companion chat line that references the remaining time.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="publishMessage">
        ///     When <c>true</c> a flavour line sourced from
        ///     <see cref="CompanionChatLibrary.GetRandomCombatDeclineCooldownLine"/> is sent to the
        ///     chat service.
        /// </param>
        /// <returns><c>true</c> when the cooldown is still counting down.</returns>
        public static bool IsCombatDeclineCooldownActive(
            CompanionSkillCooldownTracker tracker,
            bool publishMessage)
        {
            if (!TryGetAnyCombatCooldownRemaining(tracker, out var remaining))
                return false;

            if (publishMessage)
                PublishCombatCooldownMessage(remaining);

            return true;
        }

        /// <summary>
        /// Starts or refreshes the cooldown applied after a combat training decline.
        /// </summary>
        /// <param name="declinedSkill">Specific combat skill that was declined.</param>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        public static void StartCombatDeclineCooldown(
            SkillType declinedSkill,
            CompanionSkillCooldownTracker tracker)
        {
            if (tracker == null || !IsCombatSkill(declinedSkill))
                return;

            for (int i = 0; i < CombatSkills.Length; i++)
                tracker.StartCooldown(CombatSkills[i], CombatDeclineCooldownDuration);

            CompanionManager.HandleCombatDeclineCooldownStarted();
        }

        /// <summary>
        /// Clears any active combat-decline cooldown so new combat requests can be processed.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        public static void ClearCombatDeclineCooldown(CompanionSkillCooldownTracker tracker)
        {
            if (tracker != null)
            {
                for (int i = 0; i < CombatSkills.Length; i++)
                    tracker.ClearCooldown(CombatSkills[i]);
            }

            CompanionManager.HandleCombatDeclineCooldownCleared();
        }

        /// <summary>
        /// Publishes a companion chat line describing how long remains on the combat cooldown.
        /// </summary>
        private static void PublishCombatCooldownMessage(TimeSpan remaining)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;
            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();

            double totalMinutes = Math.Max(0d, remaining.TotalMinutes);
            int minutes = Mathf.Max(1, (int)Math.Ceiling(totalMinutes));

            string message = CompanionChatLibrary.GetRandomCombatDeclineCooldownLine(safePlayerName, minutes);
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Determines whether the supplied skill is part of the combat discipline set.
        /// </summary>
        private static bool IsCombatSkill(SkillType skill)
        {
            for (int i = 0; i < CombatSkills.Length; i++)
            {
                if (CombatSkills[i] == skill)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve the remaining duration for any active combat cooldown timer.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="remaining">Remaining time when an active timer is found.</param>
        /// <returns><c>true</c> when any combat cooldown timer is active.</returns>
        private static bool TryGetAnyCombatCooldownRemaining(
            CompanionSkillCooldownTracker tracker,
            out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;

            if (tracker == null)
                return false;

            TimeSpan longest = TimeSpan.Zero;
            bool active = false;

            for (int i = 0; i < CombatSkills.Length; i++)
            {
                if (!tracker.TryGetRemaining(CombatSkills[i], out var skillRemaining) || skillRemaining <= TimeSpan.Zero)
                    continue;

                if (skillRemaining > longest)
                    longest = skillRemaining;

                active = true;
            }

            if (!active)
                return false;

            remaining = longest;
            return true;
        }
    }
}
