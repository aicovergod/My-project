using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using World;

namespace UI
{
    public interface IUIWindow
    {
        bool IsOpen { get; }
        void Close();
    }

    /// <summary>
    /// Central manager for UI windows. Opening one window closes any others.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        private readonly List<IUIWindow> windows = new List<IUIWindow>();
        private bool sceneGateSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                BeginWaitingForAllowedScene();
                return;
            }

            if (PersistentSceneGate.ShouldSpawnInScene(activeScene))
            {
                CreateOrAdoptInstance();
            }
            else
            {
                BeginWaitingForAllowedScene();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                if (sceneGateSubscribed)
                {
                    PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
                    sceneGateSubscribed = false;
                }

                Instance = null;

                if (!applicationIsQuitting)
                    BeginWaitingForAllowedScene();
            }
        }

        public void RegisterWindow(IUIWindow window)
        {
            if (!windows.Contains(window))
                windows.Add(window);
        }

        public void OpenWindow(IUIWindow window)
        {
            foreach (var w in windows)
            {
                if (w != window && w.IsOpen)
                    w.Close();
            }
        }

        private static void CreateOrAdoptInstance()
        {
            if (Instance != null)
                return;

            StopWaitingForAllowedScene();

            var existing = FindExistingManager();
            if (existing != null)
            {
                Instance = existing;
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    DontDestroyOnLoad(existing.gameObject);
                existing.EnsureSceneGateSubscription();
                return;
            }

            var go = new GameObject(nameof(UIManager));
            DontDestroyOnLoad(go);
            go.AddComponent<UIManager>();
        }

        private static UIManager FindExistingManager()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<UIManager>();
#else
            return Object.FindObjectOfType<UIManager>();
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
    }
}
