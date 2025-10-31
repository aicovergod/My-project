using System;

namespace Companions.Commands
{
    /// <summary>
    /// Provides reusable helpers for gathering companion command services so skill-specific
    /// classes can focus on wiring controller delegates and cooldown hooks.
    /// </summary>
    public abstract class CompanionGatheringCommandServiceBase
    {
        private readonly string skillName;
        private readonly string logPrefix;

        /// <summary>
        /// Initializes the base helper with the shared skill metadata.
        /// </summary>
        /// <param name="skillName">Display name for the skill (e.g. "Mining").</param>
        /// <param name="logPrefix">Prefix emitted in front of all log output.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="skillName"/> is null or whitespace.</exception>
        protected CompanionGatheringCommandServiceBase(string skillName, string logPrefix = "[Companion]")
        {
            if (string.IsNullOrWhiteSpace(skillName))
                throw new ArgumentException("Skill name cannot be null or whitespace.", nameof(skillName));

            this.skillName = skillName;
            logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? "[Companion]" : logPrefix;
            this.logPrefix = logPrefix;
        }

        /// <summary>
        /// Gets the human readable skill name.
        /// </summary>
        protected string SkillName => skillName;

        /// <summary>
        /// Gets the prefix applied to all log output.
        /// </summary>
        protected string LogPrefix => logPrefix;

        /// <summary>
        /// Builds a <see cref="CompanionSkillCommandRouter.SingleTargetCommandRequest{TController, TResult}"/> with the
        /// shared metadata populated so callers only provide skill-specific delegates.
        /// </summary>
        /// <typeparam name="TController">Type of the skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum produced by the controller.</typeparam>
        /// <param name="cooldownCheck">Cooldown guard used to decline commands when timers are active.</param>
        /// <param name="commandExecutor">Delegate that executes the command on the concrete controller.</param>
        /// <param name="treatResultAsSuccess">Optional predicate that re-interprets failure results as successes.</param>
        /// <param name="cooldownMessageBuilder">Optional builder that formats cooldown rejection logs.</param>
        /// <param name="outcomeMessageBuilder">Optional builder that formats outcome logs.</param>
        /// <param name="onFailure">Optional callback invoked when the command ultimately fails.</param>
        /// <param name="onSuccess">Optional callback invoked when the command succeeds.</param>
        /// <param name="shouldAttemptInventoryFallback">Optional predicate that determines whether an inventory fallback should run.</param>
        /// <param name="inventoryFallback">Optional fallback invoked to free inventory space before retrying.</param>
        /// <param name="fallbackExecutor">Optional executor used after a successful inventory fallback.</param>
        /// <param name="resultObserver">Optional observer invoked whenever the guard reports an outcome.</param>
        /// <returns>The configured single target request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="commandExecutor"/> is null.</exception>
        protected CompanionSkillCommandRouter.SingleTargetCommandRequest<TController, TResult> CreateSingleTargetRequest<TController, TResult>(
            CompanionSkillCommandGuard.CooldownCheck<TResult> cooldownCheck,
            CompanionSkillCommandGuard.SingleCommandExecutor<TController, TResult> commandExecutor,
            Func<TResult, bool> treatResultAsSuccess = null,
            Func<TResult, string> cooldownMessageBuilder = null,
            Func<bool, TResult, string> outcomeMessageBuilder = null,
            Action<TResult> onFailure = null,
            Action<TResult> onSuccess = null,
            Func<TResult, bool> shouldAttemptInventoryFallback = null,
            Func<bool> inventoryFallback = null,
            CompanionSkillCommandGuard.SingleCommandExecutor<TController, TResult> fallbackExecutor = null,
            Action<TResult> resultObserver = null)
        {
            if (commandExecutor == null)
                throw new ArgumentNullException(nameof(commandExecutor));

            return new CompanionSkillCommandRouter.SingleTargetCommandRequest<TController, TResult>
            {
                SkillName = skillName,
                LogPrefix = logPrefix,
                CooldownCheck = cooldownCheck,
                CommandExecutor = commandExecutor,
                TreatResultAsSuccess = treatResultAsSuccess,
                CooldownMessageBuilder = cooldownMessageBuilder,
                OutcomeMessageBuilder = outcomeMessageBuilder,
                OnFailure = onFailure,
                OnSuccess = onSuccess,
                ShouldAttemptInventoryFallback = shouldAttemptInventoryFallback,
                InventoryFallback = inventoryFallback,
                FallbackExecutor = fallbackExecutor,
                ResultObserver = resultObserver
            };
        }

