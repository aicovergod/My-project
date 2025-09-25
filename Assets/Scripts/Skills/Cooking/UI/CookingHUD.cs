using Skills.Common.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Util;
using World;

namespace Skills.Cooking
{
    /// <summary>
    ///     Displays a world-space progress bar whenever the player is actively cooking items.
    ///     The HUD mirrors the shared gathering HUD lifecycle so it survives scene loads and
    ///     automatically binds to the local <see cref="CookingSkill"/> instance when the player spawns.
    /// </summary>
    public sealed class CookingHUD : GatheringSkillHudBase<CookingSkill>, ITickable
    {
        private static CookingHUD instance;
        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        private const string HudName = nameof(CookingHUD);

        /// <summary>
        ///     Singleton accessor used by other systems that need to poke the HUD at runtime.
        /// </summary>
        public static CookingHUD Instance => instance;

        private bool sceneGateSubscribed;
        private bool sceneLoadedSubscribed;
        private bool tickerSubscribed;

        private GameObject progressRoot;
        private Canvas progressCanvas;
        private Image progressFill;
        private readonly Vector3 offset = new Vector3(0f, 0.8f, 0f);

        private bool hasTarget;
        private Vector3 targetPosition;
        private Transform activeStationAnchor;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float segmentDuration = Ticker.TickDuration;
        private float progressStep = 1f;

        /// <summary>
        ///     Tolerance used when detecting when a new cooking cycle starts so the bar can reset cleanly.
        /// </summary>
        private const float ProgressResetThreshold = 0.001f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !PersistentSceneGate.ShouldSpawnInScene(activeScene))
            {
                BeginWaitingForAllowedScene();
                return;
            }

            CreateOrAdoptInstance();
        }

        private static void CreateOrAdoptInstance()
        {
            if (instance != null)
                return;

            StopWaitingForAllowedScene();

            var existing = FindExistingInstance();
            if (existing != null)
            {
                instance = existing;
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    Object.DontDestroyOnLoad(existing.gameObject);
                existing.EnsureSceneGateSubscription();
                existing.EnsureSceneLoadedSubscription();
                existing.EnsureProgressObjects();
                existing.RefreshSkillSubscription();
                return;
            }

            var go = new GameObject(HudName);
            Object.DontDestroyOnLoad(go);
            go.AddComponent<CookingHUD>();
        }

        private static CookingHUD FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<CookingHUD>();
#else
            return Object.FindObjectOfType<CookingHUD>();
