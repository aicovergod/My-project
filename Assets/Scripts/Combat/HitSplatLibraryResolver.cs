using UnityEngine;

namespace Combat
{
    /// <summary>
    /// Provides a centralised entry point for resolving the shared <see cref="HitSplatLibrary"/>.
    /// Ensures the expensive <see cref="Resources.Load{T}(string)"/> lookup runs at most once
    /// and caches the result so every combatant can reuse the same library reference.
    /// </summary>
    public static class HitSplatLibraryResolver
    {
        /// <summary>
        /// Cached instance loaded from the Resources folder. Stored statically so subsequent
        /// requests avoid repeated <c>Resources.Load</c> calls and unnecessary allocations.
        /// </summary>
        private static HitSplatLibrary cachedLibrary;

        /// <summary>
        /// Returns the supplied library when available, otherwise lazily loads the shared
        /// <see cref="HitSplatLibrary"/> from <c>Resources/HitSplatLibrary</c> and caches it
        /// for future callers.
        /// </summary>
        /// <param name="existing">
        /// Optional library reference already assigned via the inspector. When provided it is
        /// returned unchanged so components can override the global asset per-instance if needed.
        /// </param>
        public static HitSplatLibrary Resolve(HitSplatLibrary existing)
        {
            if (existing != null)
                return existing;

            if (cachedLibrary == null)
                cachedLibrary = Resources.Load<HitSplatLibrary>("HitSplatLibrary");

            return cachedLibrary;
        }
    }
}
