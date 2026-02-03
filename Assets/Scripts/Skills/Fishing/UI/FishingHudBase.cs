using Companions;
using Player;
using Skills.Common.UI;
using UnityEngine;

namespace Skills.Fishing
{
    /// <summary>
    ///     Shared presentation logic for fishing HUDs that render progress bars and tool sprites above
    ///     active fishing spots. Allows both the player and companion variants to reuse the same tracking
    ///     pipeline while keeping ownership filtering centralised.
    /// </summary>
    /// <typeparam name="THud">Concrete HUD implementation derived from this base.</typeparam>
    public abstract class FishingHudBase<THud> : GatheringToolHudBase<THud, FishingSkill>
        where THud : FishingHudBase<THud>
    {
        private FishableSpot currentSpot;

        /// <inheritdoc />
        protected override string ProgressRootName => ResolveProgressRootName();

        /// <inheritdoc />
        protected override string ToolRootName => ResolveToolRootName();

        /// <inheritdoc />
        protected override bool IsGatheringActive => skill != null && skill.IsFishing;

        /// <inheritdoc />
        protected override int ResolveProgressIntervalTicks()
        {
            return skill != null ? skill.CurrentCatchIntervalTicks : 0;
        }

        /// <inheritdoc />
        protected override Sprite ResolveToolSprite()
        {
            var tool = skill?.CurrentTool;
            if (tool == null)
                return null;

            return GatheringToolIconResolver.GetIcon(tool.Id);
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
        protected override void OnSkillLocated(FishingSkill located)
        {
            located.OnStartFishing += HandleStart;
            located.OnStopFishing += HandleStop;
        }

        /// <inheritdoc />
        protected override void OnSkillDetached(FishingSkill previous)
        {
            previous.OnStartFishing -= HandleStart;
            previous.OnStopFishing -= HandleStop;
        }

        /// <inheritdoc />
        protected override void OnTrackingEnded()
        {
            base.OnTrackingEnded();
            currentSpot = null;
        }

        /// <summary>
        ///     Handles the fishing start event by following the supplied spot.
        /// </summary>
        private void HandleStart(FishableSpot spot)
        {
            currentSpot = spot;

            if (spot == null)
            {
                EndTrackingTarget();
                return;
            }

            var renderer = spot.GetComponent<SpriteRenderer>();
            BeginTrackingTarget(spot.transform, renderer);
        }

        /// <summary>
        ///     Handles the fishing stop event by clearing the current target.
        /// </summary>
        private void HandleStop()
        {
            currentSpot = null;
            EndTrackingTarget();
        }

        /// <summary>
        ///     Determines whether the supplied fishing skill belongs to the companion hierarchy.
        /// </summary>
        protected static bool IsCompanionSkill(FishingSkill candidate)
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

            if (candidate.GetComponent<CompanionFishingController>() != null ||
                candidate.GetComponentInParent<CompanionFishingController>() != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Determines whether the supplied fishing skill belongs to the player hierarchy.
        /// </summary>
        protected static bool BelongsToPlayer(FishingSkill candidate)
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

                if (root.GetComponent<FisherController>() != null)
                    return true;
            }

            var go = transform.gameObject;
            if (go != null && go.CompareTag("Player"))
                return true;

            if (candidate.GetComponent<PlayerMover>() != null || candidate.GetComponentInParent<PlayerMover>() != null)
                return true;

            if (candidate.GetComponent<FisherController>() != null || candidate.GetComponentInParent<FisherController>() != null)
                return true;

            return false;
        }
    }
}