        /// <summary>
        /// Builds a <see cref="CompanionSkillCommandRouter.AreaCommandRequest{TController, TResult}"/> with the
        /// shared metadata populated so callers only provide skill-specific delegates.
        /// </summary>
        /// <typeparam name="TController">Type of the skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum produced by the controller.</typeparam>
        /// <param name="guardRejectionResult">Failure result returned when guard checks reject the command.</param>
        /// <param name="cooldownCheck">Cooldown guard used to decline commands when timers are active.</param>
        /// <param name="commandExecutor">Delegate that executes the area command on the concrete controller.</param>
        /// <param name="treatFailureAsSuccess">Optional predicate that re-interprets failure results as successes.</param>
        /// <param name="cooldownMessageBuilder">Optional builder that formats cooldown rejection logs.</param>
        /// <param name="outcomeMessageBuilder">Optional builder that formats outcome logs.</param>
        /// <param name="onFailure">Optional callback invoked when the command ultimately fails.</param>
        /// <param name="onSuccess">Optional callback invoked when the command succeeds.</param>
        /// <param name="shouldAttemptInventoryFallback">Optional predicate that determines whether an inventory fallback should run.</param>
        /// <param name="inventoryFallback">Optional fallback invoked to free inventory space before retrying.</param>
        /// <param name="fallbackExecutor">Optional executor used after a successful inventory fallback.</param>
        /// <returns>The configured area request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="commandExecutor"/> is null.</exception>
        protected CompanionSkillCommandRouter.AreaCommandRequest<TController, TResult> CreateAreaCommandRequest<TController, TResult>(
            TResult guardRejectionResult,
            CompanionSkillCommandGuard.CooldownCheck<TResult> cooldownCheck,
            CompanionSkillCommandGuard.AreaCommandExecutor<TController, TResult> commandExecutor,
            Func<TResult, bool> treatFailureAsSuccess = null,
            Func<float, TResult, string> cooldownMessageBuilder = null,
            Func<bool, TResult, float, string> outcomeMessageBuilder = null,
            Action<TResult> onFailure = null,
            Action<TResult> onSuccess = null,
            Func<TResult, bool> shouldAttemptInventoryFallback = null,
            Func<bool> inventoryFallback = null,
            CompanionSkillCommandGuard.AreaCommandExecutor<TController, TResult> fallbackExecutor = null)
        {
            if (commandExecutor == null)
                throw new ArgumentNullException(nameof(commandExecutor));

            return new CompanionSkillCommandRouter.AreaCommandRequest<TController, TResult>
            {
                SkillName = skillName,
                LogPrefix = logPrefix,
                GuardRejectionResult = guardRejectionResult,
                CooldownCheck = cooldownCheck,
                CommandExecutor = commandExecutor,
                TreatFailureAsSuccess = treatFailureAsSuccess,
                CooldownMessageBuilder = cooldownMessageBuilder,
                OutcomeMessageBuilder = outcomeMessageBuilder,
                OnFailure = onFailure,
                OnSuccess = onSuccess,
                ShouldAttemptInventoryFallback = shouldAttemptInventoryFallback,
                InventoryFallback = inventoryFallback,
                FallbackExecutor = fallbackExecutor
            };
        }
    }
}
