using System.Collections.Generic;
using Inventory;
using Skills.Common;
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
        private LayerMask cookingStationMask = LayerMask.GetMask("Interactable");

        private static readonly Dictionary<string, CookableRecipe> RecipeLookup = new();
        private static bool recipesLoaded;

        private Inventory.Inventory inventory;
        private CookableRecipe cachedRecipe;
        private ItemData cachedRawItem;
        private CookingObject cachedStation;
        private int cachedQuantity;
        private string cachedFailureMessage;

        private CookingSkill CookingSkill => Skill;

        /// <summary>
        ///     Resolve optional references and ensure the recipe dictionary is ready.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (inventory == null && CookingSkill != null)
                inventory = CookingSkill.GetComponent<Inventory.Inventory>();
            EnsureRecipeLookup();
            if (cookingStationMask == 0)
                cookingStationMask = ~0;
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

            EnsureRecipeLookup();

            if (RecipeLookup.Count == 0)
            {
                failureMessage = "No recipes available";
                cachedFailureMessage = failureMessage;
                return false;
            }

            // Resolve the currently highlighted item.
            ItemData candidateItem = null;
            CookableRecipe candidateRecipe = null;
            int selectedIndex = inventory.selectedIndex;
            if (selectedIndex >= 0)
            {
                var selectedEntry = inventory.GetSlot(selectedIndex);
                candidateItem = selectedEntry.item;
                if (candidateItem != null && !string.IsNullOrEmpty(candidateItem.id))
                    RecipeLookup.TryGetValue(candidateItem.id, out candidateRecipe);
            }

            // Fallback to the first cookable item in the inventory if the highlighted slot is invalid.
            if (candidateRecipe == null || candidateItem == null)
            {
                for (int i = 0; i < inventory.size; i++)
                {
                    var slot = inventory.GetSlot(i);
                    var item = slot.item;
                    if (item == null || string.IsNullOrEmpty(item.id))
                        continue;

                    if (!RecipeLookup.TryGetValue(item.id, out candidateRecipe))
                        continue;

                    candidateItem = item;
                    break;
                }
            }

            if (candidateRecipe == null || candidateItem == null)
            {
                failureMessage = "You need something raw to cook";
                cachedFailureMessage = failureMessage;
                return false;
            }

            if (CookingSkill.Level < candidateRecipe.requiredLevel)
            {
                failureMessage = $"You need Cooking level {candidateRecipe.requiredLevel}";
                cachedFailureMessage = failureMessage;
                return false;
            }

            int quantity = inventory.GetItemCount(candidateItem);
            if (quantity <= 0)
            {
                failureMessage = "You need something raw to cook";
                cachedFailureMessage = failureMessage;
                return false;
            }

            cachedStation = node;
            cachedRecipe = candidateRecipe;
            cachedRawItem = candidateItem;
            cachedQuantity = quantity;
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

        private static void EnsureRecipeLookup()
        {
            if (recipesLoaded)
                return;

            RecipeLookup.Clear();
            var recipes = Resources.LoadAll<CookableRecipe>("CookingDatabase");
            foreach (var recipe in recipes)
            {
                if (recipe == null || string.IsNullOrEmpty(recipe.rawItemId))
                    continue;
                RecipeLookup[recipe.rawItemId] = recipe;
            }

            recipesLoaded = true;
        }

        private void ClearCachedInteraction()
        {
            cachedRecipe = null;
            cachedRawItem = null;
            cachedStation = null;
            cachedQuantity = 0;
            cachedFailureMessage = string.Empty;
        }

        private void HandleSkillStopped()
        {
            ClearCachedInteraction();
        }
    }
}
