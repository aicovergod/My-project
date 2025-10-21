using Companions;
using Inventory;
using Skills.Common.UI;
using UnityEngine;
using UnityEngine.UI;
using Util;

namespace Skills.Mining
{
    /// <summary>
    ///     Shared presentation logic for mining HUDs that render progress bars and pickaxe sprites above the
    ///     active rock. Handles event binding, node tracking, sorting behaviour, and sprite assignment so
    ///     both the player and companion variants reuse the same pipeline.
    /// </summary>
    /// <typeparam name="THud">Concrete HUD implementation that derives from this base.</typeparam>
    public abstract class MiningHudBase<THud> : GatheringToolHudBase<THud, MiningSkill>
        where THud : MiningHudBase<THud>
    {
        private const string ProgressLayerName = "UI";

        private MineableRock currentRock;

        /// <inheritdoc />
        protected override string ProgressRootName => ResolveProgressRootName();

        /// <inheritdoc />
        protected override string ToolRootName => ResolveToolRootName();

        /// <inheritdoc />
        protected override bool IsGatheringActive => skill != null && skill.IsMining;

        /// <inheritdoc />
        protected override int ResolveProgressIntervalTicks()
        {
            return skill != null ? skill.CurrentSwingSpeedTicks : 0;
        }

        /// <summary>
        ///     Resolves the unique name for the instantiated progress root. Derived classes can override this
        ///     if they need to preserve legacy GameObject names for compatibility.
        /// </summary>
        /// <returns>Name to assign to the progress root GameObject.</returns>
        protected virtual string ResolveProgressRootName()
        {
            return $"{typeof(THud).Name}Progress";
        }

        /// <summary>
        ///     Resolves the unique name for the instantiated pickaxe root. Derived classes can override this if
        ///     they require bespoke naming for analytics or prefab binding.
        /// </summary>
        /// <returns>Name to assign to the tool sprite GameObject.</returns>
        protected virtual string ResolveToolRootName()
        {
            return $"{typeof(THud).Name}Pickaxe";
        }

        /// <inheritdoc />
        protected override Sprite ResolveToolSprite()
        {
            var pick = skill?.CurrentPickaxe;
            if (pick == null)
                return null;

            return GatheringToolIconResolver.GetIcon(pick.Id);
        }

        /// <inheritdoc />
        protected override void OnProgressRootCreated(GameObject root, Canvas canvas, Image fillImage)
        {
            base.OnProgressRootCreated(root, canvas, fillImage);
            ApplyProgressLayer(root);
        }

        /// <inheritdoc />
        protected override void OnToolRootCreated(GameObject root, SpriteRenderer renderer)
        {
            base.OnToolRootCreated(root, renderer);
            ApplyProgressLayer(root);
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        protected override void OnSkillLocated(MiningSkill located)
        {
            located.OnStartMining += HandleStart;
            located.OnStopMining += HandleStop;
        }

        /// <inheritdoc />
        protected override void OnSkillDetached(MiningSkill previous)
        {
            previous.OnStartMining -= HandleStart;
            previous.OnStopMining -= HandleStop;
        }

        /// <inheritdoc />
        protected override void OnTrackingEnded()
        {
            base.OnTrackingEnded();
            currentRock = null;
        }

        /// <summary>
        ///     Handles the mining start event by beginning to track the supplied rock.
        /// </summary>
        /// <param name="rock">Rock that should receive HUD feedback.</param>
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

        /// <summary>
        ///     Handles the mining stop event by clearing the current target and hiding visuals.
        /// </summary>
        private void HandleStop()
        {
            EndTrackingTarget();
            currentRock = null;
        }

        /// <summary>
        ///     Applies the UI physics layer to the supplied root so camera filtering remains consistent.
        /// </summary>
        /// <param name="target">Root GameObject that should receive the UI layer.</param>
        private void ApplyProgressLayer(GameObject target)
        {
            if (target == null)
                return;

            int uiLayer = LayerMask.NameToLayer(ProgressLayerName);
            if (uiLayer < 0)
                return;

            LayerUtility.SetLayerRecursively(target.transform, uiLayer);
        }

        /// <summary>
        ///     Determines whether the supplied mining skill belongs to the companion hierarchy.
        /// </summary>
        /// <param name="candidate">Skill instance to evaluate.</param>
        /// <returns>True when the skill is attached to the companion, otherwise false.</returns>
        protected static bool IsCompanionSkill(MiningSkill candidate)
        {
            if (candidate == null)
                return false;

            var transform = candidate.transform;
            if (transform == null)
                return false;

            var root = transform.root;
            if (root != null && root.gameObject == CompanionManager.CompanionObject)
                return true;

            if (candidate.GetComponent<CompanionController>() != null || candidate.GetComponentInParent<CompanionController>() != null)
                return true;

            if (candidate.GetComponent<CompanionMiningController>() != null || candidate.GetComponentInParent<CompanionMiningController>() != null)
                return true;

            return false;
        }
    }
}
