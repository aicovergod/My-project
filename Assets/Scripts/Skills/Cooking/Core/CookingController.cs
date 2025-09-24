using Inventory;
using Skills.Common;
using Skills.Firemaking;
using UnityEngine;

namespace Skills.Cooking
{
    /// <summary>
    ///     Handles player interaction with <see cref="CookingObject"/> instances by delegating shared
    ///     behaviour to <see cref="GatheringController{TSkill, TNode}"/>. Validates selected ingredients,
    ///     enforces Cooking level requirements, and instructs the skill component to begin cooking.
    /// </summary>
    [DisallowMultipleComponent]
    public class CookingController : GatheringController<CookingSkill, CookingObject>
    {
        [SerializeField]
        [Tooltip("Layer mask used when searching for cooking stations.")]
        private LayerMask cookingStationMask;

        [SerializeField]
        [Tooltip("Optional Firemaking skill reference used to coordinate bonfire messaging.")]
        private FiremakingSkill firemakingSkill;

        /// <summary>
        ///     Layer name automatically used when the inspector has not provided a mask override.
        /// </summary>
        private const string DefaultCookingLayerName = "Interactable";

        private Inventory.Inventory inventory;
        private CookableRecipe cachedRecipe;
        private ItemData cachedRawItem;
        private CookingObject cachedStation;
        private int cachedQuantity;
        private string cachedFailureMessage;

        private CookingSkill CookingSkill => Skill;

        /// <summary>
        ///     Resolve optional references and ensure default configuration values are populated.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (inventory == null && CookingSkill != null)
                inventory = CookingSkill.GetComponent<Inventory.Inventory>();

            if (firemakingSkill == null)
                firemakingSkill = GetComponent<FiremakingSkill>();

            EnsureCookingStationMaskConfigured();
        }