#endif
        }

        private static void BeginWaitingForAllowedScene()
        {
            if (waitingForAllowedScene)
                return;

            waitingForAllowedScene = true;
            PersistentSceneGate.SceneEvaluationChanged += HandleSceneEvaluationForBootstrap;
        }

        private static void StopWaitingForAllowedScene()
        {
            if (!waitingForAllowedScene)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneEvaluationForBootstrap;
            waitingForAllowedScene = false;
        }

        private static void HandleSceneEvaluationForBootstrap(Scene scene, bool allowed)
        {
            if (!allowed)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            CreateOrAdoptInstance();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();
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

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;

            if (sceneGateSubscribed)
            {
                PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
                sceneGateSubscribed = false;
            }

            if (sceneLoadedSubscribed)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedSubscribed = false;
            }

            HandleStop();
            DetachFromSkill();
            CancelSkillRefreshRoutine();
            UnsubscribeFromTicker();

            if (!applicationIsQuitting)
                BeginWaitingForAllowedScene();

            instance = null;
        }

        protected override void OnSkillLocated(CookingSkill located)
        {
            EnsureProgressObjects();
            located.OnStartCooking += HandleStartCooking;
            located.OnStopCooking += HandleStopCooking;
        }

        protected override void OnSkillDetached(CookingSkill previous)
        {
            previous.OnStartCooking -= HandleStartCooking;
            previous.OnStopCooking -= HandleStopCooking;
            HandleStop();
        }

        private void HandleStartCooking(CookableRecipe recipe)
        {
            EnsureProgressObjects();
            hasTarget = true;
            activeStationAnchor = ResolveActiveStationAnchor();
            targetPosition = ResolveTargetPosition();
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

        private void HandleStopCooking()
        {
            HandleStop();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureProgressObjects();
            RefreshSkillSubscription();
        }

        private void LateUpdate()
        {
            if (!hasTarget || progressRoot == null || progressFill == null)
                return;

            if (skill == null || !skill.IsCooking)
            {
                HandleStop();
                return;
            }

            targetPosition = ResolveTargetPosition();
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
            if (!hasTarget || skill == null || !skill.IsCooking)
            {
                HandleStop();
                return;
            }

            tickTimer = 0f;
            UpdateSegmentSettings();
            currentFill = progressFill != null ? progressFill.fillAmount : currentFill;
            float normalized = Mathf.Clamp01(skill.CookProgressNormalized);

            if (normalized + ProgressResetThreshold < currentFill)
            {
                currentFill = normalized;
                SetProgressFill(normalized);
            }

            float targetFill = Mathf.Clamp01(currentFill + progressStep);
            nextFill = Mathf.Max(normalized, targetFill);
        }

        private void EnsureProgressObjects()
        {
            if (progressRoot != null)
                return;

            progressRoot = new GameObject("CookingProgress");
            progressRoot.transform.SetParent(transform, false);

            progressCanvas = progressRoot.AddComponent<Canvas>();
            progressCanvas.renderMode = RenderMode.WorldSpace;
            progressCanvas.overrideSorting = true;
            progressRoot.AddComponent<CanvasScaler>();
            progressRoot.AddComponent<GraphicRaycaster>();
            progressRoot.transform.localScale = Vector3.one * 0.01f;

            var bg = new GameObject("Background");
            bg.transform.SetParent(progressRoot.transform, false);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.6f);
            var bgSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            bgImage.sprite = bgSprite;
            var bgRect = bgImage.rectTransform;
            bgRect.sizeDelta = new Vector2(120f, 24f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(bg.transform, false);
            progressFill = fill.AddComponent<Image>();
            progressFill.sprite = bgSprite;
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            SetProgressFill(0f);
            var fillRect = progressFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            progressRoot.SetActive(false);
        }

        private void HandleStop()
        {
            hasTarget = false;
            activeStationAnchor = null;
            currentFill = 0f;
            nextFill = 0f;
            tickTimer = 0f;
            segmentDuration = Ticker.TickDuration;
            if (progressRoot != null)
                progressRoot.SetActive(false);
            UnsubscribeFromTicker();
        }

        private void EnsureSceneGateSubscription()
        {
            if (sceneGateSubscribed)
                return;

            PersistentSceneGate.SceneEvaluationChanged += HandleSceneGateEvaluation;
            sceneGateSubscribed = true;
        }

        private void SetProgressFill(float normalizedValue)
        {
            if (progressFill == null)
                return;

            float clamped = Mathf.Clamp01(normalizedValue);
            progressFill.fillAmount = clamped;
            progressFill.color = SkillingProgressColorGradient.Evaluate(clamped);
        }

        private void HandleSceneGateEvaluation(Scene scene, bool allowed)
        {
            if (instance != this)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            if (allowed)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
            sceneGateSubscribed = false;
            Destroy(gameObject);
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

        private Transform ResolveActiveStationAnchor()
        {
            if (skill != null && skill.ActiveCookingObject != null)
                return skill.ActiveCookingObject.ApproachAnchor;

            return skill != null ? skill.transform : null;
        }

        private Vector3 ResolveTargetPosition()
        {
            if (activeStationAnchor == null)
                activeStationAnchor = ResolveActiveStationAnchor();

            if (activeStationAnchor != null)
                return activeStationAnchor.position;

            return skill != null && skill.transform != null ? skill.transform.position : targetPosition;
        }

        private void UpdateSegmentSettings()
        {
            segmentDuration = Ticker.TickDuration;
            if (skill == null)
            {
                progressStep = 1f;
                return;
            }

            int ticksRequired = Mathf.Max(1, skill.CookTicksPerItem);
            progressStep = 1f / ticksRequired;
        }
    }
}
