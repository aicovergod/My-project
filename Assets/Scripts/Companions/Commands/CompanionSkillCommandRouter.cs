using System;
using UnityEngine;

namespace Companions.Commands
{
    /// <summary>
    /// Provides reusable wrappers around <see cref="CompanionSkillCommandGuard"/> so per-skill services can
    /// configure logs, cooldown hooks, and fallback behaviour without duplicating boilerplate guard calls.
    /// </summary>
    public static class CompanionSkillCommandRouter
    {
        /// <summary>
        /// Configuration payload used for single-target commands.
        /// </summary>
        /// <typeparam name="TController">Type of the skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum produced by the controller.</typeparam>
        public sealed class SingleTargetCommandRequest<TController, TResult>
        {
            /// <summary>Display name for the skill (e.g. "Mining").</summary>
            public string SkillName { get; set; }

            /// <summary>Prefix emitted in front of all log output (defaults to "[Companion]").</summary>
            public string LogPrefix { get; set; } = "[Companion]";

            /// <summary>Cooldown guard used to decline commands when timers are active.</summary>
            public CompanionSkillCommandGuard.CooldownCheck<TResult> CooldownCheck { get; set; }

            /// <summary>Delegate that executes the command on the concrete controller.</summary>
            public CompanionSkillCommandGuard.SingleCommandExecutor<TController, TResult> CommandExecutor { get; set; }

            /// <summary>Optional predicate that re-interprets failure results as successes.</summary>
            public Func<TResult, bool> TreatResultAsSuccess { get; set; }

            /// <summary>Optional callback invoked when the command ultimately fails.</summary>
            public Action<TResult> OnFailure { get; set; }

            /// <summary>Optional callback invoked when the command succeeds.</summary>
            public Action<TResult> OnSuccess { get; set; }

            /// <summary>Optional predicate that determines whether an inventory fallback should run.</summary>
            public Func<TResult, bool> ShouldAttemptInventoryFallback { get; set; }

            /// <summary>Optional fallback invoked to free inventory space before retrying.</summary>
            public Func<bool> InventoryFallback { get; set; }

            /// <summary>Optional executor used after a successful inventory fallback.</summary>
            public CompanionSkillCommandGuard.SingleCommandExecutor<TController, TResult> FallbackExecutor { get; set; }

            /// <summary>Optional builder that formats cooldown rejection logs.</summary>
            public Func<TResult, string> CooldownMessageBuilder { get; set; }

            /// <summary>Optional builder that formats the outcome log (accepted/result).</summary>
            public Func<bool, TResult, string> OutcomeMessageBuilder { get; set; }

            /// <summary>Optional observer invoked with the command result whenever the guard reports an outcome.</summary>
            public Action<TResult> ResultObserver { get; set; }
        }

        /// <summary>
        /// Configuration payload used for area scan commands.
        /// </summary>
        /// <typeparam name="TController">Type of the skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum produced by the controller.</typeparam>
        public sealed class AreaCommandRequest<TController, TResult>
        {
            /// <summary>Display name for the skill (e.g. "Mining").</summary>
            public string SkillName { get; set; }

            /// <summary>Prefix emitted in front of all log output (defaults to "[Companion]").</summary>
            public string LogPrefix { get; set; } = "[Companion]";

            /// <summary>Failure result returned when guard checks reject the command.</summary>
            public TResult GuardRejectionResult { get; set; }

            /// <summary>Cooldown guard used to decline commands when timers are active.</summary>
            public CompanionSkillCommandGuard.CooldownCheck<TResult> CooldownCheck { get; set; }

            /// <summary>Delegate that executes the area command on the concrete controller.</summary>
            public CompanionSkillCommandGuard.AreaCommandExecutor<TController, TResult> CommandExecutor { get; set; }

            /// <summary>Optional predicate that re-interprets failure results as successes.</summary>
            public Func<TResult, bool> TreatFailureAsSuccess { get; set; }

            /// <summary>Optional callback invoked when the command ultimately fails.</summary>
            public Action<TResult> OnFailure { get; set; }

            /// <summary>Optional callback invoked when the command succeeds.</summary>
            public Action<TResult> OnSuccess { get; set; }

            /// <summary>Optional predicate that determines whether an inventory fallback should run.</summary>
            public Func<TResult, bool> ShouldAttemptInventoryFallback { get; set; }

            /// <summary>Optional fallback invoked to free inventory space before retrying.</summary>
            public Func<bool> InventoryFallback { get; set; }

            /// <summary>Optional executor used after a successful inventory fallback.</summary>
            public CompanionSkillCommandGuard.AreaCommandExecutor<TController, TResult> FallbackExecutor { get; set; }

            /// <summary>Optional builder that formats cooldown rejection logs.</summary>
            public Func<float, TResult, string> CooldownMessageBuilder { get; set; }

            /// <summary>Optional builder that formats the outcome log (success/result/radius).</summary>
            public Func<bool, TResult, float, string> OutcomeMessageBuilder { get; set; }
        }

