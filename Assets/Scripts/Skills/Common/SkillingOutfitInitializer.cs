using UnityEngine;
using Skills.Outfits;

namespace Skills.Common
{
    /// <summary>
    ///     Provides helper methods for initialising <see cref="SkillingOutfitProgress"/> instances
    ///     across gathering skills so each component can share the same loading and warning logic.
    /// </summary>
    public static class SkillingOutfitInitializer
    {
        /// <summary>
        ///     Ensures a <see cref="SkillingOutfitDefinition"/> is available, loading it from
        ///     <see cref="Resources"/> when required, and constructs the associated
        ///     <see cref="SkillingOutfitProgress"/>. If the definition cannot be resolved the
        ///     standardised warning message is emitted so outfit rolls can be disabled gracefully.
        /// </summary>
        /// <param name="definition">
        ///     Reference to the current outfit definition field on the calling skill. The
        ///     referenced value will be replaced when the asset is loaded from the supplied
        ///     <paramref name="resourcePath"/>.
        /// </param>
        /// <param name="resourcePath">
        ///     <see cref="Resources"/> path used to lazily load the definition when it has not been
        ///     explicitly assigned in the inspector.
        /// </param>
        /// <param name="skillIdentifier">
        ///     Identifier used in warning logs so developers can immediately trace which skill is
        ///     missing configuration. Typically the calling component's type name (e.g.
        ///     <c>nameof(MiningSkill)</c>).
        /// </param>
        /// <param name="owner">
        ///     Component requesting the initialisation. Passed as the log context when warnings are
        ///     emitted to make inspector navigation easier inside Unity.
        /// </param>
        /// <returns>
        ///     Newly constructed <see cref="SkillingOutfitProgress"/> when the definition is
        ///     resolved successfully; otherwise <c>null</c> when the definition is missing.
        /// </returns>
        public static SkillingOutfitProgress InitializeOutfitProgress(
            ref SkillingOutfitDefinition definition,
            string resourcePath,
            string skillIdentifier,
            MonoBehaviour owner)
        {
            if (definition == null && !string.IsNullOrWhiteSpace(resourcePath))
                definition = Resources.Load<SkillingOutfitDefinition>(resourcePath);

            if (definition != null)
                return new SkillingOutfitProgress(definition);

            string contextName = !string.IsNullOrWhiteSpace(skillIdentifier)
                ? skillIdentifier
                : owner != null
                    ? owner.GetType().Name
                    : nameof(SkillingOutfitProgress);

            Debug.LogWarning(
                $"{contextName} is missing a SkillingOutfitDefinition reference; outfit rewards are disabled.",
                owner);

            return null;
        }
    }
}

