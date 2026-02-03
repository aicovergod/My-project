using Companions;
using Skills.Common.UI;
using UnityEngine;

namespace Skills.Cooking
{
    /// <summary>
    /// Companion-specific cooking HUD that mirrors the player's visuals without intercepting player cooking sessions.
    /// </summary>
    public sealed class CompanionCookingHUD : TickedProgressHudBase<CompanionCookingHUD, CookingSkill>
    {
        private const string HudName = nameof(CompanionCookingHUD);
        private const float CookingProgressResetThreshold = 0.001f;

        private Transform activeStationAnchor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static CompanionCookingHUD CreateInstance()
        {
            var go = new GameObject(HudName);
            return go.AddComponent<CompanionCookingHUD>();
        }

        protected override string ProgressBarName => "CompanionCookingProgress";

        protected override float ProgressResetThreshold => CookingProgressResetThreshold;

        protected override CookingSkill LocateSkill()
        {
            var companionObject = CompanionManager.CompanionObject;
            if (companionObject != null)
            {
                var skill = companionObject.GetComponentInChildren<CookingSkill>(true);
                if (IsCompanionSkill(skill))
                    return skill;
            }

            var controller = Object.FindObjectOfType<CompanionController>(true);
            if (controller != null)
            {
                var skill = controller.GetComponentInChildren<CookingSkill>(true);
                if (IsCompanionSkill(skill))
                    return skill;

                var cookingController = controller.GetComponentInChildren<CompanionCookingController>(true);
                if (cookingController != null)
                {
                    var attachedSkill = cookingController.GetComponent<CookingSkill>() ??
                                         cookingController.GetComponentInChildren<CookingSkill>(true);
                    if (IsCompanionSkill(attachedSkill))
                        return attachedSkill;
                }
            }

            var allSkills = Object.FindObjectsOfType<CookingSkill>(true);
            for (int i = 0; i < allSkills.Length; i++)
            {
                var candidate = allSkills[i];
                if (IsCompanionSkill(candidate))
                    return candidate;
            }

            return null;
        }

        protected override void OnSkillLocated(CookingSkill located)
        {
            located.OnStartCooking += HandleStartCooking;
            located.OnStopCooking += HandleStopCooking;
        }

        protected override void OnSkillDetached(CookingSkill previous)
        {
            previous.OnStartCooking -= HandleStartCooking;
            previous.OnStopCooking -= HandleStopCooking;
            StopTrackingProgress();
            activeStationAnchor = null;
        }

        protected override bool IsSkillProgressing()
        {
            return skill != null && skill.IsCooking;
        }

        protected override float GetNormalizedProgress()
        {
            if (skill == null)
                return 0f;

            return Mathf.Clamp01(skill.CookProgressNormalized);
        }

        protected override float CalculateProgressStep()
        {
            if (skill == null)
                return 1f;

            int ticksRequired = Mathf.Max(1, skill.CookTicksPerItem);
            return 1f / ticksRequired;
        }

        protected override bool TryResolveTargetPosition(out Vector3 worldPosition)
        {
            if (skill == null)
            {
                worldPosition = Vector3.zero;
                return false;
            }

            if (activeStationAnchor == null)
                activeStationAnchor = ResolveActiveStationAnchor();

            if (activeStationAnchor != null)
            {
                worldPosition = activeStationAnchor.position;
                return true;
            }

            if (skill.transform != null)
            {
                worldPosition = skill.transform.position;
                return true;
            }

            worldPosition = Vector3.zero;
            return false;
        }

        protected override void OnProgressDeactivated()
        {
            activeStationAnchor = null;
        }

        private void HandleStartCooking(CookableRecipe recipe)
        {
            _ = recipe;
            activeStationAnchor = ResolveActiveStationAnchor();

            if (!TryResolveTargetPosition(out Vector3 startPosition) && skill != null && skill.transform != null)
                startPosition = skill.transform.position;

            BeginProgressTracking(startPosition);
        }

        private void HandleStopCooking()
        {
            StopTrackingProgress();
        }

        private Transform ResolveActiveStationAnchor()
        {
            if (skill != null && skill.ActiveCookingObject != null)
                return skill.ActiveCookingObject.ApproachAnchor;

            return skill != null ? skill.transform : null;
        }

        private static bool IsCompanionSkill(CookingSkill candidate)
        {
            if (candidate == null)
                return false;

            var transform = candidate.transform;
            if (transform == null)
                return false;

            var root = transform.root;
            if (root != null && root.gameObject == CompanionManager.CompanionObject)
                return true;

            if (candidate.GetComponent<CompanionController>() != null ||
                candidate.GetComponentInParent<CompanionController>() != null)
            {
                return true;
            }

            if (candidate.GetComponent<CompanionCookingController>() != null ||
                candidate.GetComponentInParent<CompanionCookingController>() != null)
            {
                return true;
            }

            return false;
        }
    }
}
