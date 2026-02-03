using Companions;
using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    /// Companion-specific mining HUD that mirrors the player's visuals for the active rock.
    /// </summary>
    public sealed class CompanionMiningHUD : MiningHudBase<CompanionMiningHUD>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static CompanionMiningHUD CreateInstance()
        {
            var go = new GameObject(nameof(CompanionMiningHUD));
            return go.AddComponent<CompanionMiningHUD>();
        }

        /// <inheritdoc />
        protected override string ResolveProgressRootName()
        {
            return "CompanionMiningProgress";
        }

        /// <inheritdoc />
        protected override string ResolveToolRootName()
        {
            return "CompanionMiningPickaxe";
        }

        /// <inheritdoc />
        protected override MiningSkill LocateSkill()
        {
            var companionObject = CompanionManager.CompanionObject;
            if (companionObject != null)
            {
                var skill = companionObject.GetComponentInChildren<MiningSkill>(true);
                if (skill != null)
                    return skill;
            }

            var controller = Object.FindObjectOfType<CompanionController>(true);
            if (controller != null)
            {
                var skill = ResolveCompanionSkill(controller);
                if (skill != null)
                    return skill;
            }

            var miningControllers = Object.FindObjectsOfType<CompanionMiningController>(true);
            for (int i = 0; i < miningControllers.Length; i++)
            {
                var controllerCandidate = miningControllers[i];
                if (controllerCandidate == null)
                    continue;

                var skill = ResolveCompanionSkill(controllerCandidate);
                if (skill != null)
                    return skill;
            }

            var allSkills = Object.FindObjectsOfType<MiningSkill>(true);
            for (int i = 0; i < allSkills.Length; i++)
            {
                var candidate = allSkills[i];
                if (IsCompanionSkill(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Resolves the mining skill attached to the supplied companion behaviour.
        /// </summary>
        private static MiningSkill ResolveCompanionSkill(Component behaviour)
        {
            if (behaviour == null)
                return null;

            var skill = behaviour.GetComponent<MiningSkill>();
            if (skill != null)
                return skill;

            return behaviour.GetComponentInChildren<MiningSkill>(true);
        }
    }
}
