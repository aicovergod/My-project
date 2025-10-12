using Inventory;
using Skills.Common.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Util;
using World;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Displays woodcutting progress above the current tree.
    /// </summary>
    public class WoodcuttingHUD : GatheringSkillHudBase<WoodcuttingHUD, WoodcuttingSkill>, ITickable
    {
        private bool sceneLoadedSubscribed;

        private Transform target;
        private Image progressImage;
        private GameObject progressRoot;
        private GameObject axeRoot;
        private SpriteRenderer axeRenderer;
        private Canvas progressCanvas;
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);
        private readonly Vector3 axeOffset = Vector3.zero;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float step;
        // Tracks how long the current interpolation segment should last so we sync perfectly with tick events.
        private float segmentDuration = Ticker.TickDuration;
        // Flag used so we can hold the progress bar at 100% for a full tick before resetting back to 0.
        private bool awaitingResetTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static WoodcuttingHUD CreateInstance()
        {
            var go = new GameObject(nameof(WoodcuttingHUD));
            return go.AddComponent<WoodcuttingHUD>();
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
        }

        private void EnsureProgressObjects()
        {
            if (progressRoot == null)
                CreateProgressBar();
            if (axeRoot == null)
                CreateAxeSprite();
        }

        private void CreateProgressBar()
        {
            if (progressRoot != null)
                return;

            var components = GatheringProgressBarBuilder.Build("WoodcuttingProgress", transform);
            progressRoot = components.Root;
            progressCanvas = components.Canvas;
            progressImage = components.FillImage;
            progressImage.color = SkillingProgressColorGradient.Evaluate(0f);
            SetProgressFill(0f);
            progressRoot.SetActive(false);
        }

        private void CreateAxeSprite()
        {
            if (axeRoot != null)
                return;

            axeRoot = new GameObject("WoodcuttingAxe");
            axeRoot.transform.SetParent(transform);
            axeRenderer = axeRoot.AddComponent<SpriteRenderer>();
            axeRenderer.sortingOrder = 100;
            axeRoot.SetActive(false);
        }

        private void HandleStart(TreeNode tree)
        {
            EnsureProgressObjects();
            target = tree.transform;
            SetProgressFill(0f);
            currentFill = 0f;
            tickTimer = 0f;
            step = skill.CurrentChopIntervalTicks > 0 ? 1f / skill.CurrentChopIntervalTicks : 0f;
            nextFill = step;
            awaitingResetTick = false;
            segmentDuration = ResolveInitialSegmentDuration();
            progressRoot.SetActive(true);

            var axe = skill.CurrentAxe;
            if (axe != null && axeRenderer != null)
            {
                var item = Resources.Load<ItemData>("Item/" + axe.Id);
                if (item != null && item.icon != null)
                {
                    axeRenderer.sprite = item.icon;
                    axeRoot.SetActive(true);
                }
            }

            var targetRenderer = tree.GetComponent<SpriteRenderer>();
            if (targetRenderer != null)
            {
                if (progressCanvas != null)
                {
                    progressCanvas.sortingLayerID = targetRenderer.sortingLayerID;
                    progressCanvas.sortingOrder = targetRenderer.sortingOrder + 1;
                }
                if (axeRenderer != null)
                {
                    axeRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    axeRenderer.sortingOrder = targetRenderer.sortingOrder + 2;
                }
            }

            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void HandleStop()
        {
            target = null;
            if (progressRoot != null)
                progressRoot.SetActive(false);
            awaitingResetTick = false;
            segmentDuration = Ticker.TickDuration;
            if (axeRoot != null)
            {
                axeRoot.SetActive(false);
                if (axeRenderer != null)
                    axeRenderer.sprite = null;
            }
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureProgressObjects();
            RefreshSkillSubscription();
        }

        private void Update()
        {
            if (skill == null)
            {
                EnsureSkillRefreshRoutine();
                return;
            }

            if (target == null || progressImage == null)
                return;

            progressRoot.transform.position = target.position + offset;
            if (axeRoot != null && axeRoot.activeSelf)
                axeRoot.transform.position = target.position + axeOffset;

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
            segmentDuration = Ticker.TickDuration;
            if (target == null || skill == null || !skill.IsChopping)
                return;

            tickTimer = 0f;
            // When no valid interval exists we keep the bar hidden at 0.
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
                // The previous tick finished a cycle so we now snap back to the start.
                awaitingResetTick = false;
                currentFill = 0f;
                nextFill = step;
                SetProgressFill(0f);
                return;
            }

            currentFill = nextFill;

            if (currentFill >= 1f)
            {
                // Hold the bar at 100% for a full tick so the player sees a complete animation.
                currentFill = 1f;
                nextFill = 1f;
                awaitingResetTick = true;
            }
            else
            {
                nextFill = Mathf.Min(1f, currentFill + step);
            }
        }

        private void SetProgressFill(float normalizedValue)
        {
            if (progressImage == null)
                return;

            float clamped = Mathf.Clamp01(normalizedValue);
            progressImage.fillAmount = clamped;
            progressImage.color = SkillingProgressColorGradient.Evaluate(clamped);
        }

        private void EnsureSceneLoadedSubscription()
        {
            if (sceneLoadedSubscribed)
                return;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            sceneLoadedSubscribed = true;
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

            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);

            if (progressRoot != null)
            {
                Destroy(progressRoot);
                progressRoot = null;
            }

            if (axeRoot != null)
            {
                Destroy(axeRoot);
                axeRoot = null;
            }

            progressImage = null;
            axeRenderer = null;
            progressCanvas = null;
            target = null;
        }

        /// <summary>
        /// Determines how long the first interpolation segment should last based on the ticker's remaining time.
        /// </summary>
        private float ResolveInitialSegmentDuration()
        {
            if (Ticker.Instance == null)
            {
                return Ticker.TickDuration;
            }

            float remaining = Ticker.Instance.TimeUntilNextTick;
            return remaining > 0f ? remaining : Ticker.TickDuration;
        }

        /// <summary>
        /// Subscribes to woodcutting events when the player skill component becomes available.
        /// </summary>
        /// <param name="located">The woodcutting skill that will drive the HUD.</param>
        protected override void OnSkillLocated(WoodcuttingSkill located)
        {
            located.OnStartChopping += HandleStart;
            located.OnStopChopping += HandleStop;
        }

        /// <summary>
        /// Cleans up event subscriptions when the HUD detaches from the woodcutting skill.
        /// </summary>
        /// <param name="previous">The woodcutting skill instance that is no longer tracked.</param>
        protected override void OnSkillDetached(WoodcuttingSkill previous)
        {
            previous.OnStartChopping -= HandleStart;
            previous.OnStopChopping -= HandleStop;
        }
    }
}
