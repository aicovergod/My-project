using UnityEngine;
using Skills.Common;

namespace Skills.Thieving.Core
{
    /// <summary>
    ///     Handles player interaction with thieving object nodes by leveraging the shared gathering controller plumbing.
    ///     Responsible for range checks, inventory gating and forwarding start/cancel events to <see cref="ThievingSkill"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ThievingSkill))]
    public sealed class ThievingController : GatheringController<ThievingSkill, ThievingObjectNode>
    {
        private ThievingSkill cachedSkill;

        protected override void Awake()
        {
            base.Awake();
            cachedSkill = Skill;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SubscribeSkillEvents();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            UnsubscribeSkillEvents();
        }

        private void SubscribeSkillEvents()
        {
            if (cachedSkill == null)
                cachedSkill = Skill;

            if (cachedSkill == null)
                return;

            cachedSkill.ObjectTheftFinished += HandleObjectTheftFinished;
            cachedSkill.AttemptCancelled += HandleAttemptCancelled;
        }

        private void UnsubscribeSkillEvents()
        {
            if (cachedSkill == null)
                return;

            cachedSkill.ObjectTheftFinished -= HandleObjectTheftFinished;
            cachedSkill.AttemptCancelled -= HandleAttemptCancelled;
        }

        private void HandleObjectTheftFinished(ThievingObjectNode node, bool success)
        {
            _ = success;
        }

        private void HandleAttemptCancelled()
        {
        }

        protected override bool SupportsProspecting => false;

        protected override bool IsPerformingAction => Skill != null && Skill.IsAttemptActive;

        protected override ThievingObjectNode CurrentNode => Skill != null ? Skill.ActiveObjectNode : null;

        protected override bool ValidateNode(ThievingObjectNode node, out string failureMessage)
        {
            failureMessage = string.Empty;

            if (node == null)
            {
                failureMessage = "There is nothing to steal here.";
                return false;
            }

            var definition = node.Definition;
            if (definition == null)
            {
                failureMessage = "This object has not been configured.";
                return false;
            }

            if (node.IsDepleted)
            {
                failureMessage = "The stall is currently empty.";
                return false;
            }

            if (Skill == null)
            {
                failureMessage = "Thieving skill not available.";
                return false;
            }

            int level = Skill.CurrentLevel;
            if (level < definition.RequiredLevel)
            {
                failureMessage = $"You need Thieving level {definition.RequiredLevel} to steal from this.";
                return false;
            }

            return true;
        }

        protected override bool HasInventorySpace(ThievingObjectNode node, out string failureMessage)
        {
            failureMessage = string.Empty;
            return Skill != null && Skill.CanAcceptObjectLoot(node, out failureMessage);
        }

        protected override bool TryStartAction(ThievingObjectNode node, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (Skill == null)
            {
                failureMessage = "Thieving skill not available.";
                return false;
            }

            if (Skill.TryStartObjectTheft(node))
                return true;

            failureMessage = "You are unable to steal right now.";
            return false;
        }

        protected override void StopAction()
        {
            Skill?.CancelAttempt();
        }

        protected override ThievingObjectNode FindNodeAtWorldPosition(Vector2 worldPosition)
        {
            var hit = Physics2D.OverlapPoint(worldPosition);
            if (hit == null)
                return null;

            return hit.GetComponentInParent<ThievingObjectNode>();
        }

        protected override bool IsNodeDepleted(ThievingObjectNode node)
        {
            return node != null && node.IsDepleted;
        }

        protected override bool IsNodeBusy(ThievingObjectNode node)
        {
            return node != null && Skill != null && Skill.IsAttemptActive && Skill.ActiveObjectNode == node;
        }

        protected override Vector3 GetNodePosition(ThievingObjectNode node)
        {
            return node != null ? node.InteractionPoint : base.GetNodePosition(node);
        }

        protected override Transform GetApproachTransform(ThievingObjectNode node)
        {
            return node != null ? node.transform : base.GetApproachTransform(node);
        }

        protected override float GetInteractionRange(ThievingObjectNode node)
        {
            return DefaultInteractRange;
        }

        protected override float GetCancelDistance(ThievingObjectNode node)
        {
            return DefaultCancelDistance;
        }
    }
}
