using Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Util;
using World;

namespace Skills.Mining
{
    /// <summary>
    /// Displays mining progress above the current rock.
    /// </summary>
    public class MiningUI : MonoBehaviour, ITickable
    {
        private static MiningUI instance;
        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        public static MiningUI Instance => instance;

        private bool sceneGateSubscribed;
        private bool sceneLoadedSubscribed;

        private MiningSkill skill;
        private Transform target;
        private Image progressImage;
        private GameObject progressRoot;
        private GameObject pickaxeRoot;
        private SpriteRenderer pickaxeRenderer;
        private Canvas progressCanvas;
        // Offset from the targeted rock's position where the progress bar will appear.
        // Reduced the vertical component to half of its previous value so the bar sits closer to the object.
        private readonly Vector3 offset = new Vector3(0f, 0.75f, 0f);
        private readonly Vector3 pickaxeOffset = Vector3.zero;

        private float currentFill;
        private float nextFill;
        private float tickTimer;
        private float step;
        // Captures how long the current interpolation segment should last to align the UI with tick cadence.
        private float segmentDuration = Ticker.TickDuration;
        // Tracks whether the bar should be reset after spending one full tick at 100%.
        private bool awaitingResetTick;

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
                    DontDestroyOnLoad(existing.gameObject);
                existing.EnsureSceneGateSubscription();
                existing.EnsureSceneLoadedSubscription();
                existing.EnsureProgressObjects();
                existing.RefreshSkillSubscription();
                return;
            }

            var go = new GameObject(nameof(MiningUI));
            DontDestroyOnLoad(go);
            go.AddComponent<MiningUI>();
        }

        private static MiningUI FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<MiningUI>();
#else
            return UnityEngine.Object.FindObjectOfType<MiningUI>();
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
        }

        private void EnsureProgressObjects()
        {
            if (progressRoot == null)
                CreateProgressBar();
            if (pickaxeRoot == null)
                CreatePickaxeSprite();
        }

        private void CreateProgressBar()
        {
            if (progressRoot != null)
                return;

            progressRoot = new GameObject("MiningProgress");
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
            progressImage.color = Color.green;
            progressImage.sprite = bgSprite;
            progressImage.type = Image.Type.Filled;
            progressImage.fillMethod = Image.FillMethod.Horizontal;
            progressImage.fillAmount = 0f;
            var fillRect = progressImage.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            progressRoot.SetActive(false);
        }

        private void CreatePickaxeSprite()
        {
            if (pickaxeRoot != null)
                return;

            pickaxeRoot = new GameObject("MiningPickaxe");
            pickaxeRoot.transform.SetParent(transform);
            pickaxeRenderer = pickaxeRoot.AddComponent<SpriteRenderer>();
            pickaxeRenderer.sortingOrder = 100;
            pickaxeRoot.SetActive(false);
        }

        private void HandleStart(MineableRock rock)
        {
            EnsureProgressObjects();
            target = rock.transform;
            progressImage.fillAmount = 0f;
            currentFill = 0f;
            tickTimer = 0f;
            step = skill.CurrentSwingSpeedTicks > 0 ? 1f / skill.CurrentSwingSpeedTicks : 0f;
            nextFill = step;
            awaitingResetTick = false;
            segmentDuration = ResolveInitialSegmentDuration();
            progressRoot.SetActive(true);
            var pick = skill.CurrentPickaxe;
            if (pick != null && pickaxeRenderer != null)
            {
                var item = Resources.Load<ItemData>("Item/" + pick.Id);
                if (item != null && item.icon != null)
                {
                    pickaxeRenderer.sprite = item.icon;
                    pickaxeRoot.SetActive(true);
                }
            }
            var targetRenderer = rock.GetComponent<SpriteRenderer>();
            if (targetRenderer != null)
            {
                if (progressCanvas != null)
                {
                    progressCanvas.sortingLayerID = targetRenderer.sortingLayerID;
                    progressCanvas.sortingOrder = targetRenderer.sortingOrder + 1;
                }
                if (pickaxeRenderer != null)
                {
                    pickaxeRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    pickaxeRenderer.sortingOrder = targetRenderer.sortingOrder + 2;
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
            if (pickaxeRoot != null)
            {
                pickaxeRoot.SetActive(false);
                if (pickaxeRenderer != null)
                    pickaxeRenderer.sprite = null;
            }
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureProgressObjects();
            RefreshSkillSubscription();
        }

        private void RefreshSkillSubscription()
        {
            var current = FindObjectOfType<MiningSkill>();
            if (current == skill)
                return;

            DetachFromSkill();
            skill = current;
            if (skill != null)
            {
                skill.OnStartMining += HandleStart;
                skill.OnStopMining += HandleStop;
            }
        }

        private void DetachFromSkill()
        {
            if (skill != null)
            {
                skill.OnStartMining -= HandleStart;
                skill.OnStopMining -= HandleStop;
                skill = null;
            }
        }

        private void Update()
        {
            if (target == null || progressImage == null || skill == null)
                return;

            progressRoot.transform.position = target.position + offset;
            if (pickaxeRoot != null && pickaxeRoot.activeSelf)
                pickaxeRoot.transform.position = target.position + pickaxeOffset;

            tickTimer += Time.deltaTime;
            if (segmentDuration <= 0f)
            {
                progressImage.fillAmount = nextFill;
                return;
            }

            float t = Mathf.Clamp01(tickTimer / segmentDuration);
            progressImage.fillAmount = Mathf.Lerp(currentFill, nextFill, t);
        }

        public void OnTick()
        {
            segmentDuration = Ticker.TickDuration;
            if (target == null || skill == null || !skill.IsMining)
                return;

            tickTimer = 0f;
            // No valid swing speed means we cannot animate progress, so stay at zero.
            if (step <= 0f)
            {
                currentFill = 0f;
                nextFill = 0f;
                awaitingResetTick = false;
                if (progressImage != null)
                    progressImage.fillAmount = 0f;
                return;
            }

            if (awaitingResetTick)
            {
                awaitingResetTick = false;
                currentFill = 0f;
                nextFill = step;
                if (progressImage != null)
                    progressImage.fillAmount = 0f;
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

                if (Ticker.Instance != null)
                    Ticker.Instance.Unsubscribe(this);

                if (progressRoot != null)
                {
                    Destroy(progressRoot);
                    progressRoot = null;
                }

                if (pickaxeRoot != null)
                {
                    Destroy(pickaxeRoot);
                    pickaxeRoot = null;
                }

                progressImage = null;
                pickaxeRenderer = null;
                progressCanvas = null;
                target = null;

                instance = null;

                if (!applicationIsQuitting)
                    BeginWaitingForAllowedScene();
            }
        }

        /// <summary>
        /// Computes the first interpolation window using the ticker's remaining time so partial ticks animate correctly.
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
    }
}
