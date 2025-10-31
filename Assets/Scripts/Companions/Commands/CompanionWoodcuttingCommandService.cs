using Skills.Woodcutting;
using UnityEngine;

namespace Companions.Commands
{
    /// <summary>
    /// Provides woodcutting-specific companion command helpers.
    /// </summary>
    public sealed class CompanionWoodcuttingCommandService
    {
        private const string LogPrefix = "[Companion]";
        private const string SkillName = "Woodcutting";

        /// <summary>
        /// Attempts to route a woodcutting command to the supplied controllers.
        /// </summary>
        public bool TryCommandChop(CompanionController companionController, CompanionWoodcuttingController woodcuttingController, TreeNode tree)
        {
            if (tree == null)
            {
                Debug.LogWarning($"{LogPrefix} Cannot command woodcutting: target tree reference was null.");
                return false;
            }

            var request = new CompanionSkillCommandRouter.SingleTargetCommandRequest<CompanionWoodcuttingController, CompanionWoodcuttingCommandResult>
            {
                SkillName = SkillName,
                LogPrefix = LogPrefix,
                CooldownCheck = CompanionSkillCooldownTimers.ShouldDeclineWoodcuttingRequest,
                CommandExecutor = (controller, out CompanionWoodcuttingCommandResult result) => controller.TryCommandChop(tree, out result),
                TreatResultAsSuccess = result => result == CompanionWoodcuttingCommandResult.InventoryFull,
                CooldownMessageBuilder = result => $"{LogPrefix} Woodcutting command outcome: accepted=False (result={result}).",
                OutcomeMessageBuilder = (accepted, result) => $"{LogPrefix} Woodcutting command outcome: accepted={accepted} (result={result})."
            };

            return CompanionSkillCommandRouter.TryExecuteSingleTarget(companionController, woodcuttingController, request);
        }

        /// <summary>
        /// Attempts to start an area woodcutting routine within the provided radius.
        /// </summary>
        public bool TryCommandChopNearby(
            CompanionController companionController,
            CompanionWoodcuttingController woodcuttingController,
            float radius,
            out CompanionWoodcuttingCommandResult failureReason)
        {
            var request = new CompanionSkillCommandRouter.AreaCommandRequest<CompanionWoodcuttingController, CompanionWoodcuttingCommandResult>
            {
                SkillName = SkillName,
                LogPrefix = LogPrefix,
                GuardRejectionResult = CompanionWoodcuttingCommandResult.RequirementsNotMet,
                CooldownCheck = CompanionSkillCooldownTimers.ShouldDeclineWoodcuttingRequest,
                CommandExecutor = (controller, scanRadius, out CompanionWoodcuttingCommandResult result) => controller.TryStartAreaWoodcutting(scanRadius, out result),
                TreatFailureAsSuccess = result => result == CompanionWoodcuttingCommandResult.InventoryFull,
                CooldownMessageBuilder = (scanRadius, result) => $"{LogPrefix} Area woodcutting command outcome: success=False, radius={scanRadius}, reason=Cooldown active.",
                OutcomeMessageBuilder = (accepted, result, scanRadius) =>
                {
                    if (accepted)
                    {
                        string detail = result == CompanionWoodcuttingCommandResult.InventoryFull
                            ? "Area woodcutting aborted because the companion inventory is full."
                            : "Area woodcutting routine started successfully.";
                        return $"{LogPrefix} Area woodcutting command outcome: success=True, radius={scanRadius}, reason={detail}";
                    }

                    string rejectionDetail = $"The woodcutting controller rejected the area woodcutting request ({result}).";
                    return $"{LogPrefix} Area woodcutting command outcome: success=False, radius={scanRadius}, reason={rejectionDetail}";
                }
            };

            return CompanionSkillCommandRouter.TryExecuteArea(companionController, woodcuttingController, radius, request, out failureReason);
        }
    }
}
