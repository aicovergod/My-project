using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Util;

namespace Skills.Common.UI
{
    /// <summary>
    ///     Provides a reusable foundation for tick-driven gathering HUDs that animate a world-space progress bar.
    ///     Derived classes only need to expose their per-skill progress queries while this base handles
    ///     ticker subscription, scene gating, interpolation, and bar creation.
    /// </summary>
    /// <typeparam name="TSelf">The concrete HUD type implementing the behaviour.</typeparam>
    /// <typeparam name="TSkill">The skill component observed by the HUD.</typeparam>
    public abstract class TickedProgressHudBase<TSelf, TSkill> : GatheringSkillHudBase<TSelf, TSkill>, ITickable
        where TSelf : TickedProgressHudBase<TSelf, TSkill>
        where TSkill : MonoBehaviour
    {
        private bool sceneLoadedSubscribed;
        private bool tickerSubscribed;

        private GameObject progressRoot;
        private Canvas progressCanvas;
        private Image progressFill;

        private bool isTrackingProgress;
        private Vector3 targetPosition;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float segmentDuration = Ticker.TickDuration;
        private float progressStep = 1f;

        /// <summary>
        ///     Name applied to the root GameObject generated for the HUD.
        /// </summary>
        protected abstract string ProgressBarName { get; }

        /// <summary>
        ///     Size applied to the HUD background. Derived classes can override if they need bespoke sizing.
        /// </summary>
        protected virtual Vector2 ProgressBarSize => new Vector2(120f, 24f);

        /// <summary>
        ///     Tint applied to the HUD background panel.
        /// </summary>
        protected virtual Color ProgressBarBackgroundColor => new Color(0f, 0f, 0f, 0.6f);

        /// <summary>
        ///     Offset applied when positioning the HUD relative to its target in world space.
        /// </summary>
        protected virtual Vector3 WorldOffset => new Vector3(0f, 0.8f, 0f);

        /// <summary>
        ///     Threshold used when checking if the skill reported progress has wrapped back to the start of a new cycle.
        /// </summary>
        protected virtual float ProgressResetThreshold => 0.001f;

        /// <summary>
        ///     Exposes the generated progress bar root for derived classes that need direct access (e.g. additional effects).
        /// </summary>
        protected GameObject ProgressRoot => progressRoot;

        /// <summary>
        ///     Provides read-only access to the world-space canvas created for the HUD.
        /// </summary>
        protected Canvas ProgressCanvas => progressCanvas;

        /// <summary>
        ///     Provides read-only access to the fill image so derived classes can adjust additional styling if required.
        /// </summary>
        protected Image ProgressFill => progressFill;

        /// <summary>
        ///     Indicates whether the HUD is currently animating a progress cycle.
        /// </summary>
        protected bool IsTrackingProgress => isTrackingProgress;

        /// <summary>
        ///     Current target world position used for interpolation.
        /// </summary>
        protected Vector3 CurrentTargetPosition => targetPosition;

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

            StopTrackingProgress();
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

            StopTrackingProgress();
            DetachFromSkill();
            CancelSkillRefreshRoutine();
            UnsubscribeFromTicker();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _ = scene;
            _ = mode;
            EnsureProgressObjects();
            RefreshSkillSubscription();
        }

        private void LateUpdate()
        {
            if (!isTrackingProgress || progressRoot == null || progressFill == null)
                return;

            if (!IsSkillProgressing())
            {
                StopTrackingProgress();
                return;
            }

            if (!TryResolveTargetPosition(out Vector3 resolvedPosition))
            {
                StopTrackingProgress();
                return;
            }

            targetPosition = resolvedPosition;
            progressRoot.transform.position = targetPosition + WorldOffset;

            tickTimer += Time.deltaTime;
            if (segmentDuration <= 0f)
            {
                SetProgressFill(nextFill);
                return;
            }

            float t = Mathf.Clamp01(tickTimer / segmentDuration);
            SetProgressFill(Mathf.Lerp(currentFill, nextFill, t));
        }

