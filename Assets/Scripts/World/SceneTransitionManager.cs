using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// Manages moving key objects between scenes and handling fade transitions.
    /// </summary>
    public class SceneTransitionManager : MonoBehaviour
    {
        public static SceneTransitionManager Instance;
        public static bool IsTransitioning;

        public static event Action TransitionStarted;
        public static event Action TransitionCompleted;

        private static readonly System.Collections.Generic.List<IScenePersistent> _persistentObjects = new();
        public static string NextSpawnPoint { get; private set; }

        public static void RegisterPersistentObject(IScenePersistent obj)
        {
            if (obj != null && !_persistentObjects.Contains(obj))
                _persistentObjects.Add(obj);
        }

        public static void UnregisterPersistentObject(IScenePersistent obj)
        {
            if (obj != null)
                _persistentObjects.Remove(obj);
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
        }

        public IEnumerator Transition(string sceneToLoad, string spawnPointName, string requiredItemId, bool removeItemOnUse)
        {
            if (IsTransitioning)
                yield break;

            if (string.IsNullOrEmpty(sceneToLoad))
                yield break;

            IsTransitioning = true;
            TransitionStarted?.Invoke();

            if (ScreenFader.Instance == null)
                new GameObject("ScreenFader").AddComponent<ScreenFader>();

            if (ScreenFader.Instance != null)
                yield return ScreenFader.Instance.FadeOut();

            NextSpawnPoint = spawnPointName;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && removeItemOnUse && !string.IsNullOrEmpty(requiredItemId))
            {
                var inv = player.GetComponent<Inventory.Inventory>();
                if (inv != null)
                    inv.RemoveItem(requiredItemId);
            }

            foreach (var obj in _persistentObjects)
                obj.OnBeforeSceneUnload();

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Load the new scene additively so we can explicitly set it active and
            // unload the previous scene once loading completes.  This prevents the
            // previous scene from lingering if the default single-mode load fails on
            // some platforms and ensures the overworld becomes the active scene.
            var currentScene = SceneManager.GetActiveScene();
            var loadOp = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Additive);
            while (!loadOp.isDone)
                yield return null;

            var loadedScene = SceneManager.GetSceneByName(sceneToLoad);
            SceneManager.SetActiveScene(loadedScene);

            var unloadOp = SceneManager.UnloadSceneAsync(currentScene);
            while (unloadOp != null && !unloadOp.isDone)
                yield return null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var obj in _persistentObjects)
                obj.OnAfterSceneLoad(scene);

            SceneManager.sceneLoaded -= OnSceneLoaded;
            NextSpawnPoint = null;

            if (ScreenFader.Instance != null)
                StartCoroutine(FadeInRoutine());
            else
                OnFadeInComplete();
        }

        private IEnumerator FadeInRoutine()
        {
            yield return ScreenFader.Instance.FadeIn();
            OnFadeInComplete();
        }

        private void OnFadeInComplete()
        {
            IsTransitioning = false;
            TransitionCompleted?.Invoke();
        }
    }
}
