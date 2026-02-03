using Companions;
using UnityEngine;

namespace Skills.Fishing
{
    /// <summary>
    /// Companion-specific fishing HUD that mirrors the player's visuals for the active spot.
    /// </summary>
    public sealed class CompanionFishingHUD : FishingHudBase<CompanionFishingHUD>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static CompanionFishingHUD CreateInstance()
        {
            var go = new GameObject(nameof(CompanionFishingHUD));
            return go.AddComponent<CompanionFishingHUD>();
        }

        /// <inheritdoc />
        protected override string ResolveProgressRootName()
        {
            return "CompanionFishingProgress";
        }

        /// <inheritdoc />
        protected override string ResolveToolRootName()
        {
            return "CompanionFishingTool";
        }

        /// <inheritdoc />
        protected override FishingSkill LocateSkill()
        {
            var companionObject = CompanionManager.CompanionObject;
            if (companionObject != null)
            {
                var skill = companionObject.GetComponentInChildren<FishingSkill>(true);
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

            var fishingControllers = Object.FindObjectsOfType<CompanionFishingController>(true);
            for (int i = 0; i < fishingControllers.Length; i++)
            {
                var controllerCandidate = fishingControllers[i];
                if (controllerCandidate == null)
                    continue;

                var skill = ResolveCompanionSkill(controllerCandidate);
                if (skill != null)
                    return skill;
            }

            var allSkills = Object.FindObjectsOfType<FishingSkill>(true);
            for (int i = 0; i < allSkills.Length; i++)
            {
                var candidate = allSkills[i];
                if (IsCompanionSkill(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Resolves the fishing skill attached to the supplied companion behaviour.
        /// </summary>
        private static FishingSkill ResolveCompanionSkill(Component behaviour)
        {
            if (behaviour == null)
                return null;

            var skill = behaviour.GetComponent<FishingSkill>();
            if (skill != null)
                return skill;

            return behaviour.GetComponentInChildren<FishingSkill>(true);
        }
    }
}
