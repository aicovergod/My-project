using System;
using World;

namespace UI.Utilities
{
    /// <summary>
    /// Helper base that combines <see cref="ManagedUiWindow"/> with the scene-gated singleton
    /// lifecycle used throughout the project. Derived classes only need to implement the singleton
    /// hooks and call <see cref="BootstrapSingleton"/> from a runtime initialise step.
    /// </summary>
    /// <typeparam name="T">Concrete window type.</typeparam>
    public abstract class SceneGatedManagedUiWindow<T> : ManagedUiWindow
        where T : SceneGatedManagedUiWindow<T>
    {
        /// <summary>
        /// Exposes the active instance tracked by <see cref="PersistentSceneSingleton{T}"/>.
        /// </summary>
        public static T Instance => PersistentSceneSingleton<T>.Instance;

        /// <summary>
        /// Entry point used by runtime bootstrap hooks to spawn the singleton when the current scene
        /// permits it.
        /// </summary>
        protected static void BootstrapSingleton(Func<T> factory = null)
        {
            PersistentSceneSingleton<T>.Bootstrap(factory);
        }

        /// <summary>
        /// Invoked when <see cref="PersistentSceneSingleton{T}"/> accepts this component as the
        /// canonical instance.
        /// </summary>
        protected virtual void OnSingletonAwake()
        {
        }

        /// <summary>
        /// Invoked when the singleton instance is being destroyed.
        /// </summary>
        protected virtual void OnSingletonDestroyed()
        {
        }

        /// <summary>
        /// Internal Awake hook that mirrors <see cref="SceneGatedSingletonBehaviour{T}"/> while still
        /// allowing derived classes to extend it.
        /// </summary>
        protected virtual void Awake()
        {
            if (!PersistentSceneSingleton<T>.HandleAwake((T)this))
                return;

            OnSingletonAwake();
        }

        /// <summary>
        /// Internal OnDestroy hook that mirrors <see cref="SceneGatedSingletonBehaviour{T}"/>.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (!PersistentSceneSingleton<T>.HandleOnDestroy((T)this))
                return;

            OnSingletonDestroyed();
        }
    }
}
