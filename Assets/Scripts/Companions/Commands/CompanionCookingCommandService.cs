using System;
using Skills.Cooking;
using UnityEngine;

namespace Companions.Commands
{
    /// <summary>
    /// Provides cooking-specific companion command helpers including inventory fallback routing.
    /// </summary>
    public sealed class CompanionCookingCommandService
    {
        private const string LogPrefix = "[Companion]";
        private const string SkillName = "Cooking";

        /// <summary>
        /// Attempts to command the companion to cook at the supplied station with the provided recipe.
        /// </summary>
        /// <param name="companionController">Live companion controller (may be null).</param>
        /// <param name="cookingController">Cooking controller exposed by the companion.</param>
        /// <param name="station">Target station.</param>
        /// <param name="recipe">Recipe that should be prepared.</param>
        /// <param name="result">Detailed result produced by the controller.</param>
        /// <param name="inventoryFallback">Optional callback that deposits items before retrying when the inventory is full.</param>
        /// <returns>True when the command was accepted.</returns>
        public bool TryCommandCook(
            CompanionController companionController,
            CompanionCookingController cookingController,
            CookingObject station,
            CookableRecipe recipe,
            out CompanionCookingCommandResult result,
            Func<bool> inventoryFallback)
        {
            result = CompanionCookingCommandResult.RequirementsNotMet;

            if (station == null)
            {
                Debug.LogWarning($"{LogPrefix} Cannot command cooking: station reference was null.");
                if (cookingController != null)
                    cookingController.PublishCookingCommandFailure(CompanionCookingCommandResult.StationUnavailable);
                else
                    CompanionCookingController.PublishCookingFailureLine(CompanionCookingCommandResult.StationUnavailable);
                return false;
            }

            CompanionCookingCommandResult capturedResult = CompanionCookingCommandResult.RequirementsNotMet;

            var request = new CompanionSkillCommandRouter.SingleTargetCommandRequest<CompanionCookingController, CompanionCookingCommandResult>
            {
                SkillName = SkillName,
                LogPrefix = LogPrefix,
                CooldownCheck = CompanionSkillCooldownTimers.ShouldDeclineCookingRequest,
                CommandExecutor = (CompanionCookingController controller, out CompanionCookingCommandResult commandResult) => controller.TryCommandCook(station, recipe, out commandResult),
                TreatResultAsSuccess = commandResult => commandResult == CompanionCookingCommandResult.InventoryFull,
                CooldownMessageBuilder = commandResult => $"{LogPrefix} Cooking command outcome: accepted=False (result={commandResult}).",
                OutcomeMessageBuilder = (accepted, commandResult) => $"{LogPrefix} Cooking command outcome: accepted={accepted} (result={commandResult}).",
                OnFailure = commandResult =>
                {
                    if (cookingController != null)
                        cookingController.PublishCookingCommandFailure(commandResult);
                    else
                        CompanionCookingController.PublishCookingFailureLine(commandResult);
                },
                OnSuccess = commandResult =>
                {
                    if (commandResult == CompanionCookingCommandResult.Accepted)
                    {
                        if (cookingController != null)
                            cookingController.PublishCookingCommandStart();
                        else
                            CompanionCookingController.PublishCookingStartLine();
                    }
                },
                ShouldAttemptInventoryFallback = commandResult => commandResult == CompanionCookingCommandResult.InventoryFull,
                InventoryFallback = inventoryFallback,
                FallbackExecutor = (CompanionCookingController controller, out CompanionCookingCommandResult retryResult) => controller.TryCommandCook(station, recipe, out retryResult),
                ResultObserver = commandResult => capturedResult = commandResult
            };

            bool accepted = CompanionSkillCommandRouter.TryExecuteSingleTarget(companionController, cookingController, request);
            result = capturedResult;
            return accepted;
        }

        /// <summary>
        /// Attempts to command the companion to cook at any nearby station.
        /// </summary>
        /// <param name="companionController">Live companion controller (may be null).</param>
        /// <param name="cookingController">Cooking controller exposed by the companion.</param>
        /// <param name="radius">Radius (in Unity units) to scan for stations.</param>
        /// <param name="failureReason">Detailed failure reason when the command is rejected.</param>
        /// <param name="inventoryFallback">Optional callback that deposits items before retrying when the inventory is full.</param>
        /// <returns>True when the command was accepted.</returns>
        public bool TryCommandCookNearby(
            CompanionController companionController,
            CompanionCookingController cookingController,
            float radius,
            out CompanionCookingCommandResult failureReason,
            Func<bool> inventoryFallback)
        {
            var request = new CompanionSkillCommandRouter.AreaCommandRequest<CompanionCookingController, CompanionCookingCommandResult>
            {
                SkillName = SkillName,
                LogPrefix = LogPrefix,
                GuardRejectionResult = CompanionCookingCommandResult.RequirementsNotMet,
                CooldownCheck = CompanionSkillCooldownTimers.ShouldDeclineCookingRequest,
                CommandExecutor = (CompanionCookingController controller, float scanRadius, out CompanionCookingCommandResult result) => controller.TryStartAreaCooking(scanRadius, out result),
                TreatFailureAsSuccess = result => result == CompanionCookingCommandResult.InventoryFull,
                CooldownMessageBuilder = (scanRadius, result) => $"{LogPrefix} Area cooking command outcome: success=False, radius={scanRadius}, reason=Cooldown active.",
                OutcomeMessageBuilder = (accepted, result, scanRadius) =>
                {
                    if (accepted)
                    {
                        string detail = result == CompanionCookingCommandResult.InventoryFull
                            ? "Inventory full."
                            : "Area cooking routine started successfully.";
                        return $"{LogPrefix} Area cooking command outcome: success=True, radius={scanRadius}, reason={detail}";
                    }

                    string rejectionDetail = $"The cooking controller rejected the area cooking request ({result}).";
                    return $"{LogPrefix} Area cooking command outcome: success=False, radius={scanRadius}, reason={rejectionDetail}.";
                },
                OnFailure = result =>
                {
                    if (cookingController != null)
                        cookingController.PublishCookingCommandFailure(result);
                    else
                        CompanionCookingController.PublishCookingFailureLine(result);
                },
                OnSuccess = result =>
                {
                    if (result == CompanionCookingCommandResult.Accepted)
                    {
                        if (cookingController != null)
                            cookingController.PublishCookingCommandStart();
                        else
                            CompanionCookingController.PublishCookingStartLine();
                    }
                },
                ShouldAttemptInventoryFallback = result => result == CompanionCookingCommandResult.InventoryFull,
                InventoryFallback = inventoryFallback,
                FallbackExecutor = (CompanionCookingController controller, float scanRadius, out CompanionCookingCommandResult retryResult) => controller.TryStartAreaCooking(scanRadius, out retryResult)
            };

            return CompanionSkillCommandRouter.TryExecuteArea(companionController, cookingController, radius, request, out failureReason);
        }
    }
}
