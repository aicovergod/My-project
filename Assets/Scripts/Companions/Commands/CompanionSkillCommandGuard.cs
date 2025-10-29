using System;
using UnityEngine;

namespace Companions.Commands
{
    /// <summary>
    /// Provides shared guard helpers for companion skill commands so controller, activation, and cooldown
    /// checks stay consistent across the different gathering skills.
    /// </summary>
    public static class CompanionSkillCommandGuard
    {
        /// <summary>
        /// Delegate used for cooldown validation. Mirrors <see cref="CompanionSkillCooldownTimers"/> guard methods.
        /// </summary>
        /// <typeparam name="TResult">Result enum reported by the concrete skill controller.</typeparam>
        /// <param name="tracker">Cooldown tracker bound to the active companion.</param>
        /// <param name="result">Result that describes the cooldown rejection reason.</param>
        /// <returns>True when the command should be declined because a cooldown is still active.</returns>
        public delegate bool CooldownCheck<TResult>(CompanionSkillCooldownTracker tracker, out TResult result);

        /// <summary>
        /// Delegate used to execute a single-target command against a companion skill controller.
        /// </summary>
        /// <typeparam name="TController">Type of the concrete skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum reported by the controller.</typeparam>
        /// <param name="controller">Controller instance that should process the command.</param>
        /// <param name="result">Detailed result emitted by the controller.</param>
        /// <returns>True when the command was accepted.</returns>
        public delegate bool SingleCommandExecutor<TController, TResult>(TController controller, out TResult result);

        /// <summary>
        /// Delegate used to execute an area command that scans for nearby targets before starting the routine.
        /// </summary>
        /// <typeparam name="TController">Type of the concrete skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum reported by the controller.</typeparam>
        /// <param name="controller">Controller instance that should process the command.</param>
        /// <param name="radius">Radius that should be scanned for valid targets.</param>
        /// <param name="result">Detailed result emitted by the controller.</param>
        /// <returns>True when the command was accepted.</returns>
        public delegate bool AreaCommandExecutor<TController, TResult>(TController controller, float radius, out TResult result);

