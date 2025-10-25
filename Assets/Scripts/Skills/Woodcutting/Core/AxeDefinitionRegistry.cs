using System;
using System.Collections.Generic;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Maintains a runtime cache of <see cref="AxeDefinition"/> assets so non-player systems
    /// (companions, AI controllers, etc.) can resolve tool data without relying on a specific
    /// <see cref="AxeToUse"/> instance being present in the scene.
    /// </summary>
    public static class AxeDefinitionRegistry
    {
        private static readonly Dictionary<string, AxeDefinition> definitionsById =
            new Dictionary<string, AxeDefinition>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<AxeDefinition> sortedDefinitions = new List<AxeDefinition>();
        private static readonly object syncRoot = new object();

        private static AxeDefinition[] cachedSnapshot = Array.Empty<AxeDefinition>();

        /// <summary>
        /// Registers the supplied definitions with the cache, ignoring null entries and duplicate identifiers.
        /// </summary>
        /// <param name="definitions">Definitions that should be made available to the cache.</param>
        public static void RegisterDefinitions(IEnumerable<AxeDefinition> definitions)
        {
            if (definitions == null)
                return;

            bool changed = false;

            lock (syncRoot)
            {
                foreach (var definition in definitions)
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
        /// Retrieves all registered axe definitions sorted by power (descending) and identifier.
        /// </summary>
        public static IReadOnlyList<AxeDefinition> GetAllDefinitions()
        {
            lock (syncRoot)
            {
                if (cachedSnapshot.Length == 0)
                    return Array.Empty<AxeDefinition>();

                return cachedSnapshot;
            }
        }

        private static int CompareDefinitions(AxeDefinition left, AxeDefinition right)
        {
            if (ReferenceEquals(left, right))
                return 0;

            if (left == null)
                return 1;
            if (right == null)
                return -1;

            int powerComparison = right.Power.CompareTo(left.Power);
            if (powerComparison != 0)
                return powerComparison;

            return string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }
    }
}
