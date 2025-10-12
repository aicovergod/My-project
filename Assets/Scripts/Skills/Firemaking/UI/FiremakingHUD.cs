using Skills.Common.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Util;
using World;
using Skills;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Displays Firemaking progress above the current ignition point using a world space progress bar.
    /// </summary>
    public sealed class FiremakingHUD : GatheringSkillHudBase<FiremakingHUD, FiremakingSkill>, ITickable
    {
        private enum FiremakingHudMode
        {
            None,
            Ignition,
            Bonfire
        }

        private const string HudName = nameof(FiremakingHUD);
        private bool sceneLoadedSubscribed;
        private bool tickerSubscribed;

        private GameObject progressRoot;
        private Canvas progressCanvas;
        private Image progressFill;
        private readonly Vector3 offset = new Vector3(0f, 0.8f, 0f);

        private bool hasTarget;
        private Vector3 targetPosition;
        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float segmentDuration = Ticker.TickDuration;
        private float progressStep = 1f;

        /// <summary>
        ///     Tolerance used when detecting when a new progress cycle begins so the bar can reset cleanly.
        /// </summary>
        private const float ProgressResetThreshold = 0.001f;
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

        protected override void OnSingletonAwake()
        {
            EnsureSceneLoadedSubscription();
            EnsureProgressObjects();
            RefreshSkillSubscription();
        }

        private void OnEnable()
        {
            EnsureSceneLoadedSubscription();
            RefreshSkillSubscription();
        }

        private void OnDisable()
        {
            if (sceneLoadedSubscribed)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedSubscribed = false;
            }

            HandleStop();
            DetachFromSkill();
            CancelSkillRefreshRoutine();
            UnsubscribeFromTicker();
        }

        protected override void OnSingletonDestroyed()
        {
            if (sceneLoadedSubscribed)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedSubscribed = false;
            }

            HandleStop();
            DetachFromSkill();
            CancelSkillRefreshRoutine();
            UnsubscribeFromTicker();
        }

        protected override void OnSkillLocated(FiremakingSkill located)
        {
            EnsureProgressObjects();
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
            HandleStop();
        }

        private void HandleIgnitionStarted(FiremakingLogDefinition definition, Vector3 position)
        {
            EnsureProgressObjects();
            mode = FiremakingHudMode.Ignition;
            activeBonfire = null;
            hasTarget = true;
            targetPosition = position;
            UpdateSegmentSettings();
            currentFill = 0f;
            nextFill = progressStep;
            tickTimer = 0f;
            SetProgressFill(0f);
            if (progressRoot != null)
            {
                progressRoot.SetActive(true);
                progressRoot.transform.position = position + offset;
            }

            SubscribeToTicker();
        }

        private void HandleIgnitionStopped()
        {
            if (mode == FiremakingHudMode.Ignition)
                HandleStop();
        }

        private void HandleBonfireFeedingStarted(FiremakingBonfireObject bonfire, FiremakingLogDefinition definition)
        {
            _ = definition; // The definition is currently unused but retained for future HUD expansions.
            EnsureProgressObjects();
            mode = FiremakingHudMode.Bonfire;
            activeBonfire = bonfire;
            hasTarget = true;
            targetPosition = ResolveBonfirePosition();
            UpdateSegmentSettings();
            currentFill = 0f;
            nextFill = progressStep;
            tickTimer = 0f;
            SetProgressFill(0f);
            if (progressRoot != null)
            {
                progressRoot.SetActive(true);
                progressRoot.transform.position = targetPosition + offset;
            }

            SubscribeToTicker();
        }

        private void HandleBonfireFeedingStopped()
        {
            if (mode == FiremakingHudMode.Bonfire)
                HandleStop();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureProgressObjects();
            RefreshSkillSubscription();
        }

        private Vector3 ResolveBonfirePosition()
        {
            if (activeBonfire != null && activeBonfire.transform != null)
                return activeBonfire.transform.position;

            if (skill != null && skill.transform != null)
                return skill.transform.position;

            return targetPosition;
        }

        private void LateUpdate()
        {
            if (!hasTarget || progressRoot == null || progressFill == null)
                return;

            switch (mode)
            {
                case FiremakingHudMode.Ignition:
                    if (skill == null || !skill.IsLighting)
                    {
                        HandleStop();
                        return;
                    }

                    targetPosition = skill.CurrentAttemptPosition;
                    break;
                case FiremakingHudMode.Bonfire:
                    if (skill == null || !skill.IsFeedingBonfire)
                    {
                        HandleStop();
                        return;
                    }

                    targetPosition = ResolveBonfirePosition();
                    break;
                default:
                    HandleStop();
                    return;
            }

            progressRoot.transform.position = targetPosition + offset;

            tickTimer += Time.deltaTime;
            if (segmentDuration <= 0f)
            {
                SetProgressFill(nextFill);
                return;
            }

            float t = Mathf.Clamp01(tickTimer / segmentDuration);
            SetProgressFill(Mathf.Lerp(currentFill, nextFill, t));
        }

        public void OnTick()
        {
            if (!hasTarget || skill == null)
            {
                HandleStop();
                return;
            }

            if (mode == FiremakingHudMode.Ignition && !skill.IsLighting)
            {
                HandleStop();
                return;
            }

            if (mode == FiremakingHudMode.Bonfire && !skill.IsFeedingBonfire)
            {
                HandleStop();
                return;
            }

            if (mode == FiremakingHudMode.None)
            {
                HandleStop();
                return;
            }

            tickTimer = 0f;
            UpdateSegmentSettings();
            currentFill = progressFill != null ? progressFill.fillAmount : currentFill;

            float normalized = 0f;
            if (mode == FiremakingHudMode.Ignition)
                normalized = Mathf.Clamp01(skill.IgnitionProgressNormalized);
            else if (mode == FiremakingHudMode.Bonfire)
                normalized = Mathf.Clamp01(skill.BonfireFeedingProgressNormalized);

            // If the skill reports a lower progress value we have started a new cycle (new log/feed).
            if (normalized + ProgressResetThreshold < currentFill)
            {
                currentFill = normalized;
                SetProgressFill(normalized);
            }

            // Ensure the bar always advances even if the skill reports stale progress (e.g. after a failure retry).
            float targetFill = Mathf.Clamp01(currentFill + progressStep);
            nextFill = Mathf.Max(normalized, targetFill);
        }

        private void EnsureProgressObjects()
        {
            if (progressRoot != null)
                return;

            var components = GatheringProgressBarBuilder.Build(
                "FiremakingProgress",
                transform,
                new Vector2(120f, 24f),
                new Color(0f, 0f, 0f, 0.6f));

            progressRoot = components.Root;
            progressRoot.transform.SetParent(transform, false);
            progressCanvas = components.Canvas;
            progressFill = components.FillImage;
            progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            SetProgressFill(0f);
            progressRoot.SetActive(false);
        }

        private void HandleStop()
        {
            mode = FiremakingHudMode.None;
            activeBonfire = null;
            hasTarget = false;
            currentFill = 0f;
            nextFill = 0f;
            tickTimer = 0f;
            segmentDuration = Ticker.TickDuration;
            if (progressRoot != null)
                progressRoot.SetActive(false);
            UnsubscribeFromTicker();
        }

        /// <summary>
        /// Applies the requested fill amount and synchronises the rainbow gradient with the current progress.
        /// </summary>
        /// <param name="normalizedValue">Progress value between 0 and 1.</param>
        private void SetProgressFill(float normalizedValue)
        {
            if (progressFill == null)
                return;

            float clamped = Mathf.Clamp01(normalizedValue);
            progressFill.fillAmount = clamped;
            progressFill.color = SkillingProgressColorGradient.Evaluate(clamped);
        }
        private void EnsureSceneLoadedSubscription()
        {
            if (sceneLoadedSubscribed)
                return;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            sceneLoadedSubscribed = true;
        }

        private void SubscribeToTicker()
        {
            if (tickerSubscribed)
                return;

            if (Ticker.Instance == null)
                return;

            Ticker.Instance.Subscribe(this);
            tickerSubscribed = true;
        }

        private void UnsubscribeFromTicker()
        {
            if (!tickerSubscribed)
                return;

            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
            tickerSubscribed = false;
        }

        private void UpdateSegmentSettings()
        {
            segmentDuration = Ticker.TickDuration;

            if (skill == null)
            {
                progressStep = 1f;
                return;
            }

            switch (mode)
            {
                case FiremakingHudMode.Ignition:
                    var definition = skill.CurrentDefinition;
                    if (definition == null)
                    {
                        progressStep = 1f;
                        return;
                    }

                    int level = skillManager != null ? skillManager.GetLevel(SkillType.Firemaking) : 1;
                    int ignitionTicks = Mathf.Max(1, definition.GetIgnitionTicks(level));
                    progressStep = 1f / ignitionTicks;
                    break;
                case FiremakingHudMode.Bonfire:
                    int bonfireTicks = Mathf.Max(1, skill.BonfireFeedingTicksRequired);
                    progressStep = 1f / bonfireTicks;
                    break;
                default:
                    progressStep = 1f;
                    break;
            }
        }
    }
}
