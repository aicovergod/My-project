using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Status;
using World;

namespace UI.HUD
{
    /// <summary>
    /// Manages buff infobox UI anchored next to the minimap.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuffHudManager : MonoBehaviour
    {
        private static BuffHudManager instance;
        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        /// <summary>
        /// Global accessor used by systems that need to query the HUD manager at runtime.
        /// </summary>
        public static BuffHudManager Instance => instance;

        [SerializeField] private BuffInfoBox infoBoxPrefab;
        [SerializeField] private float topPadding = 8f;
        [SerializeField] private float leftPadding = 8f;
        [SerializeField] private float verticalSpacing = 2f;
        [SerializeField] private int columns = 3;
        [SerializeField] private float horizontalSpacing = 2f;
        [SerializeField] private BuffType[] ordering;
        [SerializeField] private bool playExpiryNotification = true;
        [SerializeField] private AudioClip expiryClip;

        private readonly Dictionary<BuffKey, BuffInfoBox> activeBoxes = new();
        private bool sceneGateSubscribed;

        private RectTransform container;
        private RectTransform anchor;
        private GameObject player;
        private AudioSource audioSource;

        /// <summary>
        /// Cached reference to the timer service currently driving this HUD. Tracking the
        /// reference allows the manager to detect when the underlying singleton is recreated
        /// during scene transitions or domain reloads so handlers can be rewired safely.
        /// </summary>
        private BuffTimerService subscribedService;

        /// <summary>
        /// Indicates whether the HUD has successfully registered event handlers with the timer
        /// service. The flag prevents duplicate subscriptions and gives <see cref="LateUpdate"/>
        /// a reliable signal for when a retry is required because the service was not yet ready.
        /// </summary>
        private bool serviceHandlersAttached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInstanceExists()
        {
            if (Instance != null)
                return;

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !PersistentSceneGate.ShouldSpawnInScene(activeScene))
            {
                BeginWaitingForAllowedScene();
                return;
            }

            CreateOrAdoptManager();
        }

