namespace NPC
{
    /// <summary>
    /// Identifies the faction or clan an NPC belongs to.
    /// </summary>
    public enum FactionId
    {
        Neutral,
        GoblinGreen,
        GoblinRed,
    }

    /// <summary>
    /// Abstraction for accessing faction information.
    /// </summary>
    public interface IFactionProvider
    {
        /// <summary>The faction this object belongs to.</summary>
        FactionId Faction { get; }

        /// <summary>
        /// Returns <c>true</c> if this faction is hostile towards <paramref name="other"/>.
        /// </summary>
        bool IsEnemy(FactionId other);
    }

    /// <summary>
    /// Static helper for faction relationship queries.
    /// </summary>
    public static class FactionUtility
    {
        // Relationship matrix: [mine, other] => is enemy
        // Neutral is friendly with every faction.
        private static readonly bool[,] Relationships =
        {
            //                Neutral, GoblinGreen, GoblinRed
            /* Neutral */    { false,   false,      false },
            /* GoblinGreen */{ false,   false,      true  },
            /* GoblinRed */  { false,   true,       false },
        };

        // Aggression matrix: [aggressor, target] => will proactively attack when nearby.
        private static readonly bool[,] AggressiveRelationships =
        {
            //                Neutral, GoblinGreen, GoblinRed
            /* Neutral */    { false,   false,      false },
            /* GoblinGreen */{ false,   false,      true  },
            /* GoblinRed */  { false,   true,       false },
        };

        /// <summary>
        /// Determine if two factions are enemies.
        /// </summary>
        public static bool IsEnemy(FactionId a, FactionId b) => Relationships[(int)a, (int)b];

        /// <summary>
        /// Determine if <paramref name="aggressor"/> will initiate combat when spotting
        /// <paramref name="target"/>.
        /// </summary>
        public static bool IsAggressiveTowardFaction(FactionId aggressor, FactionId target) =>
            AggressiveRelationships[(int)aggressor, (int)target];
    }
}

