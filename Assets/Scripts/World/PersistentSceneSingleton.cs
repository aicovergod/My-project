using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace World
{
    /// <summary>
    /// Helper that centralises the boilerplate required for persistent scene gated singletons.
    /// The wrapper keeps track of the current instance, automatically subscribes to
    /// <see cref="PersistentSceneGate"/>, survives scene loads via <see cref="Object.DontDestroyOnLoad"/>,
    /// and recreates the singleton when the active scene becomes eligible again.
    /// </summary>
    /// <typeparam name="T">Type of the persistent singleton.</typeparam>
    public static class PersistentSceneSingleton<T> where T : MonoBehaviour
    {
        private static T instance;
        private static Func<T> factory;
        private static bool waitingForAllowedScene;
        private static bool sceneGateSubscribed;
        private static bool quitHooked;
        private static bool applicationIsQuitting;
        private static Action<Scene, bool> sceneEvaluationHandler;

        /// <summary>
        /// Returns the active singleton instance if it currently exists.
        /// </summary>
        public static T Instance => instance;

        /// <summary>
        /// Entry point used by bootstrap methods. The helper evaluates the active scene and either
        /// creates/adopts the singleton immediately or begins waiting for the next permitted scene.
        /// </summary>
        /// <param name="factoryMethod">
        /// Optional custom factory responsible for spawning the singleton. When omitted the helper
        /// instantiates a new <see cref="GameObject"/> named after <typeparamref name="T"/> and
        /// attaches the component.
        /// </param>
        public static void Bootstrap(Func<T> factoryMethod = null)
        {
            if (applicationIsQuitting)
                return;

            if (factoryMethod != null)
                factory = factoryMethod;
            else if (factory == null)
                factory = DefaultFactory;

            EnsureSubscriptions();

            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !PersistentSceneGate.ShouldSpawnInScene(activeScene))
            {
                waitingForAllowedScene = true;
                return;
            }

            CreateOrAdoptInstance();
        }

        /// <summary>
        /// Handles <see cref="MonoBehaviour.Awake"/> for the singleton. When the provided
        /// <paramref name="candidate"/> is accepted as the canonical instance the method returns
        /// <c>true</c>; otherwise the duplicate is destroyed and <c>false</c> is returned.
        /// </summary>
        /// <param name="candidate">Instance being initialised.</param>
        public static bool HandleAwake(T candidate)
        {
            if (applicationIsQuitting)
            {
                Object.Destroy(candidate.gameObject);
                return false;
            }

            factory ??= DefaultFactory;
            EnsureSubscriptions();

            if (instance != null && instance != candidate)
            {
                Object.Destroy(candidate.gameObject);
                return false;
            }

            instance = candidate;
            waitingForAllowedScene = false;

            if (candidate.gameObject.scene.name != "DontDestroyOnLoad")
                Object.DontDestroyOnLoad(candidate.gameObject);

            return true;
        }

        /// <summary>
        /// Handles <see cref="MonoBehaviour.OnDestroy"/>. Returns <c>true</c> when teardown for the
        /// canonical instance should continue. When the component being destroyed is a duplicate the
        /// helper returns <c>false</c>, signalling that the caller should skip any additional cleanup.
        /// </summary>
        /// <param name="candidate">Component that is being destroyed.</param>
        public static bool HandleOnDestroy(T candidate)
        {
            if (instance != candidate)
                return false;

            instance = null;

            if (applicationIsQuitting)
                return true;

            waitingForAllowedScene = true;

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && PersistentSceneGate.ShouldSpawnInScene(activeScene))
                CreateOrAdoptInstance();

            return true;
        }

        /// <summary>
        /// Ensures the helper is subscribed to <see cref="PersistentSceneGate"/> and the global quit
        /// notification so that state is updated automatically as the application shuts down.
        /// </summary>
        private static void EnsureSubscriptions()
        {
            if (!sceneGateSubscribed)
            {
                sceneEvaluationHandler ??= HandleSceneEvaluation;
                PersistentSceneGate.SceneEvaluationChanged += sceneEvaluationHandler;
                sceneGateSubscribed = true;
            }

            if (!quitHooked)
            {
                Application.quitting += HandleApplicationQuitting;
                quitHooked = true;
            }
        }

        private static void HandleApplicationQuitting()
        {
            applicationIsQuitting = true;
        }

        private static void HandleSceneEvaluation(Scene scene, bool allowed)
        {
            if (applicationIsQuitting)
                return;

            if (scene != SceneManager.GetActiveScene())
                return;

            if (allowed)
            {
                if (waitingForAllowedScene && instance == null)
                    CreateOrAdoptInstance();
            }
            else
            {
                waitingForAllowedScene = true;
                if (instance != null)
                    Object.Destroy(instance.gameObject);
            }
        }

        private static void CreateOrAdoptInstance()
        {
            if (applicationIsQuitting)
                return;

            if (instance != null)
                return;

            if (!PersistentSceneGate.IsActiveSceneAllowed)
            {
                waitingForAllowedScene = true;
                return;
            }

            waitingForAllowedScene = false;

            var existing = FindExistingInstance();
            if (existing != null)
            {
                if (existing.gameObject.scene.name != "DontDestroyOnLoad")
                    Object.DontDestroyOnLoad(existing.gameObject);
                instance = existing;
                return;
            }

            var created = (factory ??= DefaultFactory)?.Invoke();
            if (created == null)
            {
                waitingForAllowedScene = true;
                return;
            }

            if (created.gameObject.scene.name != "DontDestroyOnLoad")
                Object.DontDestroyOnLoad(created.gameObject);

            instance = created;
        }

        private static T FindExistingInstance()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        private static T DefaultFactory()
        {
            var go = new GameObject(typeof(T).Name);
            return go.AddComponent<T>();
        }
    }
}
