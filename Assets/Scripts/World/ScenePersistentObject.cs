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
            var others = FindObjectsOfType<ScenePersistentObject>();
            foreach (var obj in others)
            {
                if (obj != this && obj.gameObject.name == gameObject.name)
                {
                    Destroy(gameObject);
                    return;
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
