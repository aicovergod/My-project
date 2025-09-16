using UnityEngine;
using Skills.Common;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Extends the shared gathering controller with woodcutting specific checks such as axe selection
    /// and tree definition requirements. Ensures the player is in range and has inventory space before chopping.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodcutterController : GatheringController<WoodcuttingSkill, TreeNode>
    {
        [SerializeField]
        [Tooltip("Layers including tree interaction triggers")] private LayerMask treeMask = ~0;

        [SerializeField] private AxeToUse axeSelector;

        private AxeDefinition cachedAxe;

        private WoodcuttingSkill WoodcuttingSkill => Skill;

        /// <summary>
        /// Cache axe selector reference while letting the base controller resolve other dependencies.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (axeSelector == null)
                axeSelector = GetComponent<AxeToUse>();
        }

        /// <inheritdoc />
        protected override bool IsPerformingAction => WoodcuttingSkill != null && WoodcuttingSkill.IsChopping;

        /// <inheritdoc />
        protected override TreeNode CurrentNode => WoodcuttingSkill != null ? WoodcuttingSkill.CurrentTree : null;

        /// <inheritdoc />
        protected override void StopAction()
        {
            WoodcuttingSkill?.StopChopping();
        }

        /// <inheritdoc />
        protected override bool IsNodeDepleted(TreeNode node) => node != null && node.IsDepleted;

        /// <inheritdoc />
        protected override bool IsNodeBusy(TreeNode node) => node != null && node.IsBusy;

        /// <inheritdoc />
        protected override float GetInteractionRange(TreeNode node)
        {
            return node != null && node.def != null ? node.def.InteractRange : base.GetInteractionRange(node);
        }

        /// <inheritdoc />
        protected override float GetCancelDistance(TreeNode node)
        {
            return node != null && node.def != null ? node.def.CancelDistance : base.GetCancelDistance(node);
        }

        /// <inheritdoc />
        protected override TreeNode FindNodeAtWorldPosition(Vector2 worldPosition)
        {
            var collider = Physics2D.OverlapPoint(worldPosition, treeMask);
            return collider != null ? collider.GetComponentInParent<TreeNode>() : null;
        }

        /// <inheritdoc />
        protected override bool ValidateNode(TreeNode node, out string failureMessage)
        {
            failureMessage = string.Empty;
            cachedAxe = null;

            if (WoodcuttingSkill == null || node == null || node.def == null)
            {
                failureMessage = "You can't chop this tree";
                return false;
            }

            cachedAxe = axeSelector != null ? axeSelector.GetBestAxe() : null;
            if (cachedAxe == null)
            {
                failureMessage = "You need an axe";
                return false;
            }

            if (WoodcuttingSkill.Level < node.def.RequiredWoodcuttingLevel)
            {
                failureMessage = $"You need Woodcutting level {node.def.RequiredWoodcuttingLevel}";
                return false;
            }

            if (WoodcuttingSkill.Level < cachedAxe.RequiredWoodcuttingLevel)
            {
                failureMessage = $"You need Woodcutting level {cachedAxe.RequiredWoodcuttingLevel}";
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        protected override bool HasInventorySpace(TreeNode node, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (WoodcuttingSkill == null || node == null || node.def == null)
            {
                failureMessage = "You can't chop this tree";
                return false;
            }

            if (WoodcuttingSkill.CanAddLog(node.def))
                return true;

            failureMessage = "Your inventory is full";
            return false;
        }

        /// <inheritdoc />
        protected override bool TryStartAction(TreeNode node, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (WoodcuttingSkill == null || node == null)
            {
                failureMessage = "You can't chop this tree";
                return false;
            }

            if (cachedAxe == null && axeSelector != null)
                cachedAxe = axeSelector.GetBestAxe();

            if (cachedAxe == null)
            {
                failureMessage = "You need an axe";
                return false;
            }

            WoodcuttingSkill.StartChopping(node, cachedAxe);
            return WoodcuttingSkill.IsChopping;
        }
    }
}
