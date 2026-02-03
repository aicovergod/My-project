using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Skills.Common;
using Companions;
using Inventory;

namespace Skills.Mining
{
    /// <summary>
    /// Handles mining specific validation on top of the shared <see cref="GatheringController{TSkill,TNode}"/> logic.
    /// Selects the appropriate pickaxe, checks skill requirements and ensures inventory space before mining.
    /// </summary>
    [DisallowMultipleComponent]
    public class MinerController : GatheringController<MiningSkill, MineableRock>
    {
        [SerializeField] private LayerMask rockMask = ~0;
        [SerializeField] private PickaxeToUse pickaxeSelector;

        private PickaxeDefinition cachedPickaxe;
        private Dictionary<string, ItemData> gatheredItemCache;

        private MiningSkill MiningSkill => Skill;

        /// <summary>
        /// Let the base class wire common references before caching the pickaxe selector.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (pickaxeSelector == null)
                pickaxeSelector = GetComponent<PickaxeToUse>();
        }

        /// <summary>
        /// Mining supports right click prospecting like OSRS rocks.
        /// </summary>
        protected override bool SupportsProspecting => true;

        /// <inheritdoc />
        protected override bool IsPerformingAction => MiningSkill != null && MiningSkill.IsMining;

        /// <inheritdoc />
        protected override MineableRock CurrentNode => MiningSkill != null ? MiningSkill.CurrentRock : null;

        /// <inheritdoc />
        protected override void StopAction()
        {
            MiningSkill?.StopMining();
        }

        /// <inheritdoc />
        protected override bool IsNodeDepleted(MineableRock node) => node != null && node.IsDepleted;

        /// <inheritdoc />
        protected override MineableRock FindNodeAtWorldPosition(Vector2 worldPosition)
        {
            var hit = Physics2D.Raycast(worldPosition, Vector2.zero, 0f, rockMask);
            return hit.collider != null ? hit.collider.GetComponent<MineableRock>() : null;
        }

        /// <inheritdoc />
        protected override bool ValidateNode(MineableRock node, out string failureMessage)
        {
            failureMessage = string.Empty;
            cachedPickaxe = null;

            if (MiningSkill == null || node == null || node.RockDef == null)
            {
                failureMessage = "You can't mine this rock";
                return false;
            }

            var personalNode = node.GetComponent<PersonalOreNode>();
            if (personalNode != null && !personalNode.CanMine(MiningSkill, out failureMessage))
            {
                return false;
            }

            cachedPickaxe = pickaxeSelector != null ? pickaxeSelector.GetBestPickaxe() : null;
            if (cachedPickaxe == null)
            {
                failureMessage = "You need a pickaxe";
                return false;
            }

            if (MiningSkill.Level < node.RockDef.Ore.LevelRequirement)
            {
                failureMessage = $"You need Mining level {node.RockDef.Ore.LevelRequirement}";
                return false;
            }

            if (cachedPickaxe.Tier < node.RockDef.RequiresToolTier)
            {
                failureMessage = "You need a better pickaxe";
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        protected override bool HasInventorySpace(MineableRock node, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (MiningSkill == null || node == null || node.RockDef == null)
            {
                failureMessage = "You can't mine this rock";
                return false;
            }

            var rockDefinition = node.RockDef;
            var oreDefinition = rockDefinition != null ? rockDefinition.Ore : null;

            var capacityResult = GatheringInventoryHelper.EvaluateGatheredItemCapacity(
                MiningSkill.InventoryComponent,
                oreDefinition != null ? oreDefinition.Id : string.Empty,
                "Rock Golem",
                ref gatheredItemCache);

            if (capacityResult.PlayerOrPetHasCapacity)
                return true;

            failureMessage = capacityResult.CompanionInventoryHasCapacity
                ? PlayerInventoryFullChatMessage
                : PlayerAndCompanionInventoryFullChatMessage;
            return false;
        }

        /// <inheritdoc />
        protected override bool TryStartAction(MineableRock node, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (MiningSkill == null || node == null)
            {
                failureMessage = "You can't mine this rock";
                return false;
            }

            if (cachedPickaxe == null && pickaxeSelector != null)
                cachedPickaxe = pickaxeSelector.GetBestPickaxe();

            if (cachedPickaxe == null)
            {
                failureMessage = "You need a pickaxe";
                return false;
            }

            MiningSkill.StartMining(node, cachedPickaxe);
            return MiningSkill.IsMining;
        }

        /// <inheritdoc />
        protected override void Prospect(MineableRock node)
        {
            if (node == null)
                return;

            bool shiftHeld = false;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                shiftHeld = (keyboard.leftShiftKey != null && keyboard.leftShiftKey.isPressed)
                    || (keyboard.rightShiftKey != null && keyboard.rightShiftKey.isPressed);
            }
            else
            {
                shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }

            if (shiftHeld)
            {
                // Shift-right click is reserved for commanding the companion. Regardless of whether the
                // companion can accept the order (no pickaxe, out of range, inactive, etc.), we should not
                // prospect the rock. This prevents the player from prospecting when they intended to issue
                // a companion order and ensures the feedback from the companion remains the only response.
                CompanionManager.TryCommandMine(node);
                return;
            }

            node.Prospect(transform);
        }
    }
}
