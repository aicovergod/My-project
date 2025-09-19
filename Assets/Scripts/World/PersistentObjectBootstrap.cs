using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// Ensures a curated list of prefabs exists in every scene.  The prefabs live in
    /// <c>PersistentObjects.asset</c> under <see cref="Resources"/> and are instantiated
    /// when missing so cross-scene singletons do not need to be placed manually in each
    /// scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class PersistentObjectBootstrap : MonoBehaviour
    {
        /// <summary>
        /// Default resource path used to load the <see cref="PersistentObjectCatalog"/> asset.
        /// </summary>
        public const string CatalogResourcePath = "PersistentObjects";

        [Tooltip("Catalog describing which prefabs must exist in every scene.")]
        [SerializeField]
        private PersistentObjectCatalog catalog;

        private static PersistentObjectBootstrap instance;

        /// <summary>
        /// Makes sure a bootstrapper exists even in empty scenes by creating one before the
        /// first scene loads.  Developers can still place the component manually when desired.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootstrapper()
        {
            if (instance != null)
                return;

            var existing = FindObjectOfType<PersistentObjectBootstrap>();
            if (existing != null)
            {
                instance = existing;
                return;
            }

            var go = new GameObject(nameof(PersistentObjectBootstrap));
            instance = go.AddComponent<PersistentObjectBootstrap>();
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

            if (catalog == null)
                catalog = Resources.Load<PersistentObjectCatalog>(CatalogResourcePath);

            if (catalog == null)
            {
                Debug.LogWarning($"PersistentObjectBootstrap could not locate a catalog at Resources/{CatalogResourcePath}.asset, so no persistent prefabs were spawned.");
                return;
            }

            EnsurePersistentObjects();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>
        /// Re-runs the spawn routine whenever a new scene becomes active.  This keeps the
        /// project resilient if a persistent object is accidentally removed at runtime.
        /// </summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsurePersistentObjects();
        }

        /// <summary>
        /// Instantiates any missing prefabs from the catalog and verifies they include
        /// <see cref="ScenePersistentObject"/> so they register with
        /// <see cref="SceneTransitionManager"/> automatically.
        /// </summary>
        private void EnsurePersistentObjects()
        {
            // Resolve the currently active scene so the catalog can opt-out of spawning
            // persistent prefabs for non-gameplay contexts such as login or menus.
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
                return;

            if (!catalog.ShouldSpawnInScene(activeScene.name))
                return;

            IReadOnlyList<GameObject> prefabs = catalog.Prefabs;
            for (int i = 0; i < prefabs.Count; i++)
            {
                var prefab = prefabs[i];
                if (prefab == null)
                    continue;

                string rootName = prefab.name;
                if (RootExists(rootName))
                    continue;

                var instance = Instantiate(prefab);
                instance.name = rootName;
                // Prevent required objects—such as the main camera—from being destroyed before the initial scene transition.
                DontDestroyOnLoad(instance);

                if (!TryEnsurePersistentComponent(instance))
                {
                    Debug.LogWarning($"Persistent prefab \"{rootName}\" was missing a ScenePersistentObject component. One was added automatically so it is tracked across scenes.", instance);
                }
            }
        }

        /// <summary>
        /// Checks for a root-level object with the provided name in the active hierarchy or the
        /// DontDestroyOnLoad scene.
        /// </summary>
        private static bool RootExists(string rootName)
        {
            var objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                var candidate = objects[i];
                if (!candidate.scene.IsValid())
                    continue;
                if (candidate.transform.parent != null)
                    continue;
                if (!string.Equals(candidate.name, rootName, StringComparison.Ordinal))
                    continue;
                if ((candidate.hideFlags & HideFlags.HideAndDontSave) != 0)
                    continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Ensures the instantiated prefab includes a <see cref="ScenePersistentObject"/>
        /// so it is registered with the transition system.
        /// </summary>
        private static bool TryEnsurePersistentComponent(GameObject instance)
        {
            var persistent = instance.GetComponent<ScenePersistentObject>();
            if (persistent != null)
                return true;

            persistent = instance.GetComponentInChildren<ScenePersistentObject>();
            if (persistent != null)
                return true;

            var behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IScenePersistent)
                    return true;
            }

            instance.AddComponent<ScenePersistentObject>();
            return false;
        }
    }

    /// <summary>
    /// Scriptable object containing the prefabs required in every scene.  Stored under
    /// <see cref="Resources"/> so it can be loaded automatically by the bootstrapper.
    /// </summary>
    [CreateAssetMenu(menuName = "World/Persistent Object Catalog", fileName = "PersistentObjects")]
    public sealed class PersistentObjectCatalog : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Prefabs that should be spawned automatically if they are missing from the scene.")]
        private List<GameObject> prefabs = new();

        [SerializeField]
        [Tooltip("Scene names (case-insensitive) where the persistent prefabs should NOT spawn.")]
        private List<string> excludedSceneNames = new();

        /// <summary>
        /// Read-only view of the prefabs used by the bootstrapper.
        /// </summary>
        public IReadOnlyList<GameObject> Prefabs => prefabs;

        /// <summary>
        /// Read-only view of the scene exclusion list for editor tooling or debugging.
        /// </summary>
        public IReadOnlyList<string> ExcludedSceneNames => excludedSceneNames;

        /// <summary>
        /// Checks whether the persistent catalog should spawn in the supplied scene name.
        /// </summary>
        /// <param name="sceneName">Scene currently being loaded or activated.</param>
        /// <returns><c>true</c> when prefabs should spawn; otherwise, <c>false</c>.</returns>
        public bool ShouldSpawnInScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return true;

            for (int i = 0; i < excludedSceneNames.Count; i++)
            {
                string excluded = excludedSceneNames[i];
                if (string.IsNullOrWhiteSpace(excluded))
                    continue;

                if (string.Equals(excluded, sceneName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Filter out scene instances that may have been dragged in by mistake to keep the
            // list focused on prefabs only.
            for (int i = prefabs.Count - 1; i >= 0; i--)
            {
                var entry = prefabs[i];
                if (entry == null)
                    continue;

                if (entry.scene.IsValid())
                {
                    Debug.LogWarning($"Removed scene object '{entry.name}' from {name}. Drag prefab assets into the list instead.", this);
                    prefabs.RemoveAt(i);
                }
            }

            // Normalise and deduplicate excluded scene entries so the runtime checks remain
            // deterministic and case-insensitive.
            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = excludedSceneNames.Count - 1; i >= 0; i--)
            {
                string rawName = excludedSceneNames[i];
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    // Keep blank inspector slots so designers can immediately enter a scene name.
                    excludedSceneNames[i] = string.Empty;
                    continue;
                }

                string trimmed = rawName.Trim();
                if (!uniqueNames.Add(trimmed))
                {
                    excludedSceneNames.RemoveAt(i);
                    continue;
                }

                excludedSceneNames[i] = trimmed;
            }
        }
#endif
    }
}
