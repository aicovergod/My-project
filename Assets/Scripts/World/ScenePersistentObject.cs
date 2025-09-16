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

            // Track whether a lower instance ID already exists.  Unity assigns
            // monotonically increasing instance IDs, so the very first copy of a
            // prefab receives the smallest ID value.  By preserving the lowest ID
            // we guarantee the original object persists and any later scene load
            // clones are culled immediately.
            bool hasLowerInstance = false;
            int thisId = GetInstanceID();

            foreach (var obj in others)
            {
                // Ignore unrelated persistent objects.
                if (obj == this || obj.gameObject.name != gameObject.name)
                {
                    continue;
                }

                int otherId = obj.GetInstanceID();

                if (otherId < thisId)
                {
                    // A lower ID means a pre-existing instance is already
                    // persistent.  Mark this instance for destruction.
                    hasLowerInstance = true;
                    break;
                }
            }

            if (hasLowerInstance)
            {
                // A prior persistent instance exists – destroy the newcomer so
                // the original remains the sole survivor across scenes.
                Destroy(gameObject);
                return;
            }

            // This is the first instance, so remove any higher-ID duplicates to
            // enforce singleton behaviour before they can execute additional
            // lifecycle logic.
            foreach (var obj in others)
            {
                if (obj == this || obj.gameObject.name != gameObject.name)
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

        public void OnBeforeSceneUnload()
        {
            DontDestroyOnLoad(gameObject);
        }

        public void OnAfterSceneLoad(Scene scene)
        {
            SceneManager.MoveGameObjectToScene(gameObject, scene);
        }
    }
}
