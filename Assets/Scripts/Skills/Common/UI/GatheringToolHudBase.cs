using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Util;

namespace Skills.Common.UI
{
    /// <summary>
    ///     Centralises the shared behaviour for gathering HUDs that render a progress bar and tool sprite
    ///     above the active gathering node. Handles lifecycle management, ticker interpolation, scene-load
    ///     subscriptions, and tool sprite assignment so individual skills only implement the unique pieces
    ///     of logic that differ between woodcutting, fishing, and mining.
    /// </summary>
    /// <typeparam name="THud">Concrete HUD type so the singleton bootstrapper can reference the derived class.</typeparam>
    /// <typeparam name="TSkill">Concrete gathering skill that drives the HUD.</typeparam>
    public abstract class GatheringToolHudBase<THud, TSkill> : GatheringSkillHudBase<THud, TSkill>, ITickable
        where THud : GatheringToolHudBase<THud, TSkill>
        where TSkill : MonoBehaviour
    {
        private bool sceneLoadedSubscribed;
        private bool trackingActive;
        private bool tickerSubscribed;

        private Transform target;
        private Image progressImage;
        private GameObject progressRoot;
        private Canvas progressCanvas;
        private GameObject toolRoot;
        private SpriteRenderer toolRenderer;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float step;
        private float segmentDuration = Ticker.TickDuration;
        private bool awaitingResetTick;

        /// <summary>
        ///     Accessor for the instantiated progress bar GameObject so derived classes can adjust
        ///     additional components (e.g. physics layers) when required.
        /// </summary>
        protected GameObject ProgressRootObject => progressRoot;

        /// <summary>
        ///     Exposes the progress bar canvas so derived HUDs can tweak sorting behaviour for special cases.
        /// </summary>
        protected Canvas ProgressWorldCanvas => progressCanvas;

        /// <summary>
        ///     Provides access to the instantiated fill image that represents gathering progress.
        /// </summary>
        protected Image ProgressFillImage => progressImage;

        /// <summary>
        ///     The sprite renderer that displays the currently equipped tool above the node.
        /// </summary>
        protected SpriteRenderer ToolSpriteRenderer => toolRenderer;

        /// <summary>
        ///     The transform of the node that the HUD is currently following.
        /// </summary>
        protected Transform CurrentTarget => target;

        /// <summary>
        ///     Derived classes provide the unique name for the spawned progress bar root.
        /// </summary>
        protected abstract string ProgressRootName { get; }

        /// <summary>
        ///     Derived classes provide the unique name for the spawned tool sprite root.
        /// </summary>
        protected abstract string ToolRootName { get; }

        /// <summary>
        ///     Offset applied to the progress bar relative to the tracked node.
        /// </summary>
        protected virtual Vector3 ProgressWorldOffset => new Vector3(0f, 0.75f, 0f);

        /// <summary>
        ///     Offset applied to the tool sprite relative to the tracked node.
        /// </summary>
        protected virtual Vector3 ToolWorldOffset => Vector3.zero;

        /// <summary>
        ///     Offset applied to the progress bar sorting order relative to the tracked node's renderer.
        /// </summary>
        protected virtual int ProgressSortingOrderOffset => 1;

        /// <summary>
        ///     Offset applied to the tool sprite sorting order relative to the tracked node's renderer.
        /// </summary>
        protected virtual int ToolSortingOrderOffset => 2;

        /// <summary>
        ///     Indicates whether the associated gathering skill is actively performing its action and therefore
        ///     whether the HUD should animate progress for the current tick.
        /// </summary>
        protected abstract bool IsGatheringActive { get; }

        /// <summary>
        ///     Resolves the number of ticks required to complete one gather action for the active tool.
        /// </summary>
        /// <returns>The tick interval, or zero if no valid action is available.</returns>
        protected abstract int ResolveProgressIntervalTicks();

        /// <summary>
        ///     Resolves the sprite that should be displayed for the currently equipped gathering tool.
        /// </summary>
        /// <returns>The sprite for the active tool, or <c>null</c> when no tool should be shown.</returns>
        protected abstract Sprite ResolveToolSprite();

        /// <inheritdoc />
        protected override void OnSingletonAwake()
        {
            EnsureSceneLoadedSubscription();
            EnsureHudObjects();
            RefreshSkillSubscription();
        }

        /// <summary>
        ///     Unity enable hook used to restore subscriptions after domain reloads.
        /// </summary>
        protected virtual void OnEnable()
        {
            EnsureSceneLoadedSubscription();
            RefreshSkillSubscription();
        }

        /// <summary>
        ///     Unity disable hook used to tear down subscriptions when the HUD is deactivated.
        /// </summary>
        protected virtual void OnDisable()
        {
            if (sceneLoadedSubscribed)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedSubscribed = false;
            }

            EndTrackingTarget();
            DetachFromSkill();
            CancelSkillRefreshRoutine();
        }

        /// <inheritdoc />
        protected override void OnSingletonDestroyed()
        {
            base.OnSingletonDestroyed();

            if (sceneLoadedSubscribed)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedSubscribed = false;
            }

