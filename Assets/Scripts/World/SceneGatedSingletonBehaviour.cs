using System;
using UnityEngine;

namespace World
{
    /// <summary>
    /// Provides a thin wrapper around <see cref="PersistentSceneSingleton{T}"/> so derived
    /// behaviours can opt-in to the shared scene-gated singleton lifecycle without duplicating
    /// boilerplate. The helper owns the singleton creation/teardown logic and exposes overridable
    /// hooks that mirror <see cref="MonoBehaviour.Awake"/> and <see cref="MonoBehaviour.OnDestroy"/>.
    /// </summary>
    /// <typeparam name="T">Concrete singleton type.</typeparam>
    public abstract class SceneGatedSingletonBehaviour<T> : MonoBehaviour
        where T : SceneGatedSingletonBehaviour<T>
    {
        /// <summary>
        /// Exposes the active instance managed by <see cref="PersistentSceneSingleton{T}"/>.
        /// </summary>
        public static T Instance => PersistentSceneSingleton<T>.Instance;

        /// <summary>
        /// Entry point used by bootstrap helpers. Derived classes call this from their
        /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/> so the singleton is created when the
        /// active scene is allowed by <see cref="PersistentSceneGate"/>.
        /// </summary>
        /// <param name="factory">Optional factory responsible for spawning the singleton.</param>
        protected static void BootstrapSingleton(Func<T> factory = null)
        {
            PersistentSceneSingleton<T>.Bootstrap(factory);
        }

        /// <summary>
        /// Invoked when <see cref="PersistentSceneSingleton{T}"/> accepts this behaviour as the
        /// canonical singleton instance.
        /// </summary>
        protected virtual void OnSingletonAwake()
        {
        }

        /// <summary>
        /// Invoked when the canonical singleton instance is being destroyed (usually because the
        /// active scene no longer allows the singleton or the application is shutting down).
        /// </summary>
        protected virtual void OnSingletonDestroyed()
        {
        }

        /// <summary>
        /// Internal Awake handler that defers to <see cref="PersistentSceneSingleton{T}"/> and only
        /// forwards execution when this component is accepted as the singleton.
        /// </summary>
        protected void Awake()
        {
            if (!PersistentSceneSingleton<T>.HandleAwake((T)this))
                return;

            OnSingletonAwake();
        }

        /// <summary>
        /// Internal OnDestroy handler that mirrors <see cref="Awake"/>. The override hook only runs
        /// for the canonical singleton so duplicate objects silently exit.
        /// </summary>
        protected void OnDestroy()
        {
            if (!PersistentSceneSingleton<T>.HandleOnDestroy((T)this))
                return;

            OnSingletonDestroyed();
        }
    }
}