        /// <summary>
        /// Executes a companion skill command after validating controller state, active companion status,
        /// and cooldown windows. Optionally re-attempts the command when an inventory overflow fallback succeeds.
        /// </summary>
        /// <typeparam name="TController">Type of the concrete skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum reported by the controller.</typeparam>
        /// <param name="companionController">Live companion controller instance.</param>
        /// <param name="skillController">Skill controller that should handle the command.</param>
        /// <param name="controllerMissingMessage">Log message emitted when the companion controller is null.</param>
        /// <param name="skillControllerMissingMessage">Log message emitted when the skill controller is null.</param>
        /// <param name="inactiveCompanionMessage">Log message emitted when the companion is currently inactive.</param>
        /// <param name="cooldownCheck">Cooldown guard delegate for the specific skill.</param>
        /// <param name="cooldownMessageBuilder">Builds the log message when a cooldown rejects the command.</param>
        /// <param name="commandExecutor">Delegate that routes the command to the concrete controller.</param>
        /// <param name="treatResultAsSuccess">Optional predicate that flips rejection results into success responses (e.g. inventory full).</param>
        /// <param name="logOutcome">Callback used to emit the success/failure log.</param>
        /// <param name="onFailure">Invoked when the command ultimately fails.</param>
        /// <param name="onSuccess">Invoked when the command completes successfully.</param>
        /// <param name="shouldAttemptInventoryFallback">Optional predicate that indicates whether the inventory fallback should run.</param>
        /// <param name="inventoryFallback">Optional fallback that tries to free inventory space before retrying.</param>
        /// <param name="fallbackCommandExecutor">Command executor used after a successful inventory fallback.</param>
        /// <returns>True when the command is accepted (either immediately or after any fallback logic).</returns>
        public static bool TryExecuteSingleTargetCommand<TController, TResult>(
            CompanionController companionController,
            TController skillController,
            string controllerMissingMessage,
            string skillControllerMissingMessage,
            string inactiveCompanionMessage,
            CooldownCheck<TResult> cooldownCheck,
            Func<TResult, string> cooldownMessageBuilder,
            SingleCommandExecutor<TController, TResult> commandExecutor,
            Func<TResult, bool> treatResultAsSuccess,
            Action<bool, TResult> logOutcome,
            Action<TResult> onFailure = null,
            Action<TResult> onSuccess = null,
            Func<TResult, bool> shouldAttemptInventoryFallback = null,
            Func<bool> inventoryFallback = null,
            SingleCommandExecutor<TController, TResult> fallbackCommandExecutor = null)
        {
            if (companionController == null)
            {
                if (!string.IsNullOrEmpty(controllerMissingMessage))
                    Debug.LogWarning(controllerMissingMessage);
                return false;
            }

            if (Equals(skillController, null))
            {
                if (!string.IsNullOrEmpty(skillControllerMissingMessage))
                    Debug.LogWarning(skillControllerMissingMessage);
                return false;
            }

            if (!CompanionManager.HasActiveCompanion)
            {
                if (!string.IsNullOrEmpty(inactiveCompanionMessage))
                    Debug.LogWarning(inactiveCompanionMessage);
                return false;
            }

            if (cooldownCheck != null && cooldownCheck(companionController.SkillCooldowns, out var cooldownResult))
            {
                string cooldownMessage = cooldownMessageBuilder != null ? cooldownMessageBuilder(cooldownResult) : string.Empty;
                if (!string.IsNullOrEmpty(cooldownMessage))
                    Debug.LogWarning(cooldownMessage);
                return false;
            }

            if (commandExecutor == null)
                throw new ArgumentNullException(nameof(commandExecutor));

            bool accepted = commandExecutor(skillController, out var result);

            if (!accepted && shouldAttemptInventoryFallback != null && shouldAttemptInventoryFallback(result))
            {
                bool fallbackTriggered = inventoryFallback != null && inventoryFallback();
                if (fallbackTriggered && fallbackCommandExecutor != null)
                {
                    accepted = fallbackCommandExecutor(skillController, out result);
                }
            }

            if (!accepted && treatResultAsSuccess != null && treatResultAsSuccess(result))
            {
                accepted = true;
            }

            logOutcome?.Invoke(accepted, result);

            if (accepted)
            {
                onSuccess?.Invoke(result);
                return true;
            }

            onFailure?.Invoke(result);
            return false;
        }