            EndTrackingTarget();
            DetachFromSkill();
            CancelSkillRefreshRoutine();

            if (progressRoot != null)
            {
                Destroy(progressRoot);
                progressRoot = null;
            }

            if (toolRoot != null)
            {
                Destroy(toolRoot);
                toolRoot = null;
            }

            progressImage = null;
            progressCanvas = null;
            toolRenderer = null;
            target = null;
        }

        /// <summary>
        ///     Ensures the progress bar and tool sprite objects exist before the HUD starts tracking targets.
        /// </summary>
        private void EnsureHudObjects()
        {
            if (progressRoot == null)
                CreateProgressBar();
            if (toolRoot == null)
                CreateToolSprite();
        }

        /// <summary>
        ///     Builds the shared gathering progress bar.
        /// </summary>
        private void CreateProgressBar()
        {
            if (progressRoot != null)
                return;

            var components = GatheringProgressBarBuilder.Build(ProgressRootName, transform);
            progressRoot = components.Root;
            progressCanvas = components.Canvas;
            progressImage = components.FillImage;
            progressImage.color = SkillingProgressColorGradient.Evaluate(0f);
            SetProgressFill(0f);
            progressRoot.SetActive(false);
            OnProgressRootCreated(progressRoot, progressCanvas, progressImage);
        }

        /// <summary>
        ///     Builds the sprite renderer used for the currently equipped gathering tool.
        /// </summary>
        private void CreateToolSprite()
        {
            if (toolRoot != null)
                return;

            toolRoot = new GameObject(ToolRootName);
            toolRoot.transform.SetParent(transform);
            toolRoot.transform.localPosition = Vector3.zero;
            toolRenderer = toolRoot.AddComponent<SpriteRenderer>();
            toolRenderer.sortingOrder = 100;
            toolRoot.SetActive(false);
            OnToolRootCreated(toolRoot, toolRenderer);
        }

        /// <summary>
        ///     Allows derived HUDs to perform additional configuration on the progress bar root after creation.
        /// </summary>
        protected virtual void OnProgressRootCreated(GameObject root, Canvas canvas, Image fillImage)
        {
        }

        /// <summary>
        ///     Allows derived HUDs to perform additional configuration on the tool sprite root after creation.
        /// </summary>
        protected virtual void OnToolRootCreated(GameObject root, SpriteRenderer renderer)
        {
        }

        /// <summary>
        ///     Begins tracking the supplied gathering node so the HUD follows and animates progress above it.
        /// </summary>
        /// <param name="newTarget">Transform of the gathering node that the HUD should follow.</param>
        /// <param name="targetRenderer">Optional sprite renderer used for sorting layer alignment.</param>
        protected void BeginTrackingTarget(Transform newTarget, SpriteRenderer targetRenderer = null)
        {
            EnsureHudObjects();

            target = newTarget;
            trackingActive = true;
            tickTimer = 0f;
            currentFill = 0f;
            awaitingResetTick = false;
            step = CalculateProgressStep();
            nextFill = step;
            segmentDuration = ResolveInitialSegmentDuration();

            if (progressRoot != null)
            {
                progressRoot.SetActive(true);
                progressRoot.transform.position = target != null ? target.position + ProgressWorldOffset : Vector3.zero;
            }

            RefreshToolSprite();
            ApplyTargetSorting(targetRenderer);
            SubscribeToTicker();
            OnTrackingStarted();
        }

        /// <summary>
        ///     Stops tracking the current gathering node and hides the HUD visuals.
        /// </summary>
        protected void EndTrackingTarget()
        {
            if (!trackingActive)
                return;

            trackingActive = false;
            target = null;
            awaitingResetTick = false;
            segmentDuration = Ticker.TickDuration;
            tickTimer = 0f;
            currentFill = 0f;
            nextFill = 0f;

            if (progressRoot != null)
                progressRoot.SetActive(false);

            if (toolRoot != null)
                toolRoot.SetActive(false);

            if (toolRenderer != null)
                toolRenderer.sprite = null;

            UnsubscribeFromTicker();
            OnTrackingEnded();
        }

        /// <summary>
        ///     Hook for derived HUDs to react when tracking begins.
        /// </summary>
        protected virtual void OnTrackingStarted()
        {
        }

        /// <summary>
        ///     Hook for derived HUDs to react when tracking ends.
        /// </summary>
        protected virtual void OnTrackingEnded()
        {
        }

        /// <summary>
        ///     Subscribes to the global ticker so the HUD receives tick callbacks.
        /// </summary>
        private void SubscribeToTicker()
        {
            if (tickerSubscribed)
                return;

            if (Ticker.Instance == null)
                return;

            Ticker.Instance.Subscribe(this);
            tickerSubscribed = true;
        }

        /// <summary>
        ///     Removes the HUD from the global ticker.
        /// </summary>
        private void UnsubscribeFromTicker()
        {
            if (!tickerSubscribed)
                return;

            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);

            tickerSubscribed = false;
        }

        /// <summary>
        ///     Updates the displayed tool sprite to match the current equipment.
        /// </summary>
        protected void RefreshToolSprite()
        {
            if (toolRenderer == null)
                return;

            var sprite = ResolveToolSprite();
            toolRenderer.sprite = sprite;
            if (toolRoot != null)
                toolRoot.SetActive(sprite != null);

            OnToolSpriteAssigned(sprite);
        }

        /// <summary>
        ///     Hook invoked whenever the tool sprite changes so derived HUDs can adjust layering or FX.
        /// </summary>
        /// <param name="sprite">Sprite assigned to the tool renderer.</param>
        protected virtual void OnToolSpriteAssigned(Sprite sprite)
        {
        }

        /// <summary>
        ///     Aligns the progress bar and tool sprite sorting with the supplied renderer.
        /// </summary>
        /// <param name="targetRenderer">Renderer belonging to the gathering node.</param>
        protected virtual void ApplyTargetSorting(SpriteRenderer targetRenderer)
        {
            if (progressCanvas != null && targetRenderer != null)
            {
                progressCanvas.sortingLayerID = targetRenderer.sortingLayerID;
                progressCanvas.sortingOrder = targetRenderer.sortingOrder + ProgressSortingOrderOffset;
            }

            if (toolRenderer != null && targetRenderer != null)
            {
                toolRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                toolRenderer.sortingOrder = targetRenderer.sortingOrder + ToolSortingOrderOffset;
            }
        }

        /// <summary>
        ///     Unity Update loop used to interpolate the progress bar fill between ticks and keep the
        ///     HUD anchored above the tracked node.
        /// </summary>
        protected virtual void Update()
        {
            if (skill == null)
            {
                EnsureSkillRefreshRoutine();
                return;
            }

            if (!trackingActive || target == null || progressImage == null || progressRoot == null)
                return;

            Vector3 basePosition = target.position + ProgressWorldOffset;
            progressRoot.transform.position = basePosition;

            if (toolRoot != null && toolRoot.activeSelf)
                toolRoot.transform.position = target.position + ToolWorldOffset;

            tickTimer += Time.deltaTime;
            if (segmentDuration <= 0f)
            {
                SetProgressFill(nextFill);
                return;
            }

            float t = Mathf.Clamp01(tickTimer / segmentDuration);
            SetProgressFill(Mathf.Lerp(currentFill, nextFill, t));
        }

        /// <inheritdoc />
        public virtual void OnTick()
        {
            segmentDuration = Ticker.TickDuration;

            if (!trackingActive || target == null || skill == null || !IsGatheringActive)
                return;

            tickTimer = 0f;

            step = CalculateProgressStep();
            if (step <= 0f)
            {
                currentFill = 0f;
                nextFill = 0f;
                awaitingResetTick = false;
                SetProgressFill(0f);
                return;
            }

            if (awaitingResetTick)
            {
                awaitingResetTick = false;
                currentFill = 0f;
                nextFill = step;
                SetProgressFill(0f);
                return;
            }

            currentFill = nextFill;

            if (currentFill >= 1f)
            {
                currentFill = 1f;
                nextFill = 1f;
                awaitingResetTick = true;
            }
            else
            {
                nextFill = Mathf.Min(1f, currentFill + step);
            }
        }

        /// <summary>
        ///     Sets the fill amount and colour of the progress bar using the shared skilling gradient.
        /// </summary>
        /// <param name="normalizedValue">Progress value clamped between 0 and 1.</param>
        protected void SetProgressFill(float normalizedValue)
        {
            if (progressImage == null)
                return;

            float clamped = Mathf.Clamp01(normalizedValue);
            progressImage.fillAmount = clamped;
            progressImage.color = SkillingProgressColorGradient.Evaluate(clamped);
        }

        /// <summary>
        ///     Converts the resolved interval into a normalised step value for each tick.
        /// </summary>
        private float CalculateProgressStep()
        {
            int interval = ResolveProgressIntervalTicks();
            return interval > 0 ? 1f / interval : 0f;
        }

        /// <summary>
        ///     Determines the duration of the first interpolation segment using the ticker's remaining time.
        /// </summary>
        private float ResolveInitialSegmentDuration()
        {
            if (Ticker.Instance == null)
                return Ticker.TickDuration;

            float remaining = Ticker.Instance.TimeUntilNextTick;
            return remaining > 0f ? remaining : Ticker.TickDuration;
        }

        /// <summary>
        ///     Ensures the HUD rebinds to the ticker and player skill after additive scene loads.
        /// </summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureHudObjects();
            RefreshSkillSubscription();
        }

        /// <summary>
        ///     Subscribes to <see cref="SceneManager.sceneLoaded"/> so the HUD recreates UI in new scenes.
        /// </summary>
        private void EnsureSceneLoadedSubscription()
        {
            if (sceneLoadedSubscribed)
                return;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            sceneLoadedSubscribed = true;
        }
    }
}
