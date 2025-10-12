using Inventory;
using Skills.Common.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Util;
using World;

namespace Skills.Fishing
{
    public class FishingHUD : GatheringSkillHudBase<FishingHUD, FishingSkill>, ITickable
    {
        private bool sceneLoadedSubscribed;

        private Transform target;
        private Image progressImage;
        private GameObject progressRoot;
        private GameObject toolRoot;
        private SpriteRenderer toolRenderer;
        private Canvas progressCanvas;
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);
        private readonly Vector3 toolOffset = Vector3.zero;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float step;
        // Stores the duration of the current interpolation span so we can sync visual progress with the tick cadence.
        private float segmentDuration = Ticker.TickDuration;
        // Keeps track of whether the bar should reset after being displayed at full progress for one tick.
        private bool awaitingResetTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static FishingHUD CreateInstance()
        {
            var go = new GameObject(nameof(FishingHUD));
            return go.AddComponent<FishingHUD>();
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
            if (toolRoot == null)
                CreateToolSprite();
        }

        private void CreateProgressBar()
        {
            if (progressRoot != null)
                return;

            progressRoot = new GameObject("FishingProgress");
            progressRoot.transform.SetParent(transform);

            progressCanvas = progressRoot.AddComponent<Canvas>();
            progressCanvas.renderMode = RenderMode.WorldSpace;
            progressCanvas.overrideSorting = true;
            progressRoot.AddComponent<CanvasScaler>();
            progressRoot.AddComponent<GraphicRaycaster>();
            progressRoot.transform.localScale = Vector3.one * 0.01f;

            var bg = new GameObject("Background");
            bg.transform.SetParent(progressRoot.transform, false);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.5f);
            var bgSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            bgImage.sprite = bgSprite;
            var bgRect = bgImage.rectTransform;
            bgRect.sizeDelta = new Vector2(150f, 25f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(bg.transform, false);
            progressImage = fill.AddComponent<Image>();
            progressImage.color = SkillingProgressColorGradient.Evaluate(0f);
            progressImage.sprite = bgSprite;
            progressImage.type = Image.Type.Filled;
            progressImage.fillMethod = Image.FillMethod.Horizontal;
            SetProgressFill(0f);
            var fillRect = progressImage.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            progressRoot.SetActive(false);
        }

        private void CreateToolSprite()
        {
            if (toolRoot != null)
                return;

            toolRoot = new GameObject("FishingTool");
            toolRoot.transform.SetParent(transform);
            toolRenderer = toolRoot.AddComponent<SpriteRenderer>();
            toolRenderer.sortingOrder = 100;
            toolRoot.SetActive(false);
        }

        private void HandleStart(FishableSpot spot)
        {
            EnsureProgressObjects();
            target = spot.transform;
            SetProgressFill(0f);
            currentFill = 0f;
            tickTimer = 0f;
            step = skill.CurrentCatchIntervalTicks > 0 ? 1f / skill.CurrentCatchIntervalTicks : 0f;
            nextFill = step;
            awaitingResetTick = false;
            segmentDuration = ResolveInitialSegmentDuration();
            progressRoot.SetActive(true);

            var tool = skill.CurrentTool;
            if (tool != null && toolRenderer != null)
            {
                var item = Resources.Load<ItemData>("Item/" + tool.Id);
                if (item != null && item.icon != null)
                {
                    toolRenderer.sprite = item.icon;
                    toolRoot.SetActive(true);
                }
            }

            var targetRenderer = spot.GetComponent<SpriteRenderer>();
            if (targetRenderer != null)
            {
                if (progressCanvas != null)
                {
                    progressCanvas.sortingLayerID = targetRenderer.sortingLayerID;
                    progressCanvas.sortingOrder = targetRenderer.sortingOrder + 1;
                }
                if (toolRenderer != null)
                {
                    toolRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    toolRenderer.sortingOrder = targetRenderer.sortingOrder + 2;
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
            if (toolRoot != null)
            {
                toolRoot.SetActive(false);
                if (toolRenderer != null)
                    toolRenderer.sprite = null;
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
            if (toolRoot != null && toolRoot.activeSelf)
                toolRoot.transform.position = target.position + toolOffset;

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
            if (target == null || skill == null || !skill.IsFishing)
                return;

            tickTimer = 0f;
            // If the tool cannot catch anything we keep the bar cleared.
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

        private void EnsureSceneLoadedSubscription()
        {
            if (sceneLoadedSubscribed)
                return;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            sceneLoadedSubscribed = true;
        }

        private void SetProgressFill(float normalizedValue)
        {
            if (progressImage == null)
                return;

            float clamped = Mathf.Clamp01(normalizedValue);
            progressImage.fillAmount = clamped;
            progressImage.color = SkillingProgressColorGradient.Evaluate(clamped);
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

            if (toolRoot != null)
            {
                Destroy(toolRoot);
                toolRoot = null;
            }

            progressImage = null;
            toolRenderer = null;
            progressCanvas = null;
            target = null;
        }

        /// <summary>
        /// Uses the ticker to determine how long the first lerp segment should last after starting a catch cycle.
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
        /// Subscribes to the fishing skill events once the player instance has spawned.
        /// </summary>
        /// <param name="located">The fishing skill that was discovered.</param>
        protected override void OnSkillLocated(FishingSkill located)
        {
            located.OnStartFishing += HandleStart;
            located.OnStopFishing += HandleStop;
        }

        /// <summary>
        /// Unsubscribes from the fishing skill events when the HUD detaches from the player.
        /// </summary>
        /// <param name="previous">The fishing skill reference that is being released.</param>
        protected override void OnSkillDetached(FishingSkill previous)
        {
            previous.OnStartFishing -= HandleStart;
            previous.OnStopFishing -= HandleStop;
        }
    }
}
