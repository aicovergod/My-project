using UnityEngine;

namespace NPC.Navigation
{
    /// <summary>
    /// Abstraction for navigation grid data so pathfinding and occupancy systems can operate
    /// against streamed chunk data or editor-built grids transparently.
    /// </summary>
    public interface INavGridData
    {
        /// <summary>
        /// Current revision of the data set. Increment whenever the underlying grid changes so
        /// long-running searches can detect invalidation.
        /// </summary>
        int Revision { get; }

        /// <summary>
        /// Size of a single tile measured in world units.
        /// </summary>
        float TileSize { get; }

        /// <summary>
        /// Layer mask describing colliders that block navigation within this grid.
        /// </summary>
        LayerMask BlockingLayerMask { get; }

        /// <summary>
        /// Returns <c>true</c> when at least one navigation chunk is available.
        /// </summary>
        bool HasData { get; }

        /// <summary>
        /// Attempts to convert a world-space position to a global cell coordinate.
        /// </summary>
        bool TryGetCell(Vector2 worldPosition, out Vector2Int cell);

        /// <summary>
        /// Converts a world-space position to the nearest valid global cell coordinate.
        /// </summary>
        Vector2Int WorldToCellClamped(Vector2 worldPosition);

        /// <summary>
        /// Converts a global cell coordinate back into a world-space tile centre.
        /// </summary>
        Vector2 GetCellCenter(Vector2Int cell);

        /// <summary>
        /// Returns <c>true</c> when the supplied global cell is marked walkable.
        /// </summary>
        bool IsCellWalkable(Vector2Int cell);

        /// <summary>
        /// Returns <c>true</c> when the supplied global cell is backed by loaded navigation data.
        /// </summary>
        bool IsCellWithinBounds(Vector2Int cell);

        /// <summary>
        /// Determines whether a straight corridor between two global cells remains unblocked.
        /// </summary>
        bool HasClearLineBetweenCells(Vector2Int origin, Vector2Int goal);

        /// <summary>
        /// Resolves the chunk coordinate that owns the supplied global cell.
        /// </summary>
        bool TryResolveChunkForCell(Vector2Int cell, out Vector2Int chunkCoordinates);

        /// <summary>
        /// Resolves the chunk coordinate and chunk-local cell for the supplied global cell.
        /// </summary>
        bool TryResolveLocalCell(Vector2Int cell, out Vector2Int chunkCoordinates, out Vector2Int localCell);
    }
}
