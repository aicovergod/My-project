using Skills.Common.UI;
using UnityEngine;
using World;

namespace Skills.Cooking
{
    /// <summary>
    ///     Displays a world-space progress bar whenever the player is actively cooking items.
    ///     The HUD mirrors the shared gathering HUD lifecycle so it survives scene loads and
    ///     automatically binds to the local <see cref="CookingSkill"/> instance when the player spawns.
    /// </summary>
    public sealed class CookingHUD : TickedProgressHudBase<CookingHUD, CookingSkill>
    {
        private const string HudName = nameof(CookingHUD);

        private Transform activeStationAnchor;

        /// <summary>
        ///     Tolerance used when detecting when a new cooking cycle starts so the bar can reset cleanly.
        /// </summary>
        private const float CookingProgressResetThreshold = 0.001f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static CookingHUD CreateInstance()
        {
            var go = new GameObject(HudName);
            return go.AddComponent<CookingHUD>();
        }

        protected override string ProgressBarName => "CookingProgress";

        protected override float ProgressResetThreshold => CookingProgressResetThreshold;

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
            _ = recipe; // The recipe is retained for future HUD enhancements (e.g. flavour text overlays).
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
    }
}
