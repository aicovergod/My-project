using System;
using System.Collections.Generic;

namespace Skills.Fishing
{
    /// <summary>
    /// Maintains a runtime cache of <see cref="FishingToolDefinition"/> assets so non-player systems
    /// (companions, AI controllers, etc.) can resolve tool data without relying on a specific
    /// <see cref="FishingToolToUse"/> instance being present in the scene.
    /// </summary>
    public static class FishingToolDefinitionRegistry
    {
        private static readonly Dictionary<string, FishingToolDefinition> definitionsById =
            new Dictionary<string, FishingToolDefinition>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<FishingToolDefinition> sortedDefinitions = new List<FishingToolDefinition>();
        private static readonly object syncRoot = new object();

        private static FishingToolDefinition[] cachedSnapshot = Array.Empty<FishingToolDefinition>();

        /// <summary>
        /// Registers the supplied definitions with the cache, ignoring null entries and duplicate identifiers.
        /// </summary>
        /// <param name="definitions">Definitions that should be made available to the cache.</param>
        public static void RegisterDefinitions(IEnumerable<FishingToolDefinition> definitions)
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
        /// Retrieves all registered fishing tool definitions sorted by catch bonus (descending) and identifier.
        /// </summary>
        public static IReadOnlyList<FishingToolDefinition> GetAllDefinitions()
        {
            lock (syncRoot)
            {
                if (cachedSnapshot.Length == 0)
                    return Array.Empty<FishingToolDefinition>();

                return cachedSnapshot;
            }
        }

        private static int CompareDefinitions(FishingToolDefinition left, FishingToolDefinition right)
        {
            if (ReferenceEquals(left, right))
                return 0;

            if (left == null)
                return 1;
            if (right == null)
                return -1;

            int catchComparison = right.CatchBonus.CompareTo(left.CatchBonus);
            if (catchComparison != 0)
                return catchComparison;

            return string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }
    }
}
