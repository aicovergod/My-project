using Inventory;
using Skills.Common.UI;
using UnityEngine;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Displays woodcutting progress above the current tree, mirroring the player's axe and tick cadence.
    /// </summary>
    public class WoodcuttingHUD : GatheringToolHudBase<WoodcuttingHUD, WoodcuttingSkill>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static WoodcuttingHUD CreateInstance()
        {
            var go = new GameObject(nameof(WoodcuttingHUD));
            return go.AddComponent<WoodcuttingHUD>();
        }

        protected override string ProgressRootName => "WoodcuttingProgress";

        protected override string ToolRootName => "WoodcuttingAxe";

        protected override bool IsGatheringActive => skill != null && skill.IsChopping;

        protected override int ResolveProgressIntervalTicks()
        {
            return skill != null ? skill.CurrentChopIntervalTicks : 0;
        }

        protected override Sprite ResolveToolSprite()
        {
            var axe = skill?.CurrentAxe;
            if (axe == null)
                return null;

            return GatheringToolIconResolver.GetIcon(axe.Id);
        }

        protected override void OnSkillLocated(WoodcuttingSkill located)
        {
            located.OnStartChopping += HandleStart;
            located.OnStopChopping += HandleStop;
        }

        protected override void OnSkillDetached(WoodcuttingSkill previous)
        {
            previous.OnStartChopping -= HandleStart;
            previous.OnStopChopping -= HandleStop;
        }

        private void HandleStart(TreeNode tree)
        {
            if (tree == null)
            {
                EndTrackingTarget();
                return;
            }

            var renderer = tree.GetComponent<SpriteRenderer>();
            BeginTrackingTarget(tree.transform, renderer);
        }

        private void HandleStop()
        {
            EndTrackingTarget();
        }
    }
}
