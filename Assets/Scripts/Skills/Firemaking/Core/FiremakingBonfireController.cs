using Inventory;
using Skills.Common;
using Skills.Cooking;
using UI;
using UnityEngine;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Handles player interaction with permanent bonfires by delegating shared behaviour to
    ///     <see cref="GatheringController{TSkill,TNode}"/>. Reads the highlighted log from the
    ///     inventory, validates range, and passes the request to <see cref="FiremakingSkill"/> so
    ///     the existing tick-driven fueling loop remains authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FiremakingBonfireController : GatheringController<FiremakingSkill, FiremakingBonfireObject>
    {
        private const string InteractableLayerName = "Interactable";

        [Header("Bonfire Search")]
        [SerializeField]
        [Tooltip("Physics layers that contain bonfires the player can fuel.")]
        private LayerMask bonfireMask;

        [Header("Inventory")]
        [SerializeField]
        [Tooltip("Inventory providing the highlighted log selection for bonfire fueling.")]
        private Inventory.Inventory inventory;

        [Header("Cooking Integration")]
        [SerializeField]
        [Tooltip("Player cooking skill used to coordinate cookable interactions with bonfires.")]
        private CookingSkill cookingSkill;

        private FiremakingBonfireObject pendingBonfire;
        private int pendingLogSlot = -1;

        /// <summary>
        ///     Cache optional references on Awake while still letting the base controller wire the
        ///     shared gathering dependencies.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            if (bonfireMask == 0)
                bonfireMask = ResolveDefaultMask();

            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();

            if (cookingSkill == null)
                cookingSkill = GetComponent<CookingSkill>();
        }

        /// <summary>
        ///     Subscribe to cooking stop events so queued log fueling can begin immediately after fish finish cooking.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();

            if (cookingSkill == null)
                cookingSkill = GetComponent<CookingSkill>();

            if (cookingSkill != null)
                cookingSkill.OnStopCooking += HandleCookingStopped;
        }

        /// <summary>
        ///     Ensure queued state is cleared and cooking callbacks are released when the controller is disabled.
        /// </summary>
        protected override void OnDisable()
        {
            if (cookingSkill != null)
                cookingSkill.OnStopCooking -= HandleCookingStopped;

            ClearPendingBonfire();
            base.OnDisable();
        }

#if UNITY_EDITOR
        /// <summary>
        ///     Ensure newly added components default to the interactable layer mask while still allowing
        ///     designers to override the value in the inspector.
        /// </summary>
        private void Reset()
        {
            if (bonfireMask == 0)
                bonfireMask = ResolveDefaultMask();
        }
