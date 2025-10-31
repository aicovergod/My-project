using System;
using System.Collections.Generic;
using Companions.Equipment;
using Inventory;
using Skills.Common;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions
{
    /// <summary>
    /// Provides shared resolution logic for companion gathering tools so each controller can
    /// evaluate the best available tool without duplicating iteration, selector registration,
    /// or ownership checks.
    /// </summary>
    public static class CompanionToolResolver
    {
        /// <summary>
        /// Attempts to resolve the best tool definition that satisfies the supplied filters
        /// and is currently owned or equipped by the companion.
        /// </summary>
        /// <typeparam name="TDefinition">Type of tool definition that should be selected.</typeparam>
        /// <param name="definitionProvider">Callback that returns the cached definition list.</param>
        /// <param name="registerSelectorsWhenEmpty">
        /// Optional callback that will register selector-backed definitions when the registry
        /// has not been populated yet.
        /// </param>
        /// <param name="inventory">Inventory used to check tool ownership.</param>
        /// <param name="equipment">Equipment component used to check the currently equipped tool.</param>
        /// <param name="itemCache">
        /// Skill-specific cache dictionary that will be rebound to the shared gathering cache
        /// on demand so repeated ownership checks remain allocation free.
        /// </param>
        /// <param name="toolIdAccessor">Accessor that extracts the item identifier from a definition.</param>
        /// <param name="skillRequirement">
        /// Optional filter that validates whether the companion meets the skill requirement
        /// for the definition.
        /// </param>
        /// <param name="nodeRequirement">
        /// Optional filter that validates whether the current gathering node allows the definition.
        /// </param>
        /// <param name="additionalFilters">
        /// Optional additional filters (tier gating, quest locks, etc.) that must all pass for the
        /// definition to be considered.
        /// </param>
        /// <returns>The first definition that satisfies every filter and is owned by the companion.</returns>
        public static TDefinition ResolveBestTool<TDefinition>(
            Func<IReadOnlyList<TDefinition>> definitionProvider,
            Action registerSelectorsWhenEmpty,
            RuntimeInventory inventory,
            CompanionEquipment equipment,
            ref Dictionary<string, ItemData> itemCache,
            Func<TDefinition, string> toolIdAccessor,
            Predicate<TDefinition> skillRequirement = null,
            Predicate<TDefinition> nodeRequirement = null,
            params Predicate<TDefinition>[] additionalFilters)
            where TDefinition : class
        {
            if (definitionProvider == null || toolIdAccessor == null)
                return null;

            var definitions = definitionProvider();
            if (definitions == null || definitions.Count == 0)
            {
                registerSelectorsWhenEmpty?.Invoke();
                definitions = definitionProvider();
            }

            if (definitions == null || definitions.Count == 0)
                return null;

            for (int i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null)
                    continue;

                if (skillRequirement != null && !skillRequirement(definition))
                    continue;

                if (nodeRequirement != null && !nodeRequirement(definition))
                    continue;

                bool failedAdditionalFilter = false;
                if (additionalFilters != null)
                {
                    for (int filterIndex = 0; filterIndex < additionalFilters.Length; filterIndex++)
                    {
                        var filter = additionalFilters[filterIndex];
                        if (filter == null)
                            continue;

                        if (!filter(definition))
                        {
                            failedAdditionalFilter = true;
                            break;
                        }
                    }
                }

                if (failedAdditionalFilter)
                    continue;

                string toolId = toolIdAccessor(definition);
                if (string.IsNullOrWhiteSpace(toolId))
                    continue;

                if (!CompanionToolOwnershipUtility.HasTool(toolId, inventory, equipment, ref itemCache, out _))
                    continue;

                return definition;
            }

            return null;
        }
    }
}
