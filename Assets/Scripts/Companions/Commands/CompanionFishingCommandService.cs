using Skills.Fishing;
using UnityEngine;

namespace Companions.Commands
{
    /// <summary>
    /// Provides fishing-specific companion command helpers.
    /// </summary>
    public sealed class CompanionFishingCommandService
    {
        private const string LogPrefix = "[Companion]";
        private const string SkillName = "Fishing";

        /// <summary>
        /// Attempts to route a fishing command to the supplied controllers.
        /// </summary>
        public bool TryCommandFish(CompanionController companionController, CompanionFishingController fishingController, FishableSpot spot)
        {
            if (spot == null)
            {
                Debug.LogWarning($"{LogPrefix} Cannot command fishing: target spot reference was null.");
                return false;
            }

            var request = new CompanionSkillCommandRouter.SingleTargetCommandRequest<CompanionFishingController, CompanionFishingCommandResult>
            {
                SkillName = SkillName,
                LogPrefix = LogPrefix,
                CooldownCheck = CompanionSkillCooldownTimers.ShouldDeclineFishingRequest,
                CommandExecutor = (controller, out CompanionFishingCommandResult result) => controller.TryCommandFish(spot, out result),
                TreatResultAsSuccess = result => result == CompanionFishingCommandResult.InventoryFull,
                CooldownMessageBuilder = result => $"{LogPrefix} Fishing command outcome: accepted=False (result={result}).",
                OutcomeMessageBuilder = (accepted, result) => $"{LogPrefix} Fishing command outcome: accepted={accepted} (result={result})."
            };

            return CompanionSkillCommandRouter.TryExecuteSingleTarget(companionController, fishingController, request);
        }

        /// <summary>
        /// Attempts to start an area fishing routine within the provided radius.
        /// </summary>
        public bool TryCommandFishNearby(
            CompanionController companionController,
            CompanionFishingController fishingController,
            float radius,
            out CompanionFishingCommandResult failureReason)
        {
            var request = new CompanionSkillCommandRouter.AreaCommandRequest<CompanionFishingController, CompanionFishingCommandResult>
            {
                SkillName = SkillName,
                LogPrefix = LogPrefix,
                GuardRejectionResult = CompanionFishingCommandResult.RequirementsNotMet,
                CooldownCheck = CompanionSkillCooldownTimers.ShouldDeclineFishingRequest,
                CommandExecutor = (controller, scanRadius, out CompanionFishingCommandResult result) => controller.TryStartAreaFishing(scanRadius, out result),
                TreatFailureAsSuccess = result => result == CompanionFishingCommandResult.InventoryFull,
                CooldownMessageBuilder = (scanRadius, result) => $"{LogPrefix} Area fishing command outcome: success=False, radius={scanRadius}, reason=Cooldown active.",
                OutcomeMessageBuilder = (accepted, result, scanRadius) =>
                {
                    if (accepted)
                    {
                        string detail = result == CompanionFishingCommandResult.InventoryFull
                            ? "Area fishing aborted because the companion inventory is full."
                            : "Area fishing routine started successfully.";
                        return $"{LogPrefix} Area fishing command outcome: success=True, radius={scanRadius}, reason={detail}";
                    }

                    string rejectionDetail = $"The fishing controller rejected the area fishing request ({result}).";
                    return $"{LogPrefix} Area fishing command outcome: success=False, radius={scanRadius}, reason={rejectionDetail}";
                }
            };

            return CompanionSkillCommandRouter.TryExecuteArea(companionController, fishingController, radius, request, out failureReason);
        }
    }
}
