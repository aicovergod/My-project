using System.Collections.Generic;
using UnityEngine;

namespace Skills.Thieving.Data
{
    /// <summary>
    ///     Centralised database for thieving definitions. Allows runtime systems to resolve NPC/object configurations
    ///     by ID without repeatedly scanning Resources.
    /// </summary>
    [CreateAssetMenu(menuName = "Skills/Thieving/Definition Database", fileName = "ThievingDefinitionDatabase")]
    public class ThievingDefinitionDatabase : ScriptableObject
    {
        [SerializeField, Tooltip("List of NPC pickpocket definitions available in this database.")]
        private List<ThievingNpcDefinition> npcDefinitions = new List<ThievingNpcDefinition>();

        [SerializeField, Tooltip("List of thievable object definitions (stalls, chests, etc.).")]
        private List<ThievingObjectDefinition> objectDefinitions = new List<ThievingObjectDefinition>();

        /// <summary>
        ///     Retrieves the NPC definition with the supplied identifier.
        /// </summary>
        /// <param name="id">Identifier of the NPC definition.</param>
        /// <returns>The resolved definition or <c>null</c> when not found.</returns>
        public ThievingNpcDefinition GetNpcById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            string trimmed = id.Trim();
            for (int i = 0; i < npcDefinitions.Count; i++)
            {
                var definition = npcDefinitions[i];
                if (definition != null && string.Equals(definition.Id, trimmed, System.StringComparison.Ordinal))
                    return definition;
            }

            return null;
        }

        /// <summary>
        ///     Retrieves the thieving object definition with the supplied identifier.
        /// </summary>
        /// <param name="id">Identifier of the object definition.</param>
        /// <returns>The resolved definition or <c>null</c> when not present.</returns>
        public ThievingObjectDefinition GetObjectById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            string trimmed = id.Trim();
            for (int i = 0; i < objectDefinitions.Count; i++)
            {
                var definition = objectDefinitions[i];
                if (definition != null && string.Equals(definition.Id, trimmed, System.StringComparison.Ordinal))
                    return definition;
            }

            return null;
        }

        /// <summary>
        ///     Provides read-only access to the registered NPC definitions.
        /// </summary>
        public IReadOnlyList<ThievingNpcDefinition> NpcDefinitions => npcDefinitions;

        /// <summary>
        ///     Provides read-only access to the registered object definitions.
        /// </summary>
        public IReadOnlyList<ThievingObjectDefinition> ObjectDefinitions => objectDefinitions;
    }
}
