using Skills.Common;
using Skills.Common.UI;
using Skills.Thieving;
using Skills.Thieving.Core;
using UnityEngine;

namespace Skills.Thieving.UI
{
    /// <summary>
    ///     World-space HUD that visualises progress for pickpocketing and object theft attempts.
    /// </summary>
    public sealed class ThievingHud : TickedProgressHudBase<ThievingHud, ThievingSkill>
    {
        private const string HudName = nameof(ThievingHud);
        private const string ProgressObjectName = "ThievingProgress";

        private NpcThievingTarget activeNpc;
        private ThievingObjectNode activeObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static ThievingHud CreateInstance()
        {
            var go = new GameObject(HudName);
            return go.AddComponent<ThievingHud>();
        }

        protected override string ProgressBarName => ProgressObjectName;

        protected override void OnSkillLocated(ThievingSkill located)
        {
            located.PickpocketStarted += HandlePickpocketStarted;
            located.PickpocketFinished += HandlePickpocketFinished;
            located.ObjectTheftStarted += HandleObjectTheftStarted;
            located.ObjectTheftFinished += HandleObjectTheftFinished;
            located.AttemptCancelled += HandleAttemptCancelled;
            located.LevelledUp += HandleLevelledUp;
        }

        protected override void OnSkillDetached(ThievingSkill previous)
        {
            previous.PickpocketStarted -= HandlePickpocketStarted;
            previous.PickpocketFinished -= HandlePickpocketFinished;
            previous.ObjectTheftStarted -= HandleObjectTheftStarted;
            previous.ObjectTheftFinished -= HandleObjectTheftFinished;
            previous.AttemptCancelled -= HandleAttemptCancelled;
            previous.LevelledUp -= HandleLevelledUp;
            activeNpc = null;
            activeObject = null;
            StopTrackingProgress();
        }

        protected override bool IsSkillProgressing()
        {
            return skill != null && skill.IsAttemptActive;
        }

        protected override float GetNormalizedProgress()
        {
            return skill != null ? skill.AttemptProgressNormalized : 0f;
        }

        protected override float CalculateProgressStep()
        {
            if (skill == null)
                return 1f;

            int ticks = Mathf.Max(1, skill.AttemptTicksRequired);
            return 1f / ticks;
        }

        protected override bool TryResolveTargetPosition(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (skill == null || !skill.IsAttemptActive)
                return false;

            if (activeNpc != null && activeNpc.transform != null)
            {
                worldPosition = activeNpc.transform.position;
                return true;
            }

            if (activeObject != null)
            {
                worldPosition = activeObject.InteractionPoint;
                return true;
            }

            worldPosition = skill.AttemptAnchorPosition;
            return true;
        }

        protected override void OnProgressDeactivated()
        {
            activeNpc = null;
            activeObject = null;
        }

        private void HandlePickpocketStarted(NpcThievingTarget target)
        {
            activeNpc = target;
            activeObject = null;
            Vector3 anchor = target != null ? target.transform.position : skill.AttemptAnchorPosition;
            BeginProgressTracking(anchor);
        }

        private void HandlePickpocketFinished(NpcThievingTarget target, bool success)
        {
            _ = target;
            _ = success;
            activeNpc = null;
            if (skill == null || !skill.IsAttemptActive)
                StopTrackingProgress();
        }

        private void HandleObjectTheftStarted(ThievingObjectNode node)
        {
            activeObject = node;
            activeNpc = null;
            Vector3 anchor = node != null ? node.InteractionPoint : skill.AttemptAnchorPosition;
            BeginProgressTracking(anchor);
        }

        private void HandleObjectTheftFinished(ThievingObjectNode node, bool success)
        {
            _ = node;
            _ = success;
            activeObject = null;
            if (skill == null || !skill.IsAttemptActive)
                StopTrackingProgress();
        }

        private void HandleAttemptCancelled()
        {
            activeNpc = null;
            activeObject = null;
            StopTrackingProgress();
        }

        private void HandleLevelledUp(int newLevel)
        {
            if (skill == null)
                return;

            GatheringFloatingTextService.TryShowAtAnchor($"Thieving level {newLevel}", skill.transform);
        }
    }
}
