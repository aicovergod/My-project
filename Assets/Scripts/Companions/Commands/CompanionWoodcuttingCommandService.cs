using Skills.Woodcutting;
using UnityEngine;

namespace Companions.Commands
{
    /// <summary>
    /// Provides woodcutting-specific companion command helpers.
    /// </summary>
    public sealed class CompanionWoodcuttingCommandService : CompanionGatheringCommandServiceBase
    {
        private const string SkillNameConst = "Woodcutting";

        /// <summary>
        /// Initializes a new instance of the <see cref="CompanionWoodcuttingCommandService"/> class.
        /// </summary>
        public CompanionWoodcuttingCommandService()
            : base(SkillNameConst)
        {
        }

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

            var request = CreateSingleTargetRequest<CompanionWoodcuttingController, CompanionWoodcuttingCommandResult>(
                CompanionSkillCooldownTimers.ShouldDeclineWoodcuttingRequest,
                (controller, out CompanionWoodcuttingCommandResult result) => controller.TryCommandChop(tree, out result),
                result => result == CompanionWoodcuttingCommandResult.InventoryFull,
                result => $"{LogPrefix} Woodcutting command outcome: accepted=False (result={result}).",
                (accepted, result) => $"{LogPrefix} Woodcutting command outcome: accepted={accepted} (result={result}).");

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
            var request = CreateAreaCommandRequest<CompanionWoodcuttingController, CompanionWoodcuttingCommandResult>(
                CompanionWoodcuttingCommandResult.RequirementsNotMet,
                CompanionSkillCooldownTimers.ShouldDeclineWoodcuttingRequest,
                (controller, scanRadius, out CompanionWoodcuttingCommandResult result) => controller.TryStartAreaWoodcutting(scanRadius, out result),
                result => result == CompanionWoodcuttingCommandResult.InventoryFull,
                (scanRadius, result) => $"{LogPrefix} Area woodcutting command outcome: success=False, radius={scanRadius}, reason=Cooldown active.",
                (accepted, result, scanRadius) =>
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
                });

            return CompanionSkillCommandRouter.TryExecuteArea(companionController, woodcuttingController, radius, request, out failureReason);
        }
    }
}