        /// <summary>
        ///     ITickable implementation that advances the HUD each OSRS tick.
        /// </summary>
        public void OnTick()
        {
            if (!isTrackingProgress || !IsSkillProgressing())
            {
                StopTrackingProgress();
                return;
            }

            tickTimer = 0f;
            UpdateSegmentSettings();

            if (progressFill != null)
                currentFill = progressFill.fillAmount;

            float normalized = Mathf.Clamp01(GetNormalizedProgress());
            if (ShouldResetProgress(currentFill, normalized))
            {
                currentFill = normalized;
                SetProgressFill(normalized);
            }

            float targetFill = Mathf.Clamp01(currentFill + progressStep);
            nextFill = Mathf.Max(normalized, targetFill);
        }

        /// <summary>
        ///     Begins animating the HUD at the supplied world position.
        /// </summary>
        /// <param name="initialTargetPosition">Initial anchor position for the progress bar.</param>
        protected void BeginProgressTracking(Vector3 initialTargetPosition)
        {
            EnsureProgressObjects();

            isTrackingProgress = true;
            targetPosition = initialTargetPosition;
            UpdateSegmentSettings();

            currentFill = 0f;
            nextFill = Mathf.Clamp01(progressStep);
            tickTimer = 0f;
            SetProgressFill(0f);

            if (progressRoot != null)
            {
                progressRoot.SetActive(true);
                progressRoot.transform.position = targetPosition + WorldOffset;
            }

            SubscribeToTicker();
            OnProgressActivated();
        }

        /// <summary>
        ///     Stops animating the HUD and clears any cached interpolation state.
        /// </summary>
        protected void StopTrackingProgress()
        {
            if (!isTrackingProgress)
            {
                UnsubscribeFromTicker();
                return;
            }

            isTrackingProgress = false;
            currentFill = 0f;
            nextFill = 0f;
            tickTimer = 0f;
            segmentDuration = Ticker.TickDuration;
            progressStep = 1f;

            if (progressRoot != null)
                progressRoot.SetActive(false);

            UnsubscribeFromTicker();
            OnProgressDeactivated();
        }

        /// <summary>
        ///     Allows derived classes to react when a new progress cycle begins.
        /// </summary>
        protected virtual void OnProgressActivated()
        {
        }

        /// <summary>
        ///     Allows derived classes to react when the HUD stops animating.
        /// </summary>
        protected virtual void OnProgressDeactivated()
        {
        }

        private void UpdateSegmentSettings()
        {
            segmentDuration = Mathf.Max(0f, GetSegmentDuration());
            progressStep = Mathf.Max(0f, CalculateProgressStep());
            if (progressStep <= 0f)
                progressStep = 1f;
        }

        /// <summary>
        ///     Applies the requested fill amount and synchronises the rainbow gradient with the current progress.
        /// </summary>
        /// <param name="normalizedValue">Progress value between 0 and 1.</param>
        protected void SetProgressFill(float normalizedValue)
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

        private void EnsureProgressObjects()
        {
            if (progressRoot != null)
                return;

            var components = GatheringProgressBarBuilder.Build(
                ProgressBarName,
                transform,
                ProgressBarSize,
                ProgressBarBackgroundColor);

            progressRoot = components.Root;
            if (progressRoot != null)
                progressRoot.transform.SetParent(transform, false);

            progressCanvas = components.Canvas;
            progressFill = components.FillImage;
            if (progressFill != null)
                progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            SetProgressFill(0f);
            if (progressRoot != null)
                progressRoot.SetActive(false);
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

        /// <summary>
        ///     Determines whether the skill is currently performing the activity that requires a HUD.
        /// </summary>
        protected abstract bool IsSkillProgressing();

        /// <summary>
        ///     Retrieves the current normalized progress reported by the skill.
        /// </summary>
        protected abstract float GetNormalizedProgress();

        /// <summary>
        ///     Computes the per-tick increment applied to the HUD when the skill does not advance its own progress.
        /// </summary>
        protected abstract float CalculateProgressStep();

        /// <summary>
        ///     Supplies the world-space position that the HUD should track.
        /// </summary>
        protected abstract bool TryResolveTargetPosition(out Vector3 worldPosition);

        /// <summary>
        ///     Provides the interpolation segment duration. Default implementation returns the global tick duration.
        /// </summary>
        protected virtual float GetSegmentDuration()
        {
            return Ticker.TickDuration;
        }

        /// <summary>
        ///     Allows derived classes to detect when the skill has reset progress and the HUD should snap backwards.
        /// </summary>
        protected virtual bool ShouldResetProgress(float previousFill, float reportedProgress)
        {
            return reportedProgress + ProgressResetThreshold < previousFill;
        }
    }
}
