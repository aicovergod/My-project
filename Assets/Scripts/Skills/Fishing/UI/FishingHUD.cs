using Inventory;
using Skills.Common.UI;
using UnityEngine;

namespace Skills.Fishing
{
    /// <summary>
    /// Presents fishing progress above the active spot, including the equipped tool sprite and tick timing.
    /// </summary>
    public class FishingHUD : GatheringToolHudBase<FishingHUD, FishingSkill>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static FishingHUD CreateInstance()
        {
            var go = new GameObject(nameof(FishingHUD));
            return go.AddComponent<FishingHUD>();
        }

        protected override string ProgressRootName => "FishingProgress";

        protected override string ToolRootName => "FishingTool";

        protected override bool IsGatheringActive => skill != null && skill.IsFishing;

        protected override int ResolveProgressIntervalTicks()
        {
            return skill != null ? skill.CurrentCatchIntervalTicks : 0;
        }

        protected override Sprite ResolveToolSprite()
        {
            var tool = skill?.CurrentTool;
            if (tool == null)
                return null;

            return GatheringToolIconResolver.GetIcon(tool.Id);
        }

        protected override void OnSkillLocated(FishingSkill located)
        {
            located.OnStartFishing += HandleStart;
            located.OnStopFishing += HandleStop;
        }

        protected override void OnSkillDetached(FishingSkill previous)
        {
            previous.OnStartFishing -= HandleStart;
            previous.OnStopFishing -= HandleStop;
        }

        private void HandleStart(FishableSpot spot)
        {
            if (spot == null)
            {
                EndTrackingTarget();
                return;
            }

            var renderer = spot.GetComponent<SpriteRenderer>();
            BeginTrackingTarget(spot.transform, renderer);
        }

        private void HandleStop()
        {
            EndTrackingTarget();
        }
    }
}
