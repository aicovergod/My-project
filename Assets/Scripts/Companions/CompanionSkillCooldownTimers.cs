using System;
using System.Collections.Generic;
using Companions.Chat;
using Companions.Commands;
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

        /// <summary>Default cooldown duration (in minutes) applied after a cooking refusal.</summary>
        public const float CookingCooldownMinutes = 5f;

        /// <summary>Default cooldown duration (in minutes) applied after a combat training refusal.</summary>
        public const float CombatDeclineCooldownMinutes = 5f;

        /// <summary>Time span representation of <see cref="MiningCooldownMinutes"/>.</summary>
        public static readonly TimeSpan MiningCooldownDuration = TimeSpan.FromMinutes(MiningCooldownMinutes);

        /// <summary>Time span representation of <see cref="WoodcuttingCooldownMinutes"/>.</summary>
        public static readonly TimeSpan WoodcuttingCooldownDuration = TimeSpan.FromMinutes(WoodcuttingCooldownMinutes);

        /// <summary>Time span representation of <see cref="FishingCooldownMinutes"/>.</summary>
        public static readonly TimeSpan FishingCooldownDuration = TimeSpan.FromMinutes(FishingCooldownMinutes);

        /// <summary>Time span representation of <see cref="CookingCooldownMinutes"/>.</summary>
        public static readonly TimeSpan CookingCooldownDuration = TimeSpan.FromMinutes(CookingCooldownMinutes);

        /// <summary>Time span representation of <see cref="CombatDeclineCooldownMinutes"/>.</summary>
        public static readonly TimeSpan CombatDeclineCooldownDuration = TimeSpan.FromMinutes(CombatDeclineCooldownMinutes);

        /// <summary>Lookup of cooldown profiles keyed by <see cref="SkillType"/>.</summary>
        private static readonly Dictionary<SkillType, CompanionSkillCooldownProfile> CooldownProfiles =
            new Dictionary<SkillType, CompanionSkillCooldownProfile>
            {
                { SkillType.Mining, new CompanionSkillCooldownProfile(SkillType.Mining, MiningCooldownDuration, CompanionChatLibrary.GetRandomMiningDeclineCooldownLine) },
                { SkillType.Woodcutting, new CompanionSkillCooldownProfile(SkillType.Woodcutting, WoodcuttingCooldownDuration, CompanionChatLibrary.GetRandomWoodcuttingDeclineCooldownLine) },
                { SkillType.Fishing, new CompanionSkillCooldownProfile(SkillType.Fishing, FishingCooldownDuration, CompanionChatLibrary.GetRandomFishingDeclineCooldownLine) },
                { SkillType.Cooking, new CompanionSkillCooldownProfile(SkillType.Cooking, CookingCooldownDuration, CompanionCookingDialogueLibrary.GetCooldownLine) },
                { SkillType.Attack, new CompanionSkillCooldownProfile(SkillType.Attack, CombatDeclineCooldownDuration, CompanionChatLibrary.GetRandomCombatDeclineCooldownLine) },
                { SkillType.Strength, new CompanionSkillCooldownProfile(SkillType.Strength, CombatDeclineCooldownDuration, CompanionChatLibrary.GetRandomCombatDeclineCooldownLine) },
                { SkillType.Defence, new CompanionSkillCooldownProfile(SkillType.Defence, CombatDeclineCooldownDuration, CompanionChatLibrary.GetRandomCombatDeclineCooldownLine) },
                { SkillType.Hitpoints, new CompanionSkillCooldownProfile(SkillType.Hitpoints, CombatDeclineCooldownDuration, CompanionChatLibrary.GetRandomCombatDeclineCooldownLine) },
                { SkillType.Ranged, new CompanionSkillCooldownProfile(SkillType.Ranged, CombatDeclineCooldownDuration, CompanionChatLibrary.GetRandomCombatDeclineCooldownLine) },
                { SkillType.Magic, new CompanionSkillCooldownProfile(SkillType.Magic, CombatDeclineCooldownDuration, CompanionChatLibrary.GetRandomCombatDeclineCooldownLine) }
            };

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
        /// Checks whether a skill command should be rejected because the cooldown is still active.
        /// When the cooldown is active a flavour line is published and <paramref name="failureReason"/>
        /// is set to <paramref name="declinedResult"/>.
        /// </summary>
        /// <typeparam name="TResult">Result enum reported by the concrete skill controller.</typeparam>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="skill">Skill associated with the command.</param>
        /// <param name="acceptedResult">Result that represents an accepted command.</param>
        /// <param name="declinedResult">Result that represents a cooldown rejection.</param>
        /// <param name="failureReason">Result populated when a decline occurs.</param>
        /// <returns><c>true</c> when the request should be rejected due to an active cooldown.</returns>
        public static bool ShouldDecline<TResult>(
            CompanionSkillCooldownTracker tracker,
            SkillType skill,
            TResult acceptedResult,
            TResult declinedResult,
            out TResult failureReason)
        {
            failureReason = acceptedResult;

            if (!TryGetProfile(skill, out var profile))
                return false;

            if (!TryGetRemaining(tracker, profile.Skill, out var remaining))
                return false;

            PublishCooldownMessage(profile, remaining);
            failureReason = declinedResult;
            return true;
        }

        /// <summary>
        /// Starts or refreshes a cooldown using the shared profile duration.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="skill">Skill associated with the cooldown.</param>
        public static void StartCooldown(CompanionSkillCooldownTracker tracker, SkillType skill)
        {
            if (tracker == null)
                return;

            if (!TryGetProfile(skill, out var profile))
                return;

            tracker.StartCooldown(profile.Skill, profile.DefaultDuration);
        }

        /// <summary>
        /// Clears an active cooldown so new commands can be processed immediately.
        /// </summary>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="skill">Skill associated with the cooldown.</param>
        public static void ClearCooldown(CompanionSkillCooldownTracker tracker, SkillType skill)
        {
            if (tracker == null)
                return;

            if (!TryGetProfile(skill, out var profile))
                return;

            tracker.ClearCooldown(profile.Skill);
        }

        /// <summary>
        /// Publishes a companion chat line describing how long remains on the supplied skill cooldown.
        /// </summary>
        /// <param name="skill">Skill associated with the cooldown.</param>
        /// <param name="remaining">Remaining cooldown duration.</param>
        public static void PublishCooldownMessage(SkillType skill, TimeSpan remaining)
        {
            if (!TryGetProfile(skill, out var profile))
                return;

            PublishCooldownMessage(profile, remaining);
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

            if (!TryGetRemaining(tracker, requestedSkill, out var remaining))
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
                StartCooldown(tracker, CombatSkills[i]);

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
                    ClearCooldown(tracker, CombatSkills[i]);
            }

            CompanionManager.HandleCombatDeclineCooldownCleared();
        }

        /// <summary>
        /// Publishes a companion chat line describing how long remains on the combat cooldown.
        /// </summary>
        private static void PublishCombatCooldownMessage(TimeSpan remaining)
        {
            PublishCooldownMessage(SkillType.Attack, remaining);
        }

        /// <summary>
        /// Publishes a companion chat line using the supplied cooldown profile.
        /// </summary>
        /// <param name="profile">Cooldown profile that supplies the chat line factory.</param>
        /// <param name="remaining">Remaining cooldown duration.</param>
        private static void PublishCooldownMessage(CompanionSkillCooldownProfile profile, TimeSpan remaining)
        {
            PublishSkillCooldownMessage(remaining, profile.ChatLineFactory);
        }

        /// <summary>
        /// Publishes a companion chat cooldown message using the shared sanitisation and rounding rules.
        /// </summary>
        /// <param name="remaining">Remaining cooldown duration.</param>
        /// <param name="lineFactory">Delegate that produces the flavour line using the sanitised player name and rounded minutes.</param>
        private static void PublishSkillCooldownMessage(TimeSpan remaining, Func<string, int, string> lineFactory)
        {
            if (lineFactory == null)
                throw new ArgumentNullException(nameof(lineFactory));

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;
            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();

            double totalMinutes = Math.Max(0d, remaining.TotalMinutes);
            int minutes = Mathf.Max(1, (int)Math.Ceiling(totalMinutes));

            CompanionChatPublisher.TryPublish(() => lineFactory(safePlayerName, minutes));
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
                if (!TryGetRemaining(tracker, CombatSkills[i], out var skillRemaining))
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

        /// <summary>
        /// Attempts to resolve the cooldown profile bound to the supplied skill.
        /// </summary>
        private static bool TryGetProfile(
            SkillType skill,
            out CompanionSkillCooldownProfile profile)
        {
            if (CooldownProfiles.TryGetValue(skill, out profile))
                return true;

            profile = default;
            return false;
        }

        /// <summary>
        /// Attempts to resolve the remaining time for the supplied skill cooldown.
        /// </summary>
        private static bool TryGetRemaining(
            CompanionSkillCooldownTracker tracker,
            SkillType skill,
            out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;

            if (tracker == null)
                return false;

            if (!tracker.TryGetRemaining(skill, out var candidate) || candidate <= TimeSpan.Zero)
                return false;

            remaining = candidate;
            return true;
        }
    }
}
