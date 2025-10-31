using Skills.Mining;
using UnityEngine;

namespace Companions.Commands
{
    /// <summary>
    /// Provides mining-specific companion command helpers so <see cref="CompanionManager"/> stays focused on wiring dependencies.
    /// </summary>
    public sealed class CompanionMiningCommandService
    {
        private const string LogPrefix = "[Companion]";
        private const string SkillName = "Mining";

        /// <summary>
        /// Attempts to route a mining command to the supplied controllers.
        /// </summary>
        /// <param name="companionController">Live companion controller (may be null).</param>
        /// <param name="miningController">Mining controller exposed by the companion.</param>
        /// <param name="rock">Target rock requested by the player.</param>
        /// <returns>True when the command was accepted.</returns>
        public bool TryCommandMine(CompanionController companionController, CompanionMiningController miningController, MineableRock rock)
        {
            if (rock == null)
            {
                Debug.LogWarning($"{LogPrefix} Cannot command mining: target rock reference was null.");
                return false;
            }

            var request = new CompanionSkillCommandRouter.SingleTargetCommandRequest<CompanionMiningController, CompanionMiningCommandResult>
            {
                SkillName = SkillName,
                LogPrefix = LogPrefix,
                CooldownCheck = CompanionSkillCooldownTimers.ShouldDeclineMiningRequest,
                CommandExecutor = (controller, out CompanionMiningCommandResult result) => controller.TryCommandMine(rock, out result),
                TreatResultAsSuccess = result => result == CompanionMiningCommandResult.InventoryFull,
                CooldownMessageBuilder = result => $"{LogPrefix} Mining command outcome: accepted=False (result={result}).",
                OutcomeMessageBuilder = (accepted, result) => $"{LogPrefix} Mining command outcome: accepted={accepted} (result={result})."
            };

            return CompanionSkillCommandRouter.TryExecuteSingleTarget(companionController, miningController, request);
        }

        /// <summary>
        /// Attempts to start an area mining routine within the provided radius.
        /// </summary>
        /// <param name="companionController">Live companion controller (may be null).</param>
        /// <param name="miningController">Mining controller exposed by the companion.</param>
        /// <param name="radius">Radius (in Unity units) to scan for rocks.</param>
        /// <param name="failureReason">Detailed failure reason when the command is rejected.</param>
        /// <returns>True when the command was accepted.</returns>
        public bool TryCommandMineNearby(
            CompanionController companionController,
            CompanionMiningController miningController,
            float radius,
            out CompanionMiningCommandResult failureReason)
        {
            var request = new CompanionSkillCommandRouter.AreaCommandRequest<CompanionMiningController, CompanionMiningCommandResult>
            {
                SkillName = SkillName,
                LogPrefix = LogPrefix,
                GuardRejectionResult = CompanionMiningCommandResult.RequirementsNotMet,
                CooldownCheck = CompanionSkillCooldownTimers.ShouldDeclineMiningRequest,
                CommandExecutor = (controller, scanRadius, out CompanionMiningCommandResult result) => controller.TryStartAreaMining(scanRadius, out result),
                TreatFailureAsSuccess = result => result == CompanionMiningCommandResult.InventoryFull,
                CooldownMessageBuilder = (scanRadius, result) => $"{LogPrefix} Area mining command outcome: success=False, radius={scanRadius}, reason=Cooldown active.",
                OutcomeMessageBuilder = (accepted, result, scanRadius) =>
                {
                    if (accepted)
                    {
                        string detail = result == CompanionMiningCommandResult.InventoryFull
                            ? "Area mining aborted because the companion inventory is full."
                            : "Area mining routine started successfully.";
                        return $"{LogPrefix} Area mining command outcome: success=True, radius={scanRadius}, reason={detail}";
                    }

                    string rejectionDetail = $"The mining controller rejected the area mining request ({result}).";
                    return $"{LogPrefix} Area mining command outcome: success=False, radius={scanRadius}, reason={rejectionDetail}";
                }
            };

            return CompanionSkillCommandRouter.TryExecuteArea(companionController, miningController, radius, request, out failureReason);
        }
    }
}
