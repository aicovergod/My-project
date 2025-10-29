using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skills.Thieving.NpcPickpocketDialogue
{
    /// <summary>
    ///     Base definition for NPC-specific pickpocket dialogue. Derived classes provide the
    ///     actual line collections and register themselves so the <see cref="NpcPickpocketDialogueService"/>
    ///     can locate the correct payload using the NPC's unique identifier from
    ///     <see cref="Skills.Thieving.Data.ThievingNpcDefinition"/>.
    /// </summary>
    internal abstract class NpcPickpocketDialogueSet
    {
        private static readonly Dictionary<string, NpcPickpocketDialogueSet> Registry =
            new Dictionary<string, NpcPickpocketDialogueSet>(StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyList<string> EmptyLines = Array.Empty<string>();

        /// <summary>
        ///     Unique identifier that must match <see cref="Skills.Thieving.Data.ThievingNpcDefinition.Id"/>.
        /// </summary>
        public abstract string NpcId { get; }

        /// <summary>
        ///     Collection of flavour lines surfaced on successful pickpockets when the
        ///     1-in-20 roll passes. Defaults to an empty list so derived classes only need
        ///     to populate the sets they actually use.
        /// </summary>
        protected virtual IReadOnlyList<string> SuccessDialogueLines => EmptyLines;

        /// <summary>
        ///     Collection of flavour lines surfaced when the pickpocket fails.
        /// </summary>
        protected virtual IReadOnlyList<string> FailureDialogueLines => EmptyLines;

        /// <summary>
        ///     Registers a dialogue set so it can be discovered by NPC identifier at runtime.
        ///     Derived classes should call this during startup (for example via
        ///     <see cref="RuntimeInitializeOnLoadMethodAttribute"/>) to make their lines
        ///     available to the service.
        /// </summary>
        /// <param name="set">Dialogue set being registered.</param>
        protected static void RegisterSet(NpcPickpocketDialogueSet set)
        {
            if (set == null)
            {
                Debug.LogWarning("[Thieving] Attempted to register a null pickpocket dialogue set.");
                return;
            }

            if (string.IsNullOrWhiteSpace(set.NpcId))
            {
                Debug.LogWarning("[Thieving] Pickpocket dialogue set missing NPC id; registration skipped.");
                return;
            }

            Registry[set.NpcId] = set;
        }

        /// <summary>
        ///     Attempts to resolve the dialogue set for the supplied NPC identifier.
        /// </summary>
        internal static bool TryGet(string npcId, out NpcPickpocketDialogueSet set)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                set = null;
                return false;
            }

            return Registry.TryGetValue(npcId, out set);
        }

        /// <summary>
        ///     Tries to retrieve a random success dialogue line.
        /// </summary>
        /// <param name="line">Output parameter containing the selected line.</param>
        /// <returns>True when a non-empty line was resolved.</returns>
        internal bool TryGetRandomSuccessLine(out string line)
        {
            return TryGetRandomLine(SuccessDialogueLines, out line);
        }

        /// <summary>
        ///     Tries to retrieve a random failure dialogue line.
        /// </summary>
        /// <param name="line">Output parameter containing the selected line.</param>
        /// <returns>True when a non-empty line was resolved.</returns>
        internal bool TryGetRandomFailureLine(out string line)
        {
            return TryGetRandomLine(FailureDialogueLines, out line);
        }

        private static bool TryGetRandomLine(IReadOnlyList<string> source, out string line)
        {
            if (source == null || source.Count == 0)
            {
                line = string.Empty;
                return false;
            }

            int index = UnityEngine.Random.Range(0, source.Count);
            line = source[index];
            return !string.IsNullOrWhiteSpace(line);
        }
    }
}
