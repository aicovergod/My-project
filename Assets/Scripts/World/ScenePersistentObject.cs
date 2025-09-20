using UnityEngine;
using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// Simple component that makes a GameObject persist across scene transitions.
    /// </summary>
    public class ScenePersistentObject : MonoBehaviour, IScenePersistent
    {
        // Track duplicates early so only a single instance of a
        // persistent object survives scene loads.  Without this check, a
        // copy of the object placed in a newly loaded scene would remain
        // alongside the existing instance that was carried over via
        // DontDestroyOnLoad, leading to duplicated managers and HUD
        // elements.
        private const string DontDestroyOnLoadSceneName = "DontDestroyOnLoad";

        // Tracks whether DontDestroyOnLoad has been applied previously so the object
        // is treated as persistent even after being moved into a new scene.
        private bool hasBeenMarkedPersistent;

        /// <summary>
        /// Indicates whether this instance has already been promoted to a persistent object.
        /// </summary>
        private bool IsPersistent => hasBeenMarkedPersistent || gameObject.scene.name == DontDestroyOnLoadSceneName;

        /// <summary>
        /// Performs duplicate detection so only a single copy of the object survives
        /// scene loads.  Derived classes should call the base implementation when
        /// overriding <see cref="Awake"/>.
        /// </summary>
        protected virtual void Awake()
        {
            // Gather every persistent object so we can determine which instance
            // should be allowed to survive.  Each prefab / scene setup can only
            // have a single persistent representative, otherwise a camera, UI, or
            // manager duplicated across scenes would start stacking up and spam
            // warnings like "No cameras rendering" as duplicates get disabled.
            var others = FindObjectsOfType<ScenePersistentObject>(true);

            if (gameObject.scene.name == DontDestroyOnLoadSceneName)
            {
                hasBeenMarkedPersistent = true;
            }

            // Track whether this object already lives in the DontDestroyOnLoad
            // scene or has previously been marked persistent.  When a persistent
            // copy exists we always favour it over a newly spawned scene local
            // duplicate to avoid despawning managers during scene swaps.
            bool thisIsPersistent = IsPersistent;
            bool hasLowerInstance = false;
            int thisId = GetInstanceID();

            foreach (var obj in others)
            {
                // Ignore unrelated persistent objects.
                if (obj == this)
                {
                    continue;
                }

                if (obj == null)
                {
                    continue;
                }

                // Allow derived components that live on the same GameObject to
                // coexist without being flagged as duplicates.  Prefabs often
                // include a base ScenePersistentObject component alongside a
                // specialised subclass and they should not cull each other.
                if (obj.gameObject == gameObject)
                {
                    continue;
                }

                if (obj.gameObject.name != gameObject.name)
                {
                    continue;
                }

                // If another instance already lives in the DontDestroyOnLoad
                // scene (or has previously been promoted via DontDestroyOnLoad)
                // and this copy is still scene-local, destroy the local copy.
                bool otherIsPersistent = obj.IsPersistent;

                if (!thisIsPersistent && otherIsPersistent)
                {
                    Destroy(gameObject);
                    return;
                }

                if (thisIsPersistent && !otherIsPersistent)
                {
                    Destroy(obj.gameObject);
                    continue;
                }

                int otherId = obj.GetInstanceID();

                if (otherId < thisId)
                {
                    // Neither copy has been promoted yet, so fall back to instance
                    // ID ordering.  The earliest created object (lowest ID) wins and
                    // all later scene spawns are culled.
                    hasLowerInstance = true;
                    break;
                }
            }

            if (hasLowerInstance)
            {
                // A prior instance with the same persistence state exists – destroy
                // the newcomer so the original remains the sole survivor across
                // scenes.
                Destroy(gameObject);
                return;
            }

            // This is the first instance, so remove any higher-ID duplicates to
            // enforce singleton behaviour before they can execute additional
            // lifecycle logic.
            foreach (var obj in others)
            {
                if (obj == this)
                {
                    continue;
                }

                if (obj.gameObject == gameObject)
                {
                    continue;
                }

                if (obj.gameObject.name != gameObject.name)
                {
                    continue;
                }

                if (obj == null)
                {
                    continue;
                }

                if (obj.IsPersistent != thisIsPersistent)
                {
                    continue;
                }

                if (obj.GetInstanceID() > thisId)
                {
                    Destroy(obj.gameObject);
                }
            }
        }

        void OnEnable()
        {
            SceneTransitionManager.RegisterPersistentObject(this);
        }

        void OnDisable()
        {
            SceneTransitionManager.UnregisterPersistentObject(this);
        }

        public virtual void OnBeforeSceneUnload()
        {
            hasBeenMarkedPersistent = true;
            DontDestroyOnLoad(gameObject);
        }

        public virtual void OnAfterSceneLoad(Scene scene)
        {
            SceneManager.MoveGameObjectToScene(gameObject, scene);
        }
    }
}
