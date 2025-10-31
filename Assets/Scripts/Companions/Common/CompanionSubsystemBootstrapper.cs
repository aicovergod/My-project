using System;
using UnityEngine;

namespace Companions.Common
{
    /// <summary>
    /// Provides shared bootstrapping helpers so the companion controller can lazily resolve
    /// subsystem components without duplicating null checks and AddComponent calls.
    /// </summary>
    public static class CompanionSubsystemBootstrapper
    {
        /// <summary>
        /// Ensures the requested subsystem exists on the supplied host, using the cached
        /// reference when available, resolving an existing component, or adding a new one
        /// when necessary. The returned instance is also pushed back through the cache.
        /// </summary>
        /// <typeparam name="T">Component type to ensure.</typeparam>
        /// <param name="host">Component that owns the subsystem.</param>
        /// <param name="cachedReference">Cached reference stored on the controller.</param>
        /// <param name="initialiser">Optional callback invoked once the subsystem exists.</param>
        /// <returns>The resolved or newly created component instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the host is null.</exception>
        public static T EnsureSubsystem<T>(Component host, ref T cachedReference, Action<T> initialiser = null)
            where T : Component
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            if (cachedReference == null)
            {
                cachedReference = host.GetComponent<T>();
                if (cachedReference == null)
                    cachedReference = host.gameObject.AddComponent<T>();
            }

            initialiser?.Invoke(cachedReference);
            return cachedReference;
        }

        /// <summary>
        /// Ensures the requested subsystem exists on the supplied host without requiring a
        /// cached reference field. Useful for subsystems that do not need to be stored yet.
        /// </summary>
        /// <typeparam name="T">Component type to ensure.</typeparam>
        /// <param name="host">Component that owns the subsystem.</param>
        /// <param name="initialiser">Optional callback invoked once the subsystem exists.</param>
        /// <returns>The resolved or newly created component instance.</returns>
        public static T EnsureSubsystem<T>(Component host, Action<T> initialiser)
            where T : Component
        {
            T cached = null;
            return EnsureSubsystem(host, ref cached, initialiser);
        }
    }
}