        private static BuffHudManager FindFirstManager()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<BuffHudManager>();
#else
            return UnityEngine.Object.FindObjectOfType<BuffHudManager>();
#endif
        }

        private static void CreateOrAdoptManager()
        {
            if (Instance != null)
                return;

            StopWaitingForAllowedScene();

            var existing = FindFirstManager();
            if (existing != null)
            {
                instance = existing;
                existing.EnsurePersistence();
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    DontDestroyOnLoad(existing.gameObject);
                existing.TryInitialiseContainer();
                existing.RefreshPlayerReference();
                existing.RebuildExistingBuffs();
                existing.EnsureSceneGateSubscription();
                return;
            }

            var go = new GameObject("BuffHudManager");
            go.AddComponent<ScenePersistentObject>();
            var manager = go.AddComponent<BuffHudManager>();
            DontDestroyOnLoad(go);
            manager.TryInitialiseContainer();
            manager.RefreshPlayerReference();
            manager.RebuildExistingBuffs();
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

            CreateOrAdoptManager();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            EnsurePersistence();
            DontDestroyOnLoad(gameObject);

            if (infoBoxPrefab == null)
            {
                var loaded = Resources.Load<BuffInfoBox>("UI/Status/BuffInfoBox");
                if (loaded == null)
                {
                    var prefabGO = Resources.Load<GameObject>("UI/Status/BuffInfoBox");
                    if (prefabGO != null)
                        loaded = prefabGO.GetComponent<BuffInfoBox>();
                }
                infoBoxPrefab = loaded;
            }

            if (expiryClip != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }

            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();
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

                instance = null;

                if (!applicationIsQuitting)
                    BeginWaitingForAllowedScene();
            }
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SubscribeToService();
            TryInitialiseContainer();
            RefreshPlayerReference();
            RebuildExistingBuffs();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeFromService();
            ClearBoxes();
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
            if (Instance != this)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            if (allowed)
                return;

            PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
            sceneGateSubscribed = false;
            Destroy(gameObject);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshPlayerReference();
            TryInitialiseContainer();
            RebuildExistingBuffs();
        }

        private void LateUpdate()
        {
            if (container == null || anchor == null)
                TryInitialiseContainer();
            if (player == null)
                RefreshPlayerReference();

            // Ensure we remain subscribed even if the timer service is spawned after this HUD
            // (such as during a scene load) or if it gets replaced by a new instance.
            var currentService = BuffTimerService.Instance;
            if (serviceHandlersAttached && (subscribedService == null || currentService != subscribedService))
                DetachFromTrackedService();

            if (!serviceHandlersAttached)
                SubscribeToService();
        }

        private void SubscribeToService()
        {
            if (serviceHandlersAttached && subscribedService == BuffTimerService.Instance)
                return;

            if (serviceHandlersAttached && subscribedService != BuffTimerService.Instance)
                DetachFromTrackedService();

            var service = BuffTimerService.Instance;
            if (service == null)
                return;

            service.BuffStarted += HandleBuffStarted;
            service.BuffUpdated += HandleBuffUpdated;
            service.BuffWarning += HandleBuffWarning;
            service.BuffEnded += HandleBuffEnded;

            subscribedService = service;
            serviceHandlersAttached = true;

            RebuildExistingBuffs();
        }

        private void UnsubscribeFromService()
        {
            DetachFromTrackedService();
        }

        /// <summary>
        /// Removes the HUD's event handlers from whichever timer service instance they were
        /// registered with and resets the tracking flags so a fresh subscription can occur later.
        /// </summary>
        private void DetachFromTrackedService()
        {
            if (subscribedService != null)
            {
                subscribedService.BuffStarted -= HandleBuffStarted;
                subscribedService.BuffUpdated -= HandleBuffUpdated;
                subscribedService.BuffWarning -= HandleBuffWarning;
                subscribedService.BuffEnded -= HandleBuffEnded;
            }

            subscribedService = null;
            serviceHandlersAttached = false;
        }

        private void HandleBuffStarted(BuffTimerInstance instance)
        {
            if (!IsPlayerBuff(instance))
                return;

            EnsureContainer();
            CreateOrUpdateBox(instance);
        }

        private void HandleBuffUpdated(BuffTimerInstance instance)
        {
            if (!IsPlayerBuff(instance))
                return;

            if (activeBoxes.TryGetValue(instance.Key, out var box))
            {
                box.UpdateTimer(instance);
                box.ResetVisuals();
            }
            else
            {
                CreateOrUpdateBox(instance);
            }
        }

        private void HandleBuffWarning(BuffTimerInstance instance)
        {
            if (!IsPlayerBuff(instance))
                return;

            if (activeBoxes.TryGetValue(instance.Key, out var box))
            {
                box.SetWarning(true);
                if (playExpiryNotification && expiryClip != null)
                    audioSource?.PlayOneShot(expiryClip);
            }
        }

        private void HandleBuffEnded(BuffTimerInstance instance, BuffEndReason reason)
        {
            if (!activeBoxes.TryGetValue(instance.Key, out var box))
                return;

            activeBoxes.Remove(instance.Key);
            if (box != null)
                Destroy(box.gameObject);
            LayoutBoxes();
        }

        private void CreateOrUpdateBox(BuffTimerInstance instance)
        {
            if (container == null)
                return;

            if (!activeBoxes.TryGetValue(instance.Key, out var box) || box == null)
            {
                box = CreateInfoBox();
                if (box == null)
                    return;
                activeBoxes[instance.Key] = box;
            }

            box.Bind(instance);
            LayoutBoxes();
        }

        private BuffInfoBox CreateInfoBox()
        {
            if (container == null)
                return null;

            if (infoBoxPrefab != null)
                return Instantiate(infoBoxPrefab, container);

            return BuffInfoBox.Create(container);
        }

        private void LayoutBoxes()
        {
            if (container == null)
                return;

            var ordered = new List<BuffInfoBox>(activeBoxes.Values);
            ordered.Sort(CompareBoxes);

            int effectiveColumns = Mathf.Max(1, columns);

            for (int i = 0; i < ordered.Count; i++)
            {
                var box = ordered[i];
                if (box == null)
                    continue;

                var rect = box.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);

                float rectWidth = rect.rect.width;
                float rectHeight = rect.rect.height;
                if (rectWidth <= 0f)
                    rectWidth = rect.sizeDelta.x;
                if (rectHeight <= 0f)
                    rectHeight = rect.sizeDelta.y;

                int column = i % effectiveColumns;
                int row = i / effectiveColumns;

                float x = -(rectWidth + horizontalSpacing) * column;
                float y = -(rectHeight + verticalSpacing) * row;
                rect.anchoredPosition = new Vector2(x, y);
            }
        }

        private int CompareBoxes(BuffInfoBox a, BuffInfoBox b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            int orderA = GetOrderIndex(a.BoundBuff);
            int orderB = GetOrderIndex(b.BoundBuff);
            if (orderA != orderB)
                return orderA.CompareTo(orderB);
            return a.BoundBuff.SequenceId.CompareTo(b.BoundBuff.SequenceId);
        }

        private int GetOrderIndex(BuffTimerInstance instance)
        {
            if (ordering == null || ordering.Length == 0)
                return (int)instance.Definition.type;
            for (int i = 0; i < ordering.Length; i++)
            {
                if (ordering[i] == instance.Definition.type)
                    return i;
            }
            return ordering.Length + (int)instance.Definition.type;
        }

        private bool IsPlayerBuff(BuffTimerInstance instance)
        {
            if (instance.Target == null)
                return false;
            if (player == null)
                RefreshPlayerReference();
            return instance.Target == player || instance.Target.CompareTag("Player");
        }

        private void RefreshPlayerReference()
        {
            if (player != null)
                return;
            player = GameObject.FindGameObjectWithTag("Player");
        }

        private void TryInitialiseContainer()
        {
            if (container != null && anchor != null)
                return;

            var minimap = Minimap.Instance;
            if (minimap == null)
                return;

            anchor = minimap.BorderRect != null ? minimap.BorderRect : minimap.SmallRootRect;
            if (anchor == null)
                return;

            EnsureContainer();
        }

        private void EnsureContainer()
        {
            if (anchor == null)
                return;

            if (container == null)
            {
                var go = new GameObject("BuffHud", typeof(RectTransform));
                container = go.GetComponent<RectTransform>();
                go.layer = anchor.gameObject.layer;
                container.SetParent(anchor, false);
                container.anchorMin = new Vector2(1f, 1f);
                container.anchorMax = new Vector2(1f, 1f);
                container.pivot = new Vector2(1f, 1f);
            }

            float anchorWidth = anchor.rect.width;
            if (anchorWidth <= 0f)
                anchorWidth = anchor.sizeDelta.x;

            container.anchoredPosition = new Vector2(-(anchorWidth + leftPadding), -topPadding);
            LayoutBoxes();
        }

        private void EnsurePersistence()
        {
            if (GetComponent<ScenePersistentObject>() == null)
                gameObject.AddComponent<ScenePersistentObject>();
        }

        private void RebuildExistingBuffs()
        {
            ClearBoxes();
            if (BuffTimerService.Instance == null)
                return;

            foreach (var pair in BuffTimerService.Instance.ActiveBuffs)
            {
                var instance = pair.Value;
                if (!IsPlayerBuff(instance))
                    continue;
                CreateOrUpdateBox(instance);
            }
        }

        private void ClearBoxes()
        {
            foreach (var entry in activeBoxes.Values)
            {
                if (entry != null)
                    Destroy(entry.gameObject);
            }
            activeBoxes.Clear();
        }
    }
}
