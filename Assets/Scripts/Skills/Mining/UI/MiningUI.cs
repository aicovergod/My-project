using Inventory;
using Skills.Common.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Skills.Mining
{
    /// <summary>
    /// Displays mining progress above the current rock, aligning the HUD with personal node overlays
    /// and mirroring the equipped pickaxe sprite.
    /// </summary>
    public class MiningUI : GatheringToolHudBase<MiningUI, MiningSkill>
    {
        private const string ProgressLayerName = "UI";

        private MineableRock currentRock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static MiningUI CreateInstance()
        {
            var go = new GameObject(nameof(MiningUI));
            return go.AddComponent<MiningUI>();
        }

        protected override string ProgressRootName => "MiningProgress";

        protected override string ToolRootName => "MiningPickaxe";

        protected override bool IsGatheringActive => skill != null && skill.IsMining;

        protected override int ResolveProgressIntervalTicks()
        {
            return skill != null ? skill.CurrentSwingSpeedTicks : 0;
        }

        protected override Sprite ResolveToolSprite()
        {
            var pick = skill?.CurrentPickaxe;
            if (pick == null)
                return null;

            var item = Resources.Load<ItemData>("Item/" + pick.Id);
            return item != null ? item.icon : null;
        }

        protected override void OnProgressRootCreated(GameObject root, Canvas canvas, Image fillImage)
        {
            ApplyProgressLayer(root);
        }

        protected override void OnToolRootCreated(GameObject root, SpriteRenderer renderer)
        {
            ApplyProgressLayer(root);
        }

        protected override void ApplyTargetSorting(SpriteRenderer targetRenderer)
        {
            base.ApplyTargetSorting(targetRenderer);

            var canvas = ProgressWorldCanvas;
            if (canvas == null)
                return;

            int progressLayerId = canvas.sortingLayerID;
            int progressOrder = canvas.sortingOrder;
            int minimumOrder = int.MinValue;
            int overlayOrder = int.MinValue;

            if (targetRenderer != null)
            {
                progressLayerId = targetRenderer.sortingLayerID;
                minimumOrder = targetRenderer.sortingOrder + ProgressSortingOrderOffset;
                progressOrder = minimumOrder;

                if (ToolSpriteRenderer != null)
                {
                    ToolSpriteRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    ToolSpriteRenderer.sortingOrder = targetRenderer.sortingOrder + ToolSortingOrderOffset;
                }
            }

            int characterSortingCeiling = PersonalOreNode.ResolveActiveCharacterSortingOrder();
            if (characterSortingCeiling > int.MinValue)
            {
                if (minimumOrder == int.MinValue)
                    minimumOrder = characterSortingCeiling;
                else
                    minimumOrder = Mathf.Max(minimumOrder, characterSortingCeiling);
            }

            PersonalOreNode personalNode = null;
            if (currentRock != null)
                personalNode = currentRock.GetComponent<PersonalOreNode>() ?? currentRock.GetComponentInParent<PersonalOreNode>();

            if (personalNode != null)
            {
                var overlayCanvas = personalNode.OwnerOnlyCanvas;
                if (overlayCanvas != null)
                {
                    int overlayLayerId = personalNode.OwnerOverlaySortingLayerId;
                    if (overlayLayerId != 0)
                    {
                        progressLayerId = overlayLayerId;
                    }
                    else if (!string.IsNullOrEmpty(overlayCanvas.sortingLayerName))
                    {
                        canvas.sortingLayerName = overlayCanvas.sortingLayerName;
                        progressLayerId = canvas.sortingLayerID;
                    }

                    overlayOrder = personalNode.OwnerOverlaySortingOrder;
                }
            }

            if (overlayOrder > int.MinValue + 1)
            {
                int maxOrder = overlayOrder - 1;
                if (minimumOrder != int.MinValue)
                {
                    if (maxOrder >= minimumOrder)
                        progressOrder = Mathf.Clamp(progressOrder, minimumOrder, maxOrder);
                    else
                        progressOrder = minimumOrder;
                }
                else
                {
                    progressOrder = Mathf.Min(progressOrder, maxOrder);
                }
            }
            else if (minimumOrder != int.MinValue)
            {
                progressOrder = Mathf.Max(progressOrder, minimumOrder);
            }

            canvas.sortingLayerID = progressLayerId;
            canvas.sortingOrder = progressOrder;
        }

        protected override void OnSkillLocated(MiningSkill located)
        {
            located.OnStartMining += HandleStart;
            located.OnStopMining += HandleStop;
        }

        protected override void OnSkillDetached(MiningSkill previous)
        {
            previous.OnStartMining -= HandleStart;
            previous.OnStopMining -= HandleStop;
        }

        protected override void OnTrackingEnded()
        {
            base.OnTrackingEnded();
            currentRock = null;
        }

        private void HandleStart(MineableRock rock)
        {
            currentRock = rock;

            if (rock == null)
            {
                EndTrackingTarget();
                return;
            }

            var renderer = rock.GetComponent<SpriteRenderer>();
            BeginTrackingTarget(rock.transform, renderer);
        }

        private void HandleStop()
        {
            EndTrackingTarget();
            currentRock = null;
        }

        private void ApplyProgressLayer(GameObject target)
        {
            if (target == null)
                return;

            int uiLayer = LayerMask.NameToLayer(ProgressLayerName);
            if (uiLayer < 0)
                return;

            SetLayerRecursively(target.transform, uiLayer);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;

            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child != null)
                    SetLayerRecursively(child, layer);
            }
        }
    }
}
