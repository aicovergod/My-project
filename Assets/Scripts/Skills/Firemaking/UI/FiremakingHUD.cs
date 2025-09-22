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
    public sealed class FiremakingHUD : GatheringSkillHudBase<FiremakingSkill>, ITickable
    {
        private static FiremakingHUD instance;
        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        private const string HudName = nameof(FiremakingHUD);

        public static FiremakingHUD Instance => instance;

        private bool sceneGateSubscribed;
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
        private SkillManager skillManager;

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
            go.AddComponent<FiremakingHUD>();
        }

        private static FiremakingHUD FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<FiremakingHUD>();
#else
            return Object.FindObjectOfType<FiremakingHUD>();
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
            if (instance == this)
            {
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
        }

        protected override void OnSkillLocated(FiremakingSkill located)
        {
            EnsureProgressObjects();
            skillManager = located.GetComponent<SkillManager>();
            located.IgnitionStarted += HandleIgnitionStarted;
            located.IgnitionStopped += HandleIgnitionStopped;
        }

        protected override void OnSkillDetached(FiremakingSkill previous)
        {
            previous.IgnitionStarted -= HandleIgnitionStarted;
            previous.IgnitionStopped -= HandleIgnitionStopped;
            skillManager = null;
            HandleStop();
        }

        private void HandleIgnitionStarted(FiremakingLogDefinition definition, Vector3 position)
        {
            EnsureProgressObjects();
            hasTarget = true;
            targetPosition = position;
            UpdateSegmentSettings();
            currentFill = 0f;
            nextFill = progressStep;
            tickTimer = 0f;
            if (progressFill != null)
                progressFill.fillAmount = 0f;
            if (progressRoot != null)
            {
                progressRoot.SetActive(true);
                progressRoot.transform.position = position + offset;
            }

            SubscribeToTicker();
        }

        private void HandleIgnitionStopped()
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

            // Keep the HUD hovering just above the ignition target.
            if (skill != null && skill.IsLighting)
                targetPosition = skill.CurrentAttemptPosition;

            progressRoot.transform.position = targetPosition + offset;

            tickTimer += Time.deltaTime;
            if (segmentDuration <= 0f)
            {
                progressFill.fillAmount = nextFill;
                return;
            }

            float t = Mathf.Clamp01(tickTimer / segmentDuration);
            progressFill.fillAmount = Mathf.Lerp(currentFill, nextFill, t);
        }

        public void OnTick()
        {
            if (!hasTarget || skill == null || !skill.IsLighting)
            {
                HandleStop();
                return;
            }

            tickTimer = 0f;
            UpdateSegmentSettings();
            currentFill = progressFill != null ? progressFill.fillAmount : currentFill;
            float normalized = Mathf.Clamp01(skill.IgnitionProgressNormalized);
            // Ensure the bar always advances even if the skill reports stale progress (e.g. after a failure retry).
            float targetFill = Mathf.Clamp01(currentFill + progressStep);
            nextFill = Mathf.Max(normalized, targetFill);
        }

        private void EnsureProgressObjects()
        {
            if (progressRoot != null)
                return;

            progressRoot = new GameObject("FiremakingProgress");
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
            progressFill.color = Color.green;
            progressFill.sprite = bgSprite;
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            progressFill.fillAmount = 0f;
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

        private void UpdateSegmentSettings()
        {
            if (skill == null || skill.CurrentDefinition == null)
            {
                progressStep = 1f;
                segmentDuration = Ticker.TickDuration;
                return;
            }

            int level = skillManager != null ? skillManager.GetLevel(SkillType.Firemaking) : 1;
            int ticksRequired = Mathf.Max(1, skill.CurrentDefinition.GetIgnitionTicks(level));
            progressStep = 1f / ticksRequired;
            segmentDuration = Ticker.TickDuration;
        }
    }
}
