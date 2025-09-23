using Inventory;
using Skills.Common;
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
            if (selectedIndex < 0)
            {
                failureMessage = "Select a log to feed the bonfire.";
                return false;
            }

            if (!Skill.TryStartBonfireFeeding(node, selectedIndex, out failureMessage))
                return false;

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
    }
}