        /// <summary>
        /// Executes an area-based companion skill command with the shared guard checks and optional inventory fallback.
        /// </summary>
        /// <typeparam name="TController">Type of the concrete skill controller.</typeparam>
        /// <typeparam name="TResult">Result enum reported by the controller.</typeparam>
        /// <param name="companionController">Live companion controller instance.</param>
        /// <param name="skillController">Skill controller that should handle the command.</param>
        /// <param name="radius">Scan radius supplied by the caller.</param>
        /// <param name="controllerMissingMessageFactory">Builds the log message when the companion controller is null.</param>
        /// <param name="controllerMissingFailure">Failure result returned when the companion controller is missing.</param>
        /// <param name="skillControllerMissingMessageFactory">Builds the log message when the skill controller is null.</param>
        /// <param name="skillControllerMissingFailure">Failure result returned when the skill controller is missing.</param>
        /// <param name="inactiveCompanionMessageFactory">Builds the log message when the companion is inactive.</param>
        /// <param name="inactiveCompanionFailure">Failure result returned when the companion is inactive.</param>
        /// <param name="cooldownCheck">Cooldown guard delegate for the specific skill.</param>
        /// <param name="cooldownMessageFactory">Builds the log message when a cooldown rejects the command.</param>
        /// <param name="commandExecutor">Delegate that routes the area command to the controller.</param>
        /// <param name="treatFailureAsSuccess">Optional predicate that flips failure results into success (e.g. inventory full).</param>
        /// <param name="logOutcome">Callback used to emit the success/failure log once the controller responds.</param>
        /// <param name="onFailure">Invoked when the command ultimately fails.</param>
        /// <param name="onSuccess">Invoked when the command completes successfully (excluding forced successes).</param>
        /// <param name="failureReason">Detailed failure result when the guard or controller rejects the command.</param>
        /// <param name="shouldAttemptInventoryFallback">Optional predicate that indicates whether the inventory fallback should run.</param>
        /// <param name="inventoryFallback">Optional fallback that tries to free inventory space before retrying.</param>
        /// <param name="fallbackCommandExecutor">Command executor used after a successful inventory fallback.</param>
        /// <returns>True when the command is accepted (either immediately or after any fallback logic).</returns>
        public static bool TryExecuteAreaCommand<TController, TResult>(
            CompanionController companionController,
            TController skillController,
            float radius,
            Func<float, string> controllerMissingMessageFactory,
            TResult controllerMissingFailure,
            Func<float, string> skillControllerMissingMessageFactory,
            TResult skillControllerMissingFailure,
            Func<float, string> inactiveCompanionMessageFactory,
            TResult inactiveCompanionFailure,
            CooldownCheck<TResult> cooldownCheck,
            Func<float, TResult, string> cooldownMessageFactory,
            AreaCommandExecutor<TController, TResult> commandExecutor,
            Func<TResult, bool> treatFailureAsSuccess,
            Action<bool, TResult, float> logOutcome,
            Action<TResult> onFailure,
            Action<TResult> onSuccess,
            out TResult failureReason,
            Func<TResult, bool> shouldAttemptInventoryFallback = null,
            Func<bool> inventoryFallback = null,
            AreaCommandExecutor<TController, TResult> fallbackCommandExecutor = null)
        {
            if (companionController == null)
            {
                failureReason = controllerMissingFailure;
                string message = controllerMissingMessageFactory != null ? controllerMissingMessageFactory(radius) : string.Empty;
                if (!string.IsNullOrEmpty(message))
                    Debug.LogWarning(message);
                return false;
            }

            if (Equals(skillController, null))
            {
                failureReason = skillControllerMissingFailure;
                string message = skillControllerMissingMessageFactory != null ? skillControllerMissingMessageFactory(radius) : string.Empty;
                if (!string.IsNullOrEmpty(message))
                    Debug.LogWarning(message);
                return false;
            }

            if (!CompanionManager.HasActiveCompanion)
            {
                failureReason = inactiveCompanionFailure;
                string message = inactiveCompanionMessageFactory != null ? inactiveCompanionMessageFactory(radius) : string.Empty;
                if (!string.IsNullOrEmpty(message))
                    Debug.LogWarning(message);
                return false;
            }

            if (cooldownCheck != null && cooldownCheck(companionController.SkillCooldowns, out var cooldownResult))
            {
                failureReason = cooldownResult;
                string cooldownMessage = cooldownMessageFactory != null ? cooldownMessageFactory(radius, cooldownResult) : string.Empty;
                if (!string.IsNullOrEmpty(cooldownMessage))
                    Debug.LogWarning(cooldownMessage);
                return false;
            }

            if (commandExecutor == null)
                throw new ArgumentNullException(nameof(commandExecutor));

            bool accepted = commandExecutor(skillController, radius, out failureReason);
            bool forcedSuccess = false;

            if (!accepted && shouldAttemptInventoryFallback != null && shouldAttemptInventoryFallback(failureReason))
            {
                bool fallbackTriggered = inventoryFallback != null && inventoryFallback();
                if (fallbackTriggered && fallbackCommandExecutor != null)
                {
                    accepted = fallbackCommandExecutor(skillController, radius, out failureReason);
                }
            }

            if (!accepted && treatFailureAsSuccess != null && treatFailureAsSuccess(failureReason))
            {
                accepted = true;
                forcedSuccess = true;
            }

            logOutcome?.Invoke(accepted, failureReason, radius);

            if (accepted)
            {
                if (!forcedSuccess)
                    onSuccess?.Invoke(failureReason);
                return true;
            }

            onFailure?.Invoke(failureReason);
            return false;
        }
    }
}
