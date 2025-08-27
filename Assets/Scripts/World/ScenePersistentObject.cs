using UnityEngine;
using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// Simple component that makes a GameObject persist across scene transitions.
    /// </summary>
    public class ScenePersistentObject : MonoBehaviour, IScenePersistent
    {
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
