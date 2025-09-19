using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// Centralises the logic for determining whether persistent singletons should exist in the
    /// current scene. The gate caches the <see cref="PersistentObjectCatalog"/> so bootstrap
    /// routines can avoid spawning long-lived services in menu or login scenes where they are not
    /// required.
    /// </summary>
    public static class PersistentSceneGate
    {
        /// <summary>
        /// Raised whenever Unity reports that a scene finished loading or when the active scene
        /// changes. The boolean argument communicates whether persistent services are allowed in the
        /// evaluated scene.
        /// </summary>
        public static event Action<Scene, bool> SceneEvaluationChanged;

        private static PersistentObjectCatalog cachedCatalog;
        private static bool attemptedCatalogLoad;
        private static bool initialised;
        private static bool? lastEvaluation;
        private static Scene lastEvaluatedScene;

        /// <summary>
        /// Ensures the helper is initialised prior to the first scene load so any bootstrap code can
        /// immediately query the active scene.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialise()
        {
            EnsureInitialised();
        }

        /// <summary>
        /// Determines whether persistent services should be spawned within the provided scene.
        /// </summary>
        /// <param name="scene">Scene to evaluate.</param>
        public static bool ShouldSpawnInScene(Scene scene)
        {
            EnsureInitialised();
            return EvaluateScene(scene, broadcast: false);
        }

        /// <summary>
        /// Returns the cached evaluation for the current active scene, recomputing the value if the
        /// cached state is stale.
        /// </summary>
        public static bool IsActiveSceneAllowed
        {
            get
            {
                EnsureInitialised();
                var activeScene = SceneManager.GetActiveScene();
                if (lastEvaluation.HasValue && lastEvaluatedScene == activeScene)
                    return lastEvaluation.Value;

                return EvaluateScene(activeScene, broadcast: false);
            }
        }

        /// <summary>
        /// Forces a re-evaluation of the active scene and broadcasts the result to listeners. Useful
        /// when bootstrap code needs to manually refresh the state after changing exclusion data at
        /// runtime.
        /// </summary>
        public static void RefreshActiveScene()
        {
            EnsureInitialised();
            var activeScene = SceneManager.GetActiveScene();
            EvaluateScene(activeScene, broadcast: true);
        }

        private static void EnsureInitialised()
        {
            if (initialised)
                return;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            initialised = true;

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
                EvaluateScene(activeScene, broadcast: true);
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EvaluateScene(scene, broadcast: true);
        }

        private static void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            EvaluateScene(newScene, broadcast: true);
        }

        private static bool EvaluateScene(Scene scene, bool broadcast)
        {
            if (!scene.IsValid())
            {
                if (broadcast)
                    SceneEvaluationChanged?.Invoke(scene, false);
                return false;
            }

            bool allowed = IsSceneAllowed(scene);

            lastEvaluatedScene = scene;
            lastEvaluation = allowed;

            if (broadcast)
                SceneEvaluationChanged?.Invoke(scene, allowed);

            return allowed;
        }

        private static bool IsSceneAllowed(Scene scene)
        {
            var catalog = GetCatalog();
            if (catalog == null)
                return true;

            return catalog.ShouldSpawnInScene(scene.name);
        }

        private static PersistentObjectCatalog GetCatalog()
        {
            if (!attemptedCatalogLoad || cachedCatalog == null)
            {
                cachedCatalog = Resources.Load<PersistentObjectCatalog>(PersistentObjectBootstrap.CatalogResourcePath);
                attemptedCatalogLoad = true;
            }

            return cachedCatalog;
        }
    }
}
