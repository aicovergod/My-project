using System.Collections.Generic;
using UnityEngine;
using Skills.Common;

namespace Skills.Fishing
{
    /// <summary>
    /// Handles player input for fishing by delegating shared logic to <see cref="GatheringController{TSkill,TNode}"/>.
    /// Evaluates fishing specific requirements such as tool selection, available fish and inventory capacity.
    /// </summary>
    [DisallowMultipleComponent]
    public class FisherController : GatheringController<FishingSkill, FishableSpot>
    {
        [SerializeField]
        [Tooltip("Layers including fishing spots")] private LayerMask spotMask = ~0;

        [SerializeField] private FishingToolToUse toolSelector;
        [SerializeField] private Animator animator;

        private readonly List<FishDefinition> eligibleFish = new List<FishDefinition>();
        private FishingToolDefinition cachedTool;

        private FishingSkill FishingSkill => Skill;

        /// <summary>
        /// Cache optional references on Awake while still letting the base class wire shared dependencies.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (toolSelector == null)
                toolSelector = GetComponent<FishingToolToUse>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        /// <summary>
        /// Subscribe to skill events so animations remain in sync even if fishing stops externally.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            if (FishingSkill != null)
            {
                FishingSkill.OnStartFishing += HandleStartFishing;
                FishingSkill.OnStopFishing += HandleStopFishing;
            }
        }

        /// <summary>
        /// Unsubscribe from the skill events when the component is disabled.
        /// </summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            if (FishingSkill != null)
            {
                FishingSkill.OnStartFishing -= HandleStartFishing;
                FishingSkill.OnStopFishing -= HandleStopFishing;
            }
        }

        /// <summary>
        /// Fishing supports the right click prospect action.
        /// </summary>
        protected override bool SupportsProspecting => true;

        /// <summary>
        /// Fishing should still respond to clicks when the pointer is over UI elements.
        /// </summary>
        protected override bool BlockMouseWhilePointerOverUI => false;

        /// <inheritdoc />
        protected override bool IsPerformingAction => FishingSkill != null && FishingSkill.IsFishing;

        /// <inheritdoc />
        protected override FishableSpot CurrentNode => FishingSkill != null ? FishingSkill.CurrentSpot : null;

        /// <inheritdoc />
        protected override void StopAction()
        {
            FishingSkill?.StopFishing();
        }

        /// <inheritdoc />
        protected override bool IsNodeDepleted(FishableSpot node) => node != null && node.IsDepleted;

        /// <inheritdoc />
        protected override bool IsNodeBusy(FishableSpot node) => node != null && node.IsBusy;

        /// <inheritdoc />
        protected override float GetInteractionRange(FishableSpot node)
        {
            return node != null && node.def != null ? node.def.InteractRange : base.GetInteractionRange(node);
        }

        /// <inheritdoc />
        protected override float GetCancelDistance(FishableSpot node)
        {
            return node != null && node.def != null ? node.def.CancelDistance : base.GetCancelDistance(node);
        }

        /// <inheritdoc />
        protected override FishableSpot FindNodeAtWorldPosition(Vector2 worldPosition)
        {
            var colliders = Physics2D.OverlapPointAll(worldPosition, spotMask);
            foreach (var collider in colliders)
            {
                var spot = collider.GetComponentInParent<FishableSpot>();
                if (spot != null)
                    return spot;
            }
            return null;
        }

        /// <inheritdoc />
        protected override bool ValidateNode(FishableSpot node, out string failureMessage)
        {
            failureMessage = string.Empty;
            eligibleFish.Clear();
            cachedTool = null;

            if (FishingSkill == null || node == null)
            {
                failureMessage = "You can't fish here";
                return false;
            }

            var definition = node.def;
            cachedTool = toolSelector != null
                ? toolSelector.GetBestTool(definition != null ? definition.AllowedTools : null)
                : null;

            if (cachedTool == null)
            {
                if (definition != null && definition.AllowedTools != null && definition.AllowedTools.Count > 0)
                    failureMessage = "You can't use that tool here";
                else
                    failureMessage = "You need a fishing tool";
                return false;
            }

            if (FishingSkill.Level < cachedTool.RequiredLevel)
            {
                failureMessage = $"You need Fishing level {cachedTool.RequiredLevel}";
                return false;
            }

            int minimumLevel = int.MaxValue;
            if (definition != null && definition.AvailableFish != null)
            {
                foreach (var fish in definition.AvailableFish)
                {
                    if (fish == null)
                        continue;
                    minimumLevel = Mathf.Min(minimumLevel, fish.RequiredLevel);
                    if (FishingSkill.Level >= fish.RequiredLevel)
                        eligibleFish.Add(fish);
                }
            }

            if (eligibleFish.Count == 0)
            {
                failureMessage = minimumLevel == int.MaxValue
                    ? "You can't fish here"
                    : $"You need Fishing level {minimumLevel}";
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        protected override bool HasInventorySpace(FishableSpot node, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (FishingSkill == null)
            {
                failureMessage = "You can't fish here";
                return false;
            }

            foreach (var fish in eligibleFish)
            {
                if (FishingSkill.CanAddFish(fish))
                    return true;
            }

            failureMessage = "Your inventory is full";
            return false;
        }

        /// <inheritdoc />
        protected override bool TryStartAction(FishableSpot node, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (FishingSkill == null || node == null)
            {
                failureMessage = "You can't fish here";
                return false;
            }

            if (cachedTool == null && toolSelector != null)
            {
                var definition = node.def;
                cachedTool = toolSelector.GetBestTool(definition != null ? definition.AllowedTools : null);
            }

            if (cachedTool == null)
            {
                failureMessage = "You need a fishing tool";
                return false;
            }

            FishingSkill.StartFishing(node, cachedTool);
            return FishingSkill.IsFishing;
        }

        /// <inheritdoc />
        protected override void Prospect(FishableSpot node)
        {
            node?.Prospect(transform);
        }

        /// <summary>
        /// Triggered whenever fishing successfully starts so the animator can enter the fishing state.
        /// </summary>
        private void HandleStartFishing(FishableSpot spot)
        {
            if (animator != null)
                animator.SetBool("isFishing", true);
        }

        /// <summary>
        /// Triggered whenever fishing stops for any reason, allowing the animation to reset.
        /// </summary>
        private void HandleStopFishing()
        {
            if (animator != null)
                animator.SetBool("isFishing", false);
        }
    }
}