        /// <summary>
        ///     Subscribe to skill events so cached state clears whenever cooking stops externally.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            if (CookingSkill != null)
                CookingSkill.OnStopCooking += HandleSkillStopped;
        }

        /// <summary>
        ///     Unsubscribe from skill events when disabled and clear cached selections.
        /// </summary>
        protected override void OnDisable()
        {
            if (CookingSkill != null)
                CookingSkill.OnStopCooking -= HandleSkillStopped;
            base.OnDisable();
            ClearCachedInteraction();
        }

        /// <inheritdoc />
        protected override bool BlockMouseWhilePointerOverUI => false;

        /// <inheritdoc />
        protected override bool IsPerformingAction => CookingSkill != null && CookingSkill.IsCooking;

        /// <inheritdoc />
        protected override CookingObject CurrentNode => CookingSkill != null ? CookingSkill.ActiveCookingObject : null;

        /// <inheritdoc />
        protected override CookingObject FindNodeAtWorldPosition(Vector2 worldPosition)
        {
            var colliders = Physics2D.OverlapPointAll(worldPosition, cookingStationMask);
            foreach (var collider in colliders)
            {
                var station = collider.GetComponentInParent<CookingObject>();
                if (station != null)
                    return station;
            }
            return null;
        }

        /// <inheritdoc />
        protected override float GetInteractionRange(CookingObject node)
        {
            return node != null ? node.InteractionRange : base.GetInteractionRange(node);
        }

        /// <inheritdoc />
        protected override float GetCancelDistance(CookingObject node)
        {
            return node != null ? node.CancelDistance : base.GetCancelDistance(node);
        }

        /// <summary>
        ///     Use the optional approach anchor if one is supplied on the station.
        /// </summary>
        protected override Transform GetApproachTransform(CookingObject node)
        {
            return node != null ? node.ApproachAnchor : base.GetApproachTransform(node);
        }

        /// <inheritdoc />
        protected override void StopAction()
        {
            CookingSkill?.StopCooking();
            ClearCachedInteraction();
        }

        /// <inheritdoc />
        protected override bool ValidateNode(CookingObject node, out string failureMessage)
        {
            failureMessage = string.Empty;
            cachedFailureMessage = string.Empty;
            cachedRecipe = null;
            cachedRawItem = null;
            cachedStation = null;
            cachedQuantity = 0;

            if (CookingSkill == null || inventory == null || node == null)
            {
                failureMessage = "You can't cook here";
                cachedFailureMessage = failureMessage;
                return false;
            }

            var searchResult = CookingInventoryHelper.FindCookableRecipe(inventory, CookingSkill, inventory.selectedIndex);

            if (!searchResult.HasRecipe)
            {
                if (TryHandleCombinedBonfireFailure(node, out failureMessage))
                {
                    cachedFailureMessage = failureMessage;
                    return false;
                }

                failureMessage = !string.IsNullOrEmpty(searchResult.FailureMessage)
                    ? searchResult.FailureMessage
                    : "You need something raw to cook";
                cachedFailureMessage = failureMessage;
                return false;
            }

            if (!searchResult.HasRequiredQuantity)
            {
                if (TryHandleCombinedBonfireFailure(node, out failureMessage))
                {
                    cachedFailureMessage = failureMessage;
                    return false;
                }

                failureMessage = !string.IsNullOrEmpty(searchResult.FailureMessage)
                    ? searchResult.FailureMessage
                    : "You need something raw to cook";
                cachedFailureMessage = failureMessage;
                return false;
            }

            if (!searchResult.MeetsLevelRequirement)
            {
                failureMessage = !string.IsNullOrEmpty(searchResult.FailureMessage)
                    ? searchResult.FailureMessage
                    : $"You need Cooking level {searchResult.Recipe.requiredLevel}";
                cachedFailureMessage = failureMessage;
                return false;
            }

            cachedStation = node;
            cachedRecipe = searchResult.Recipe;
            cachedRawItem = searchResult.RawItem;
            cachedQuantity = searchResult.Quantity;
            return true;
        }

        /// <summary>
        ///     Checks whether the current interaction targets a bonfire/cooking hybrid while the player
        ///     lacks both raw ingredients and logs. Returns the combined failure message when the helper
        ///     has not already issued it this frame so duplicate feedback is avoided.
        /// </summary>
        private bool TryHandleCombinedBonfireFailure(CookingObject node, out string failureMessage)
        {
            failureMessage = string.Empty;

            if (node == null)
                return false;

            if (!node.TryGetComponent<FiremakingBonfireObject>(out _))
                return false;

            if (inventory == null)
                return false;

            if (firemakingSkill == null)
                firemakingSkill = GetComponent<FiremakingSkill>();

            if (firemakingSkill == null)
                return false;

            if (firemakingSkill.HasAnyLogsInInventory(inventory))
                return false;

            if (BonfireCookingMessageUtility.TryAcquireCombinedMessage(out var combinedMessage))
                failureMessage = combinedMessage;

            return true;
        }

        /// <inheritdoc />
        protected override bool HasInventorySpace(CookingObject node, out string failureMessage)
        {
            if (cachedRecipe == null || cachedRawItem == null || cachedQuantity <= 0 || cachedStation == null)
            {
                failureMessage = !string.IsNullOrEmpty(cachedFailureMessage)
                    ? cachedFailureMessage
                    : "You can't cook that";
                return false;
            }

            failureMessage = string.Empty;
            return true;
        }

        /// <inheritdoc />
        protected override bool TryStartAction(CookingObject node, out string failureMessage)
        {
            failureMessage = string.Empty;

            if (CookingSkill == null)
            {
                failureMessage = "You can't cook here";
                ClearCachedInteraction();
                return false;
            }

            if (cachedRecipe == null || cachedRawItem == null || cachedQuantity <= 0 || cachedStation != node)
            {
                failureMessage = !string.IsNullOrEmpty(cachedFailureMessage)
                    ? cachedFailureMessage
                    : "You can't cook that";
                ClearCachedInteraction();
                return false;
            }

            bool started = CookingSkill.TryStartCooking(node, cachedRecipe, cachedQuantity, out failureMessage);
            if (started)
            {
                inventory?.ClearSelection();
                ClearCachedInteraction();
                return true;
            }

            if (string.IsNullOrEmpty(failureMessage))
                failureMessage = "You can't cook here";

            ClearCachedInteraction();
            return false;
        }

        private void ClearCachedInteraction()
        {
            cachedRecipe = null;
            cachedRawItem = null;
            cachedStation = null;
            cachedQuantity = 0;
            cachedFailureMessage = string.Empty;
        }

        /// <summary>
        ///     Ensure a sensible default layer mask is applied when the inspector has not supplied one.
        ///     Defaults to the "Interactable" layer when available and falls back to every layer otherwise.
        /// </summary>
        private void EnsureCookingStationMaskConfigured()
        {
            if (cookingStationMask != 0)
                return;

            int interactableMask = LayerMask.GetMask(DefaultCookingLayerName);
            if (interactableMask != 0)
            {
                cookingStationMask = interactableMask;
                return;
            }

            cookingStationMask = ~0;
        }

        private void HandleSkillStopped()
        {
            ClearCachedInteraction();
        }
    }
}
