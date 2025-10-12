using Skills.Common.UI;
using UnityEngine;
using World;
using Skills;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Displays Firemaking progress above the current ignition point using a world space progress bar.
    /// </summary>
    public sealed class FiremakingHUD : TickedProgressHudBase<FiremakingHUD, FiremakingSkill>
    {
        private enum FiremakingHudMode
        {
            None,
            Ignition,
            Bonfire
        }

        private const string HudName = nameof(FiremakingHUD);
        private const float FiremakingProgressResetThreshold = 0.001f;

        private FiremakingHudMode mode = FiremakingHudMode.None;
        private FiremakingBonfireObject activeBonfire;
        private SkillManager skillManager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static FiremakingHUD CreateInstance()
        {
            var go = new GameObject(HudName);
            return go.AddComponent<FiremakingHUD>();
        }

        protected override string ProgressBarName => "FiremakingProgress";

        protected override float ProgressResetThreshold => FiremakingProgressResetThreshold;

        protected override void OnSkillLocated(FiremakingSkill located)
        {
            skillManager = located.GetComponent<SkillManager>();
            located.IgnitionStarted += HandleIgnitionStarted;
            located.IgnitionStopped += HandleIgnitionStopped;
            located.BonfireFeedingStarted += HandleBonfireFeedingStarted;
            located.BonfireFeedingStopped += HandleBonfireFeedingStopped;
        }

        protected override void OnSkillDetached(FiremakingSkill previous)
        {
            previous.IgnitionStarted -= HandleIgnitionStarted;
            previous.IgnitionStopped -= HandleIgnitionStopped;
            previous.BonfireFeedingStarted -= HandleBonfireFeedingStarted;
            previous.BonfireFeedingStopped -= HandleBonfireFeedingStopped;
            skillManager = null;
            StopTrackingProgress();
            mode = FiremakingHudMode.None;
            activeBonfire = null;
        }

        protected override bool IsSkillProgressing()
        {
            if (skill == null)
                return false;

            return mode switch
            {
                FiremakingHudMode.Ignition => skill.IsLighting,
                FiremakingHudMode.Bonfire => skill.IsFeedingBonfire,
                _ => false,
            };
        }

        protected override float GetNormalizedProgress()
        {
            if (skill == null)
                return 0f;

            return mode switch
            {
                FiremakingHudMode.Ignition => Mathf.Clamp01(skill.IgnitionProgressNormalized),
                FiremakingHudMode.Bonfire => Mathf.Clamp01(skill.BonfireFeedingProgressNormalized),
                _ => 0f,
            };
        }

        protected override float CalculateProgressStep()
        {
            if (skill == null)
                return 1f;

            switch (mode)
            {
                case FiremakingHudMode.Ignition:
                    var definition = skill.CurrentDefinition;
                    if (definition == null)
                        return 1f;

                    int level = skillManager != null ? skillManager.GetLevel(SkillType.Firemaking) : 1;
                    int ignitionTicks = Mathf.Max(1, definition.GetIgnitionTicks(level));
                    return 1f / ignitionTicks;
                case FiremakingHudMode.Bonfire:
                    int bonfireTicks = Mathf.Max(1, skill.BonfireFeedingTicksRequired);
                    return 1f / bonfireTicks;
                default:
                    return 1f;
            }
        }

        protected override bool TryResolveTargetPosition(out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            if (skill == null)
                return false;

            switch (mode)
            {
                case FiremakingHudMode.Ignition:
                    worldPosition = skill.CurrentAttemptPosition;
                    return true;
                case FiremakingHudMode.Bonfire:
                    if (activeBonfire != null && activeBonfire.transform != null)
                    {
                        worldPosition = activeBonfire.transform.position;
                        return true;
                    }

                    if (skill.transform != null)
                    {
                        worldPosition = skill.transform.position;
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        protected override void OnProgressDeactivated()
        {
            mode = FiremakingHudMode.None;
            activeBonfire = null;
        }

        private void HandleIgnitionStarted(FiremakingLogDefinition definition, Vector3 position)
        {
            _ = definition; // Retained for future HUD variants that may surface log-specific information.
            mode = FiremakingHudMode.Ignition;
            activeBonfire = null;
            BeginProgressTracking(position);
        }

        private void HandleIgnitionStopped()
        {
            if (mode == FiremakingHudMode.Ignition)
                StopTrackingProgress();
        }

        private void HandleBonfireFeedingStarted(FiremakingBonfireObject bonfire, FiremakingLogDefinition definition)
        {
            _ = definition; // The definition is currently unused but retained for future HUD expansions.
            mode = FiremakingHudMode.Bonfire;
            activeBonfire = bonfire;

            Vector3 startPosition = ResolveBonfirePosition();
            BeginProgressTracking(startPosition);
        }

        private void HandleBonfireFeedingStopped()
        {
            if (mode == FiremakingHudMode.Bonfire)
                StopTrackingProgress();
        }

        private Vector3 ResolveBonfirePosition()
        {
            if (activeBonfire != null && activeBonfire.transform != null)
                return activeBonfire.transform.position;

            if (skill != null && skill.transform != null)
                return skill.transform.position;

            return CurrentTargetPosition;
        }
    }
}