#endif

        /// <summary>
        ///     Bonfires should continue to accept clicks even if the pointer is hovering UI, matching
        ///     the fishing controller behaviour so banking or chat overlays do not block fueling.
        /// </summary>
        protected override bool BlockMouseWhilePointerOverUI => false;

        /// <inheritdoc />
        protected override bool IsPerformingAction => Skill != null && Skill.IsFeedingBonfire;

        /// <inheritdoc />
        protected override FiremakingBonfireObject CurrentNode => Skill != null ? Skill.ActiveBonfire : null;

        /// <inheritdoc />
        protected override void StopAction()
        {
            Skill?.StopBonfireFeeding(false, null);
        }

        /// <inheritdoc />
        protected override FiremakingBonfireObject FindNodeAtWorldPosition(Vector2 worldPosition)
        {
            var colliders = Physics2D.OverlapPointAll(worldPosition, bonfireMask);
            foreach (var collider in colliders)
            {
                if (collider == null)
                    continue;

                var bonfire = collider.GetComponentInParent<FiremakingBonfireObject>();
                if (bonfire == null)
                    bonfire = collider.GetComponent<FiremakingBonfireObject>();
                if (bonfire != null)
                    return bonfire;
            }

            return null;
        }

        /// <inheritdoc />
        protected override bool HasInventorySpace(FiremakingBonfireObject node, out string failureMessage)
        {
            failureMessage = string.Empty;
            return true;
        }

        /// <inheritdoc />
        protected override float GetInteractionRange(FiremakingBonfireObject node)
        {
            return node != null ? Mathf.Max(0f, node.CancelDistance) : base.GetInteractionRange(node);
        }

        /// <inheritdoc />
        protected override float GetCancelDistance(FiremakingBonfireObject node)
        {
            return node != null ? Mathf.Max(0f, node.CancelDistance) : base.GetCancelDistance(node);
        }

        /// <inheritdoc />
        protected override bool ValidateNode(FiremakingBonfireObject node, out string failureMessage)
        {
            if (!base.ValidateNode(node, out failureMessage))
                return false;

            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();

            if (ShouldBlockDueToMissingFuelAndFood(node, out string combinedFailure))
            {
                failureMessage = combinedFailure;
                return false;
            }

            failureMessage = string.Empty;
            return true;
        }

        /// <inheritdoc />
        protected override bool TryStartAction(FiremakingBonfireObject node, out string failureMessage)
        {
            failureMessage = string.Empty;

            if (Skill == null || node == null)
            {
                failureMessage = "That bonfire is no longer available.";
                return false;
            }

            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();

            if (inventory == null)
            {
                failureMessage = "You need an inventory to add logs.";
                return false;
            }

            int selectedIndex = inventory.selectedIndex;

            var cookableResult = CookingInventoryHelper.FindCookableRecipe(inventory, cookingSkill, selectedIndex);
            bool selectedSlotCookable = cookableResult.HasRecipe && cookableResult.UsesPreferredSlot;

            if (cookableResult.CanCook)
            {
                int logSlot = ResolvePendingLogSlot(selectedIndex);
                QueuePendingBonfire(node, logSlot);
                return false;
            }

            if (selectedSlotCookable)
            {
                ClearPendingBonfire();
                return false;
            }

            ClearPendingBonfire();

            if (TryGetCombinedBonfireCookingFailure(node, cookableResult, out failureMessage))
                return false;

            if (selectedIndex < 0)
            {
                failureMessage = "Select a log to feed the bonfire.";
                return false;
            }

            if (!Skill.TryStartBonfireFeeding(node, selectedIndex, out failureMessage))
                return false;

            ClearPendingBonfire();
            inventory.ClearSelection();
            return true;
        }

        /// <summary>
        ///     Attempts to build a sensible default mask when the inspector value is cleared or when
        ///     the target layer is missing from the project.
        /// </summary>
        private static LayerMask ResolveDefaultMask()
        {
            int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);
            if (interactableLayer >= 0)
                return 1 << interactableLayer;

            return Physics2D.AllLayers;
        }

        /// <summary>
        ///     Determines whether the current interaction should surface the shared bonfire/cooking
        ///     failure message instead of the standard log selection prompt.
        /// </summary>
        private bool TryGetCombinedBonfireCookingFailure(
            FiremakingBonfireObject node,
            CookingInventoryHelper.CookableInventorySearchResult cookableResult,
            out string failureMessage)
        {
            failureMessage = string.Empty;

            if (node == null)
                return false;

            if (!node.TryGetComponent<CookingObject>(out _))
                return false;

            bool lacksCookable = !cookableResult.HasRecipe || !cookableResult.HasRequiredQuantity;
            if (!lacksCookable)
                return false;

            if (Skill == null || inventory == null)
                return false;

            if (Skill.HasAnyLogsInInventory(inventory))
                return false;

            if (BonfireCookingMessageUtility.TryAcquireCombinedMessage(out var combinedMessage))
                failureMessage = combinedMessage;

            return true;
        }

        /// <summary>
        ///     Prevents the player from auto-walking to the bonfire when they lack both raw food and logs.
        /// </summary>
        private bool ShouldBlockDueToMissingFuelAndFood(FiremakingBonfireObject node, out string failureMessage)
        {
            failureMessage = string.Empty;

            if (Skill == null)
                return false;

            int selectedIndex = inventory != null ? inventory.selectedIndex : -1;
            var cookableResult = CookingInventoryHelper.FindCookableRecipe(inventory, cookingSkill, selectedIndex);

            return TryGetCombinedBonfireCookingFailure(node, cookableResult, out failureMessage);
        }

        /// <summary>
        ///     Resolves the inventory slot that should be used for bonfire fueling once cooking has completed.
        /// </summary>
        private int ResolvePendingLogSlot(int preferredSlot)
        {
            if (inventory == null || Skill == null)
                return -1;

            if (preferredSlot >= 0)
            {
                var selectedEntry = inventory.GetSlot(preferredSlot);
                if (selectedEntry.item != null && Skill.GetDefinitionForItem(selectedEntry.item.id) != null)
                    return preferredSlot;
            }

            for (int i = 0; i < inventory.size; i++)
            {
                var slot = inventory.GetSlot(i);
                if (slot.item == null)
                    continue;

                if (Skill.GetDefinitionForItem(slot.item.id) != null)
                    return i;
            }

            return -1;
        }

        /// <summary>
        ///     Stores the bonfire/log slot pair to process once the cooking controller finishes.
        /// </summary>
        private void QueuePendingBonfire(FiremakingBonfireObject bonfire, int logSlot)
        {
            if (bonfire == null || logSlot < 0)
            {
                ClearPendingBonfire();
                return;
            }

            pendingBonfire = bonfire;
            pendingLogSlot = logSlot;
        }

        /// <summary>
        ///     Clears any queued bonfire fueling request.
        /// </summary>
        private void ClearPendingBonfire()
        {
            pendingBonfire = null;
            pendingLogSlot = -1;
        }

        /// <summary>
        ///     Attempts to fuel the queued bonfire immediately after cooking stops.
        /// </summary>
        private void HandleCookingStopped()
        {
            if (pendingBonfire == null || pendingLogSlot < 0)
            {
                ClearPendingBonfire();
                return;
            }

            var bonfire = pendingBonfire;
            int logSlot = pendingLogSlot;
            ClearPendingBonfire();

            if (Skill == null)
                return;

            if (Skill.TryStartBonfireFeeding(bonfire, logSlot, out string failure))
            {
                inventory?.ClearSelection();
                return;
            }

            if (!string.IsNullOrWhiteSpace(failure))
                FloatingText.Show(failure, bonfire.transform.position);
        }
    }
}
