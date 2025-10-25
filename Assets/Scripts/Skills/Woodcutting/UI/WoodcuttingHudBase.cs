using Companions;
using Player;
using Skills.Common.UI;
using UnityEngine;

namespace Skills.Woodcutting
{
    /// <summary>
    ///     Shared presentation logic for woodcutting HUDs that render progress bars and axe sprites above
    ///     active trees. Enables both the player and companion variants to reuse the same tracking pipeline.
    /// </summary>
    /// <typeparam name="THud">Concrete HUD implementation derived from this base.</typeparam>
    public abstract class WoodcuttingHudBase<THud> : GatheringToolHudBase<THud, WoodcuttingSkill>
        where THud : WoodcuttingHudBase<THud>
    {
        private TreeNode currentTree;

        /// <inheritdoc />
        protected override string ProgressRootName => ResolveProgressRootName();

        /// <inheritdoc />
        protected override string ToolRootName => ResolveToolRootName();

        /// <inheritdoc />
        protected override bool IsGatheringActive => skill != null && skill.IsChopping;

        /// <inheritdoc />
        protected override int ResolveProgressIntervalTicks()
        {
            return skill != null ? skill.CurrentChopIntervalTicks : 0;
        }

        /// <inheritdoc />
        protected override Sprite ResolveToolSprite()
        {
            var axe = skill?.CurrentAxe;
            if (axe == null)
                return null;

            return GatheringToolIconResolver.GetIcon(axe.Id);
        }

        /// <summary>
        ///     Resolves the unique name used for the instantiated progress root.
        /// </summary>
        protected abstract string ResolveProgressRootName();

        /// <summary>
        ///     Resolves the unique name used for the instantiated tool sprite root.
        /// </summary>
        protected abstract string ResolveToolRootName();

        /// <inheritdoc />
        protected override void OnSkillLocated(WoodcuttingSkill located)
        {
            located.OnStartChopping += HandleStart;
            located.OnStopChopping += HandleStop;
        }

        /// <inheritdoc />
        protected override void OnSkillDetached(WoodcuttingSkill previous)
        {
            previous.OnStartChopping -= HandleStart;
            previous.OnStopChopping -= HandleStop;
        }

        /// <inheritdoc />
        protected override void OnTrackingEnded()
        {
            base.OnTrackingEnded();
            currentTree = null;
        }

        /// <summary>
        ///     Handles the woodcutting start event by following the supplied tree.
        /// </summary>
        private void HandleStart(TreeNode tree)
        {
            currentTree = tree;

            if (tree == null)
            {
                EndTrackingTarget();
                return;
            }

            var renderer = tree.GetComponent<SpriteRenderer>();
            BeginTrackingTarget(tree.transform, renderer);
        }

        /// <summary>
        ///     Handles the woodcutting stop event by clearing the current target.
        /// </summary>
        private void HandleStop()
        {
            currentTree = null;
            EndTrackingTarget();
        }

        /// <summary>
        ///     Determines whether the supplied woodcutting skill belongs to the companion hierarchy.
        /// </summary>
        protected static bool IsCompanionSkill(WoodcuttingSkill candidate)
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

            if (candidate.GetComponent<CompanionWoodcuttingController>() != null ||
                candidate.GetComponentInParent<CompanionWoodcuttingController>() != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Determines whether the supplied woodcutting skill belongs to the player hierarchy.
        /// </summary>
        protected static bool BelongsToPlayer(WoodcuttingSkill candidate)
        {
            if (candidate == null)
                return false;

            var transform = candidate.transform;
            if (transform == null)
                return false;

            var root = transform.root;
            if (root != null)
            {
                if (root.CompareTag("Player"))
                    return true;

                if (root.GetComponent<PlayerMover>() != null)
                    return true;

                if (root.GetComponent<WoodcutterController>() != null)
                    return true;
            }

            var go = transform.gameObject;
            if (go != null && go.CompareTag("Player"))
                return true;

            if (candidate.GetComponent<PlayerMover>() != null || candidate.GetComponentInParent<PlayerMover>() != null)
                return true;

            if (candidate.GetComponent<WoodcutterController>() != null || candidate.GetComponentInParent<WoodcutterController>() != null)
                return true;

            return false;
        }
    }
}