        /// <summary>
        /// Executes a single-target companion skill command with the supplied configuration.
        /// </summary>
        /// <typeparam name="TController">Type of the skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum produced by the controller.</typeparam>
        /// <param name="companionController">Live companion controller (may be null).</param>
        /// <param name="skillController">Skill controller that should handle the command.</param>
        /// <param name="request">Configuration payload describing cooldown, logging, and fallback behaviour.</param>
        /// <returns>True when the command was accepted.</returns>
        public static bool TryExecuteSingleTarget<TController, TResult>(
            CompanionController companionController,
            TController skillController,
            SingleTargetCommandRequest<TController, TResult> request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.CommandExecutor == null)
                throw new ArgumentNullException(nameof(request.CommandExecutor));

            string skillName = string.IsNullOrEmpty(request.SkillName) ? "Skill" : request.SkillName;
            string prefix = string.IsNullOrEmpty(request.LogPrefix) ? "[Companion]" : request.LogPrefix;
            string skillNameLower = skillName.ToLowerInvariant();

            string controllerMissingMessage =
                $"{prefix} Cannot command {skillNameLower}: companion controller has not been initialised.";
            string skillControllerMissingMessage =
                $"{prefix} Cannot command {skillNameLower}: companion {skillNameLower} controller is missing.";
            string inactiveCompanionMessage =
                $"{prefix} Cannot command {skillNameLower}: the companion is not currently active.";

            Func<TResult, string> cooldownBuilder = request.CooldownMessageBuilder ??
                (result => $"{prefix} {skillName} command outcome: accepted=False (result={result}).");

            return CompanionSkillCommandGuard.TryExecuteSingleTargetCommand(
                companionController,
                skillController,
                controllerMissingMessage,
                skillControllerMissingMessage,
                inactiveCompanionMessage,
                request.CooldownCheck,
                cooldownBuilder,
                request.CommandExecutor,
                request.TreatResultAsSuccess,
                (accepted, result) =>
                {
                    request.ResultObserver?.Invoke(result);

                    string message = request.OutcomeMessageBuilder != null
                        ? request.OutcomeMessageBuilder(accepted, result)
                        : $"{prefix} {skillName} command outcome: accepted={accepted} (result={result}).";

                    if (string.IsNullOrEmpty(message))
                        return;

                    if (accepted)
                        Debug.Log(message);
                    else
                        Debug.LogWarning(message);
                },
                request.OnFailure,
                request.OnSuccess,
                request.ShouldAttemptInventoryFallback,
                request.InventoryFallback,
                request.FallbackExecutor);
        }

        /// <summary>
        /// Executes an area companion skill command with the supplied configuration.
        /// </summary>
        /// <typeparam name="TController">Type of the skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum produced by the controller.</typeparam>
        /// <param name="companionController">Live companion controller (may be null).</param>
        /// <param name="skillController">Skill controller that should handle the command.</param>
        /// <param name="radius">Scan radius provided by the caller.</param>
        /// <param name="request">Configuration payload describing cooldown, logging, and fallback behaviour.</param>
        /// <param name="failureReason">Detailed result emitted when the command fails.</param>
        /// <returns>True when the command was accepted.</returns>
        public static bool TryExecuteArea<TController, TResult>(
            CompanionController companionController,
            TController skillController,
            float radius,
            AreaCommandRequest<TController, TResult> request,
            out TResult failureReason)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (request.CommandExecutor == null)
                throw new ArgumentNullException(nameof(request.CommandExecutor));

            string skillName = string.IsNullOrEmpty(request.SkillName) ? "Skill" : request.SkillName;
            string prefix = string.IsNullOrEmpty(request.LogPrefix) ? "[Companion]" : request.LogPrefix;
            string skillNameLower = skillName.ToLowerInvariant();

            Func<float, string> controllerMissingFactory = r =>
                $"{prefix} Area {skillNameLower} command outcome: success=False, radius={r}, reason=Companion controller has not been initialised.";
            Func<float, string> skillControllerMissingFactory = r =>
                $"{prefix} Area {skillNameLower} command outcome: success=False, radius={r}, reason=Companion {skillNameLower} controller is missing.";
            Func<float, string> inactiveCompanionFactory = r =>
                $"{prefix} Area {skillNameLower} command outcome: success=False, radius={r}, reason=The companion is not currently active.";

            Func<float, TResult, string> cooldownBuilder = request.CooldownMessageBuilder ??
                ((r, result) =>
                    $"{prefix} Area {skillNameLower} command outcome: success=False, radius={r}, reason=Cooldown active.");

            return CompanionSkillCommandGuard.TryExecuteAreaCommand(
                companionController,
                skillController,
                radius,
                controllerMissingFactory,
                request.GuardRejectionResult,
                skillControllerMissingFactory,
                request.GuardRejectionResult,
                inactiveCompanionFactory,
                request.GuardRejectionResult,
                request.CooldownCheck,
                cooldownBuilder,
                request.CommandExecutor,
                request.TreatFailureAsSuccess,
                (accepted, result, scanRadius) =>
                {
                    string message = request.OutcomeMessageBuilder != null
                        ? request.OutcomeMessageBuilder(accepted, result, scanRadius)
                        : $"{prefix} Area {skillNameLower} command outcome: success={accepted}, radius={scanRadius}, result={result}.";

                    if (string.IsNullOrEmpty(message))
                        return;

                    if (accepted)
                        Debug.Log(message);
                    else
                        Debug.LogWarning(message);
                },
                request.OnFailure,
                request.OnSuccess,
                out failureReason,
                request.ShouldAttemptInventoryFallback,
                request.InventoryFallback,
                request.FallbackExecutor);
        }
    }
}
