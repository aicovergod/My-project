using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// Manages moving key objects between scenes and handling fade transitions.
    /// </summary>
    public class SceneTransitionManager : ScenePersistentObject
    {
        public static SceneTransitionManager Instance;
        public static bool IsTransitioning;

        public static event Action TransitionStarted;
        public static event Action TransitionCompleted;

        private static readonly System.Collections.Generic.List<IScenePersistent> _persistentObjects = new();
        public static string NextSpawnPoint { get; private set; }

        /// <summary>
        /// Tracks whether a required item was consumed during the current transition.
        /// When a transition fails we use this flag to restore the item.
        /// </summary>
        private bool _consumedRequiredItemThisTransition;

        /// <summary>
        /// Caches the identifier of the consumed item so it can be re-granted if the
        /// transition fails after removal.
        /// </summary>
        private string _consumedRequiredItemId;

        /// <summary>
        /// Allows external systems to manually start a transition without invoking the
        /// fade routines. This ensures persistent objects receive their unload callbacks
        /// before a caller begins loading the next scene.
        /// </summary>
        /// <param name="nextScene">
        /// Scene that will become active after the manual transition completes. The
        /// scene may be invalid when invoked ahead of the asynchronous load; the manager
        /// does not require it to be valid.
        /// </param>
        public void BeginManualTransition(Scene nextScene)
        {
            if (IsTransitioning)
            {
                Debug.LogWarning("[SceneTransitionManager] BeginManualTransition called while a transition is already running. Duplicate call ignored.");
                return;
            }

            NextSpawnPoint = null;
            ResetConsumedItemTracking();

            IsTransitioning = true;
            TransitionStarted?.Invoke();

            RemoveNullPersistentObjects();

            foreach (var obj in _persistentObjects)
                obj.OnBeforeSceneUnload();
        }

        /// <summary>
        /// Completes a manual transition by delivering load callbacks and signalling
        /// listeners that the transition has finished. Mirrors the regular transition
        /// flow without triggering fade-in visuals.
        /// </summary>
        /// <param name="loadedScene">
        /// Scene that should be passed into <see cref="IScenePersistent.OnAfterSceneLoad(Scene)"/>.
        /// If invalid, the currently active scene will be used instead.
        /// </param>
        public void CompleteManualTransition(Scene loadedScene)
        {
            Scene sceneForCallbacks = loadedScene.IsValid() ? loadedScene : SceneManager.GetActiveScene();

            RemoveNullPersistentObjects();

            foreach (var obj in _persistentObjects)
                obj.OnAfterSceneLoad(sceneForCallbacks);

            EnsureSingleAudioListener(sceneForCallbacks);

            NextSpawnPoint = null;

            IsTransitioning = false;
            TransitionCompleted?.Invoke();

            ResetConsumedItemTracking();
        }

        /// <summary>
        /// Clears all consumed item tracking so future transitions start clean.
        /// </summary>
        private void ResetConsumedItemTracking()
        {
            _consumedRequiredItemThisTransition = false;
            _consumedRequiredItemId = null;
        }

        /// <summary>
        /// Removes any null references from the persistent object cache so we do
        /// not attempt to invoke callbacks on destroyed entries.
        /// </summary>
        private static void RemoveNullPersistentObjects()
        {
            for (int i = _persistentObjects.Count - 1; i >= 0; i--)
            {
                if (_persistentObjects[i] == null)
                {
                    _persistentObjects.RemoveAt(i);
                }
            }
        }

        public static void RegisterPersistentObject(IScenePersistent obj)
        {
            RemoveNullPersistentObjects();

            if (obj != null && !_persistentObjects.Contains(obj))
                _persistentObjects.Add(obj);
        }

        public static void UnregisterPersistentObject(IScenePersistent obj)
        {
            RemoveNullPersistentObjects();

            if (obj != null)
                _persistentObjects.Remove(obj);
        }

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            base.Awake();

            Instance = this;
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

            // Reset tracking before attempting to consume any required key items.
            ResetConsumedItemTracking();

            var player = GameObject.FindGameObjectWithTag("Player");
            bool removalRequired = removeItemOnUse && !string.IsNullOrEmpty(requiredItemId);
            if (removalRequired)
            {
                if (player == null)
                {
                    yield return AbortTransitionDueToItemRemovalFailure("[SceneTransitionManager] Cannot transition because the player was not found to remove the required key item.");
                    yield break;
                }

                var inv = player.GetComponent<Inventory.Inventory>();
                if (inv == null)
                {
                    yield return AbortTransitionDueToItemRemovalFailure("[SceneTransitionManager] Cannot transition because the player's inventory component is missing for required item removal.");
                    yield break;
                }

                bool removed = inv.RemoveItem(requiredItemId);
                if (!removed)
                {
                    yield return AbortTransitionDueToItemRemovalFailure($"[SceneTransitionManager] Transition aborted. Player is missing required item '{requiredItemId}'.");
                    yield break;
                }

                _consumedRequiredItemThisTransition = true;
                _consumedRequiredItemId = requiredItemId;
            }

            NextSpawnPoint = spawnPointName;

            RemoveNullPersistentObjects();

            foreach (var obj in _persistentObjects)
                obj.OnBeforeSceneUnload();

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Load the new scene additively so we can explicitly set it active and
            // unload the previous scene once loading completes.  This prevents the
            // previous scene from lingering if the default single-mode load fails on
            // some platforms and ensures the overworld becomes the active scene.
            var currentScene = SceneManager.GetActiveScene();
            var loadOp = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Additive);
            if (loadOp == null)
            {
                yield return RecoverFromFailedTransition(sceneToLoad, currentScene, "LoadSceneAsync returned null.");
                yield break;
            }

            while (!loadOp.isDone)
                yield return null;

            var loadedScene = SceneManager.GetSceneByName(sceneToLoad);
            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
            {
                yield return RecoverFromFailedTransition(sceneToLoad, currentScene, "Loaded scene was invalid or failed to load.");
                yield break;
            }

            SceneManager.SetActiveScene(loadedScene);

            var unloadOp = SceneManager.UnloadSceneAsync(currentScene);
            if (unloadOp == null)
            {
                Debug.LogError($"[SceneTransitionManager] Failed to unload scene '{currentScene.name}'. UnloadSceneAsync returned null.");

                SceneManager.sceneLoaded -= OnSceneLoaded;
                NextSpawnPoint = null;

                if (ScreenFader.Instance != null)
                    yield return ScreenFader.Instance.FadeIn();

                IsTransitioning = false;
                TransitionCompleted?.Invoke();
                yield break;
            }

            while (!unloadOp.isDone)
                yield return null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var obj in _persistentObjects)
                obj.OnAfterSceneLoad(scene);

            EnsureSingleAudioListener(scene);

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

            // Ensure we do not carry over key-consumption state between transitions.
            ResetConsumedItemTracking();
        }

        private IEnumerator AbortTransitionDueToItemRemovalFailure(string message)
        {
            Debug.LogWarning(message);

            NextSpawnPoint = null;
            ResetConsumedItemTracking();

            if (ScreenFader.Instance != null)
                yield return ScreenFader.Instance.FadeIn();

            IsTransitioning = false;
            TransitionCompleted?.Invoke();
        }

        /// <summary>
        /// Restores state when a scene load fails so the manager can accept future transitions.
        /// </summary>
        private IEnumerator RecoverFromFailedTransition(string sceneToLoad, Scene fallbackScene, string reason)
        {
            Debug.LogError($"[SceneTransitionManager] Failed to load scene '{sceneToLoad}'. {reason}");

            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (_consumedRequiredItemThisTransition && !string.IsNullOrEmpty(_consumedRequiredItemId))
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    var inventory = player.GetComponent<Inventory.Inventory>();
                    if (inventory != null)
                    {
                        var itemData = Inventory.ItemDatabase.GetItem(_consumedRequiredItemId);
                        if (itemData != null)
                        {
                            inventory.AddItem(itemData, 1);
                        }
                        else
                        {
                            Debug.LogWarning($"[SceneTransitionManager] Failed to restore required item '{_consumedRequiredItemId}' after transition error because it was not found in the item database.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[SceneTransitionManager] Failed to restore required item after transition error because the player's inventory component is missing.");
                    }
                }
                else
                {
                    Debug.LogWarning("[SceneTransitionManager] Failed to restore required item after transition error because the player GameObject was not found.");
                }

                ResetConsumedItemTracking();
            }

            RemoveNullPersistentObjects();

            foreach (var obj in _persistentObjects)
                obj.OnAfterSceneLoad(fallbackScene);

            NextSpawnPoint = null;

            if (ScreenFader.Instance != null)
                yield return ScreenFader.Instance.FadeIn();

            IsTransitioning = false;
            TransitionCompleted?.Invoke();

            ResetConsumedItemTracking();
        }

        /// <summary>
        /// Ensures that only one <see cref="AudioListener"/> remains enabled after a
        /// scene load.  If a persistent listener exists in the DontDestroyOnLoad
        /// scene, it is preferred; otherwise the listener from the newly loaded
        /// scene is used.
        /// </summary>
        private void EnsureSingleAudioListener(Scene loadedScene)
        {
            var listeners = FindObjectsOfType<AudioListener>();

            // Prefer an AudioListener that lives in the DontDestroyOnLoad scene so
            // that persistent objects like the player retain their listener.
            AudioListener listenerToKeep = null;
            foreach (var listener in listeners)
            {
                if (listener != null && listener.gameObject.scene.name == "DontDestroyOnLoad")
                {
                    listenerToKeep = listener;
                    break;
                }
            }

            // If none found, keep a listener from the newly loaded scene.
            if (listenerToKeep == null)
            {
                foreach (var listener in listeners)
                {
                    if (listener != null && listener.gameObject.scene == loadedScene)
                    {
                        listenerToKeep = listener;
                        break;
                    }
                }
            }

            // Disable any additional listeners to avoid duplicates.
            foreach (var listener in listeners)
                listener.enabled = listener == listenerToKeep;
        }
    }
}
