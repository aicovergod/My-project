using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Core.Save;
using World;

namespace Books
{
    public class BookProgressManager : MonoBehaviour, ISaveable
    {
        public static BookProgressManager Instance { get; private set; }

        private Dictionary<string, int> progress = new Dictionary<string, int>();
        private const string SaveKey = "BookProgress";

        private static bool waitingForAllowedScene;
        private static bool applicationIsQuitting;

        private bool sceneGateSubscribed;

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
            if (Instance != null)
                return;

            StopWaitingForAllowedScene();

            var existing = FindExistingInstance();
            if (existing != null)
            {
                Instance = existing;
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    DontDestroyOnLoad(existing.gameObject);
                existing.EnsureSceneGateSubscription();
                return;
            }

            var go = new GameObject(nameof(BookProgressManager));
            DontDestroyOnLoad(go);
            go.AddComponent<BookProgressManager>();
        }

        private static BookProgressManager FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<BookProgressManager>();
#else
            return UnityEngine.Object.FindObjectOfType<BookProgressManager>();
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
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SaveManager.Register(this);

            StopWaitingForAllowedScene();
            EnsureSceneGateSubscription();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SaveManager.Unregister(this);
                Instance = null;

                if (sceneGateSubscribed)
                {
                    PersistentSceneGate.SceneEvaluationChanged -= HandleSceneGateEvaluation;
                    sceneGateSubscribed = false;
                }

                if (!applicationIsQuitting)
                    BeginWaitingForAllowedScene();
            }
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
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

        public int GetPage(string id)
        {
            return progress.TryGetValue(id, out var page) ? page : 0;
        }

        public void SetPage(string id, int page)
        {
            progress[id] = page;
        }

        [System.Serializable]
        private class Data
        {
            public List<string> ids = new List<string>();
            public List<int> pages = new List<int>();
        }

        public void Save()
        {
            var data = new Data();
            foreach (var kv in progress)
            {
                data.ids.Add(kv.Key);
                data.pages.Add(kv.Value);
            }
            SaveManager.Save(SaveKey, data);
        }

        public void Load()
        {
            var data = SaveManager.Load<Data>(SaveKey);
            progress.Clear();
            if (data?.ids != null && data.pages != null)
            {
                for (int i = 0; i < data.ids.Count && i < data.pages.Count; i++)
                    progress[data.ids[i]] = data.pages[i];
            }
        }
    }
}
