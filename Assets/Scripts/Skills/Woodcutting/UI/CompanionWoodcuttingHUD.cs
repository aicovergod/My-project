using Companions;
using UnityEngine;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Companion-specific woodcutting HUD that mirrors the player's visuals for the active tree.
    /// </summary>
    public sealed class CompanionWoodcuttingHUD : WoodcuttingHudBase<CompanionWoodcuttingHUD>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static CompanionWoodcuttingHUD CreateInstance()
        {
            var go = new GameObject(nameof(CompanionWoodcuttingHUD));
            return go.AddComponent<CompanionWoodcuttingHUD>();
        }

        /// <inheritdoc />
        protected override string ResolveProgressRootName()
        {
            return "CompanionWoodcuttingProgress";
        }

        /// <inheritdoc />
        protected override string ResolveToolRootName()
        {
            return "CompanionWoodcuttingAxe";
        }

        /// <inheritdoc />
        protected override WoodcuttingSkill LocateSkill()
        {
            var companionObject = CompanionManager.CompanionObject;
            if (companionObject != null)
            {
                var skill = companionObject.GetComponentInChildren<WoodcuttingSkill>(true);
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

            var woodcuttingControllers = Object.FindObjectsOfType<CompanionWoodcuttingController>(true);
            for (int i = 0; i < woodcuttingControllers.Length; i++)
            {
                var controllerCandidate = woodcuttingControllers[i];
                if (controllerCandidate == null)
                    continue;

                var skill = ResolveCompanionSkill(controllerCandidate);
                if (skill != null)
                    return skill;
            }

            var allSkills = Object.FindObjectsOfType<WoodcuttingSkill>(true);
            for (int i = 0; i < allSkills.Length; i++)
            {
                var candidate = allSkills[i];
                if (IsCompanionSkill(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Resolves the woodcutting skill attached to the supplied companion behaviour.
        /// </summary>
        private static WoodcuttingSkill ResolveCompanionSkill(Component behaviour)
        {
            if (behaviour == null)
                return null;

            var skill = behaviour.GetComponent<WoodcuttingSkill>();
            if (skill != null)
                return skill;

            return behaviour.GetComponentInChildren<WoodcuttingSkill>(true);
        }
    }
}
