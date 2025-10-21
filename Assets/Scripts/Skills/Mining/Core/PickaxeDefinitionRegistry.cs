using System;
using System.Collections.Generic;

namespace Skills.Mining
{
    /// <summary>
    /// Maintains a runtime cache of <see cref="PickaxeDefinition"/> assets so systems outside of the
    /// player controller (companions, AI, etc.) can query tool data without relying on a specific
    /// <see cref="PickaxeToUse"/> component being present in the scene.
    /// </summary>
    public static class PickaxeDefinitionRegistry
    {
        private static readonly Dictionary<string, PickaxeDefinition> definitionsById =
            new Dictionary<string, PickaxeDefinition>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<PickaxeDefinition> sortedDefinitions = new List<PickaxeDefinition>();
        private static readonly object syncRoot = new object();

        private static PickaxeDefinition[] cachedSnapshot = Array.Empty<PickaxeDefinition>();

        /// <summary>
        /// Registers the supplied definitions with the cache, ignoring null entries and duplicate identifiers.
        /// </summary>
        /// <param name="defs">Definitions that should be made available to the cache.</param>
        public static void RegisterDefinitions(IEnumerable<PickaxeDefinition> defs)
        {
            if (defs == null)
                return;

            bool changed = false;

            lock (syncRoot)
            {
                foreach (var definition in defs)
                {
                    if (definition == null)
                        continue;

                    string id = definition.Id;
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    if (definitionsById.ContainsKey(id))
                        continue;

                    definitionsById[id] = definition;
                    sortedDefinitions.Add(definition);
                    changed = true;
                }

                if (changed)
                {
                    sortedDefinitions.Sort(CompareDefinitions);
                    cachedSnapshot = sortedDefinitions.ToArray();
                }
            }
        }

        /// <summary>
        /// Retrieves all registered pickaxe definitions in descending tier order (then by identifier).
        /// </summary>
        /// <returns>A read-only view of the registered definitions.</returns>
        public static IReadOnlyList<PickaxeDefinition> GetAllDefinitions()
        {
            lock (syncRoot)
            {
                if (cachedSnapshot.Length == 0)
                    return Array.Empty<PickaxeDefinition>();

                return cachedSnapshot;
            }
        }

        /// <summary>
        /// Attempts to retrieve a registered definition by identifier.
        /// </summary>
        /// <param name="id">Identifier of the definition to locate.</param>
        /// <param name="definition">Outputs the located definition when available.</param>
        /// <returns>True when a matching definition is registered, otherwise false.</returns>
        public static bool TryGetDefinition(string id, out PickaxeDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            lock (syncRoot)
            {
                return definitionsById.TryGetValue(id, out definition);
            }
        }

        private static int CompareDefinitions(PickaxeDefinition a, PickaxeDefinition b)
        {
            if (ReferenceEquals(a, b))
                return 0;

            if (a == null)
                return 1;
            if (b == null)
                return -1;

            int tierComparison = b.Tier.CompareTo(a.Tier);
            if (tierComparison != 0)
                return tierComparison;

            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        }
    }
}
