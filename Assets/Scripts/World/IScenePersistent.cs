using UnityEngine.SceneManagement;

namespace World
{
    /// <summary>
    /// Interface for objects that should persist across scene transitions.
    /// </summary>
    public interface IScenePersistent
    {
        /// <summary>
        /// Called before the current scene unloads.
        /// </summary>
        void OnBeforeSceneUnload();

        /// <summary>
        /// Called after a new scene is loaded.
        /// </summary>
        /// <param name="scene">The scene that was loaded.</param>
        void OnAfterSceneLoad(Scene scene);
    }
}
