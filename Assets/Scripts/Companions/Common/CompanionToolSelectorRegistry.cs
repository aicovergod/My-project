using System;
using System.Collections.Generic;
using System.Reflection;
using Skills.Fishing;
using Skills.Mining;
using Skills.Woodcutting;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Provides shared registration helpers that allow companion controllers to cache tool
    /// definitions from <see cref="GatheringToolSelectorBase{TDefinition, TSkill}"/> derivatives.
    /// The selectors store their definitions in private serialized lists to keep the inspector clean,
    /// so runtime systems rely on reflection to access the data when no selector instance has run yet.
    /// </summary>
    public static class CompanionToolSelectorRegistry
    {
        private static readonly Dictionary<(Type selectorType, string fieldName), FieldInfo> SelectorFieldCache =
            new Dictionary<(Type selectorType, string fieldName), FieldInfo>();

        private static readonly Dictionary<Type, object> DefinitionBuffers = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, object> DefinitionSets = new Dictionary<Type, object>();

        /// <summary>
        /// Ensures every <see cref="FishingToolDefinition"/> referenced by a <see cref="FishingToolToUse"/>
        /// component in the scene is registered with <see cref="FishingToolDefinitionRegistry"/>.
        /// </summary>
        public static void RegisterFishingToolsFromSelectors()
        {
            RegisterDefinitionsFromSelectors<FishingToolToUse, FishingToolDefinition>(
                "allTools",
                FishingToolDefinitionRegistry.RegisterDefinitions);
        }

        /// <summary>
        /// Ensures every <see cref="AxeDefinition"/> referenced by an <see cref="AxeToUse"/> component in the
        /// scene is registered with <see cref="AxeDefinitionRegistry"/>.
        /// </summary>
        public static void RegisterAxesFromSelectors()
        {
            RegisterDefinitionsFromSelectors<AxeToUse, AxeDefinition>(
                "allAxes",
                AxeDefinitionRegistry.RegisterDefinitions);
        }

        /// <summary>
        /// Ensures every <see cref="PickaxeDefinition"/> referenced by a <see cref="PickaxeToUse"/> component in the
        /// scene is registered with <see cref="PickaxeDefinitionRegistry"/>.
        /// </summary>
        public static void RegisterPickaxesFromSelectors()
        {
            RegisterDefinitionsFromSelectors<PickaxeToUse, PickaxeDefinition>(
                "allPickaxes",
                PickaxeDefinitionRegistry.RegisterDefinitions,
                selector => selector != null ? selector.AllPickaxes : null);
        }

        /// <summary>
        /// Scans every selector of the requested type, reads the serialized tool list via reflection,
        /// and forwards the discovered definitions to the supplied registration callback.
        /// </summary>
        /// <typeparam name="TSelector">Selector component that serializes the tool definitions.</typeparam>
        /// <typeparam name="TDefinition">Definition asset type stored by the selector.</typeparam>
        /// <param name="fieldName">Private field containing the serialized definition list.</param>
        /// <param name="registerAction">Callback invoked with each populated tool list.</param>
        /// <param name="fallbackExtractor">
        /// Optional extractor used when the private field is missing (future refactors can expose
        /// a public property without breaking this helper).
        /// </param>
        private static void RegisterDefinitionsFromSelectors<TSelector, TDefinition>(
            string fieldName,
            Action<IEnumerable<TDefinition>> registerAction,
            Func<TSelector, IEnumerable<TDefinition>> fallbackExtractor = null)
            where TSelector : Component
            where TDefinition : class
        {
            if (registerAction == null)
                throw new ArgumentNullException(nameof(registerAction));

            var selectors = UnityEngine.Object.FindObjectsOfType<TSelector>(true);
            if (selectors == null || selectors.Length == 0)
                return;

            for (int i = 0; i < selectors.Length; i++)
            {
                var selector = selectors[i];
                if (selector == null)
                    continue;

                var definitions = ExtractDefinitions(selector, fieldName, fallbackExtractor);
                if (definitions.Count > 0)
                    registerAction(definitions);
            }
        }

        /// <summary>
        /// Reads the serialized tool list from the selector and caches the result in a reusable buffer
        /// so repeated scans avoid unnecessary allocations.
        /// </summary>
        private static IReadOnlyList<TDefinition> ExtractDefinitions<TSelector, TDefinition>(
            TSelector selector,
            string fieldName,
            Func<TSelector, IEnumerable<TDefinition>> fallbackExtractor)
            where TSelector : Component
            where TDefinition : class
        {
            var buffer = GetDefinitionBuffer<TDefinition>();
            var uniqueSet = GetDefinitionSet<TDefinition>();

            buffer.Clear();
            uniqueSet.Clear();

            IEnumerable<TDefinition> source = null;

            var field = GetSelectorField<TSelector>(fieldName);
            if (field != null && selector != null)
                source = field.GetValue(selector) as IEnumerable<TDefinition>;

            if (source == null && fallbackExtractor != null)
                source = fallbackExtractor(selector);

            if (source == null)
                return buffer;

            foreach (var definition in source)
            {
                if (definition == null)
                    continue;

                if (uniqueSet.Add(definition))
                    buffer.Add(definition);
            }

            return buffer;
        }

        /// <summary>
        /// Retrieves (or creates) the cached <see cref="FieldInfo"/> that exposes the private serialized
        /// list on the selector type.
        /// </summary>
        private static FieldInfo GetSelectorField<TSelector>(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return null;

            var key = (typeof(TSelector), fieldName);
            if (!SelectorFieldCache.TryGetValue(key, out var fieldInfo))
            {
                fieldInfo = typeof(TSelector).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                SelectorFieldCache[key] = fieldInfo;
            }

            return fieldInfo;
        }

        /// <summary>
        /// Provides a reusable list for the supplied definition type so scans avoid temporary allocations.
        /// </summary>
        private static List<TDefinition> GetDefinitionBuffer<TDefinition>() where TDefinition : class
        {
            if (!DefinitionBuffers.TryGetValue(typeof(TDefinition), out var bufferObject))
            {
                bufferObject = new List<TDefinition>();
                DefinitionBuffers[typeof(TDefinition)] = bufferObject;
            }

            return (List<TDefinition>)bufferObject;
        }

        /// <summary>
        /// Provides a reusable hash set for duplicate filtering while populating the definition buffer.
        /// </summary>
        private static HashSet<TDefinition> GetDefinitionSet<TDefinition>() where TDefinition : class
        {
            if (!DefinitionSets.TryGetValue(typeof(TDefinition), out var setObject))
            {
                setObject = new HashSet<TDefinition>();
                DefinitionSets[typeof(TDefinition)] = setObject;
            }

            return (HashSet<TDefinition>)setObject;
        }
    }
}
