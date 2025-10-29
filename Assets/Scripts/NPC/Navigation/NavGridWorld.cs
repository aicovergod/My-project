using System.Collections.Generic;
using UnityEngine;

namespace NPC.Navigation
{
    /// <summary>
    /// Aggregates streamed <see cref="NavGridChunkDefinition"/> assets into a unified global grid so
    /// pathfinding logic can traverse seamlessly across chunk boundaries.
    /// </summary>
    public sealed class NavGridWorld : INavGridData
    {
        private sealed class ChunkRecord
        {
            public NavGridChunkDefinition Definition;
            public Vector2Int Coordinates;
            public Vector2Int Dimensions;
            public Vector2Int CellOrigin;
            public Rect WorldBounds;
        }

        private static bool enableDebugLogging;

        private readonly Dictionary<Vector2Int, ChunkRecord> chunkLookup = new Dictionary<Vector2Int, ChunkRecord>();
        private LayerMask blockingLayerMask;
        private Vector2Int configuredChunkDimensions;
        private float configuredTileSize;
        private Vector2 configuredWorldOrigin;
        private int revision;

        /// <summary>
        /// Creates a new world wrapper.
        /// </summary>
        public NavGridWorld(Vector2Int chunkDimensions, float tileSize, Vector2 worldOrigin, LayerMask blockingLayerMask)
        {
            configuredChunkDimensions = new Vector2Int(Mathf.Max(1, chunkDimensions.x), Mathf.Max(1, chunkDimensions.y));
            configuredTileSize = Mathf.Max(0.0001f, tileSize);
            configuredWorldOrigin = worldOrigin;
            this.blockingLayerMask = blockingLayerMask;
        }

        /// <summary>
        /// Globally toggles verbose logging for navgrid world aggregation.
        /// </summary>
        public static bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set => enableDebugLogging = value;
        }

        /// <summary>
        /// Updates the base configuration used when interpreting chunk coordinates.
        /// </summary>
        public void UpdateConfiguration(Vector2Int chunkDimensions, float tileSize, Vector2 worldOrigin)
        {
            configuredChunkDimensions = new Vector2Int(Mathf.Max(1, chunkDimensions.x), Mathf.Max(1, chunkDimensions.y));
            configuredTileSize = Mathf.Max(0.0001f, tileSize);
            configuredWorldOrigin = worldOrigin;
        }

        /// <summary>
        /// Updates the blocking layer mask published to consumers.
        /// </summary>
        public void UpdateBlockingMask(LayerMask mask)
        {
            blockingLayerMask = mask;
        }

        /// <summary>
        /// Adds or replaces a chunk inside the world map.
        /// </summary>
        public void AddOrUpdateChunk(NavGridChunkDefinition chunk)
        {
            if (chunk == null)
            {
                return;
            }

            var record = BuildRecord(chunk);
            chunkLookup[chunk.ChunkCoordinates] = record;
            revision++;
            Log($"Chunk {chunk.ChunkCoordinates} registered. Cell origin {record.CellOrigin}, size {record.Dimensions}.");
        }

        /// <summary>
        /// Removes a chunk from the world map.
        /// </summary>
        public void RemoveChunk(Vector2Int coordinates)
        {
            if (chunkLookup.Remove(coordinates))
            {
                revision++;
                Log($"Chunk {coordinates} removed from world map.");
            }
        }

        /// <inheritdoc />
        public int Revision => revision;

        /// <inheritdoc />
        public float TileSize => configuredTileSize;

        /// <inheritdoc />
        public LayerMask BlockingLayerMask => blockingLayerMask;

        /// <inheritdoc />
        public bool HasData => chunkLookup.Count > 0;

        /// <inheritdoc />
        public bool TryGetCell(Vector2 worldPosition, out Vector2Int cell)
        {
            foreach (var entry in chunkLookup)
            {
                ChunkRecord record = entry.Value;
                if (!record.WorldBounds.Contains(worldPosition))
                {
                    continue;
                }

                if (!record.Definition.TryGetLocalCell(worldPosition, out Vector2Int local))
                {
                    continue;
                }

                cell = record.CellOrigin + local;
                return true;
            }

            cell = default;
            return false;
        }

        /// <inheritdoc />
        public Vector2Int WorldToCellClamped(Vector2 worldPosition)
        {
            if (TryGetCell(worldPosition, out Vector2Int cell))
            {
                return cell;
            }

            float bestDistance = float.MaxValue;
            ChunkRecord bestRecord = null;
            Vector2 bestPoint = Vector2.zero;

            foreach (var entry in chunkLookup)
            {
                ChunkRecord record = entry.Value;
                Vector2 closest = ClampToRect(worldPosition, record.WorldBounds);
                float distance = (closest - worldPosition).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestRecord = record;
                bestPoint = closest;
            }

            if (bestRecord == null)
            {
                return Vector2Int.zero;
            }

            Vector2 relative = bestPoint - bestRecord.Definition.ChunkOrigin;
            float tileSize = Mathf.Max(0.0001f, bestRecord.Definition.TileSize);
            int x = Mathf.Clamp(Mathf.FloorToInt(relative.x / tileSize), 0, bestRecord.Dimensions.x - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(relative.y / tileSize), 0, bestRecord.Dimensions.y - 1);
            return bestRecord.CellOrigin + new Vector2Int(x, y);
        }

        /// <inheritdoc />
        public Vector2 GetCellCenter(Vector2Int cell)
        {
            if (!TryResolveLocalCell(cell, out _, out var local))
            {
                return configuredWorldOrigin;
            }

            if (TryGetRecordForCell(cell, out var record))
            {
                return record.Definition.GetCellCenter(local);
            }

            return configuredWorldOrigin;
        }

        /// <inheritdoc />
        public bool IsCellWalkable(Vector2Int cell)
        {
            if (!TryResolveLocalCell(cell, out _, out var local))
            {
                return false;
            }

            return TryGetRecordForCell(cell, out var record) && record.Definition.IsCellWalkable(local);
        }

        /// <inheritdoc />
        public bool IsCellWithinBounds(Vector2Int cell)
        {
            return TryResolveLocalCell(cell, out _, out _);
        }

        /// <inheritdoc />
        public bool HasClearLineBetweenCells(Vector2Int origin, Vector2Int goal)
        {
            if (!HasData)
            {
                return true;
            }

            if (!IsCellWithinBounds(origin) || !IsCellWithinBounds(goal))
            {
                return false;
            }

            if (origin == goal)
            {
                return IsCellWalkable(origin);
            }

            if (!IsCellWalkable(origin))
            {
                return false;
            }

            int x = origin.x;
            int y = origin.y;
            int endX = goal.x;
            int endY = goal.y;
            int dx = Mathf.Abs(endX - x);
            int dy = Mathf.Abs(endY - y);
            int stepX = x < endX ? 1 : (x > endX ? -1 : 0);
            int stepY = y < endY ? 1 : (y > endY ? -1 : 0);
            int error = dx - dy;
            Vector2Int currentCell = new Vector2Int(x, y);

            while (true)
            {
                int error2 = error * 2;
                if (error2 > -dy)
                {
                    error -= dy;
                    x += stepX;
                }

                if (error2 < dx)
                {
                    error += dx;
                    y += stepY;
                }

                currentCell.x = x;
                currentCell.y = y;

                if (x == endX && y == endY)
                {
                    break;
                }

                if (!IsCellWithinBounds(currentCell) || !IsCellWalkable(currentCell))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public bool TryResolveChunkForCell(Vector2Int cell, out Vector2Int chunkCoordinates)
        {
            foreach (var entry in chunkLookup)
            {
                ChunkRecord record = entry.Value;
                if (IsCellInsideRecord(cell, record))
                {
                    chunkCoordinates = record.Coordinates;
                    return true;
                }
            }

            chunkCoordinates = default;
            return false;
        }

        /// <inheritdoc />
        public bool TryResolveLocalCell(Vector2Int cell, out Vector2Int chunkCoordinates, out Vector2Int localCell)
        {
            foreach (var entry in chunkLookup)
            {
                ChunkRecord record = entry.Value;
                if (!IsCellInsideRecord(cell, record))
                {
                    continue;
                }

                chunkCoordinates = record.Coordinates;
                localCell = new Vector2Int(cell.x - record.CellOrigin.x, cell.y - record.CellOrigin.y);
                return true;
            }

            chunkCoordinates = default;
            localCell = default;
            return false;
        }

        private ChunkRecord BuildRecord(NavGridChunkDefinition chunk)
        {
            Vector2Int dimensions = chunk.GridDimensions;
            Vector2 chunkOrigin = chunk.ChunkOrigin;
            Vector2 chunkSize = new Vector2(dimensions.x * chunk.TileSize, dimensions.y * chunk.TileSize);
            Vector2Int cellOrigin = new Vector2Int(
                chunk.ChunkCoordinates.x * configuredChunkDimensions.x,
                chunk.ChunkCoordinates.y * configuredChunkDimensions.y);

            return new ChunkRecord
            {
                Definition = chunk,
                Coordinates = chunk.ChunkCoordinates,
                Dimensions = dimensions,
                CellOrigin = cellOrigin,
                WorldBounds = new Rect(chunkOrigin, chunkSize)
            };
        }

        private static Vector2 ClampToRect(Vector2 value, Rect rect)
        {
            float clampedX = Mathf.Clamp(value.x, rect.xMin, rect.xMax);
            float clampedY = Mathf.Clamp(value.y, rect.yMin, rect.yMax);
            return new Vector2(clampedX, clampedY);
        }

        private static bool IsCellInsideRecord(Vector2Int cell, ChunkRecord record)
        {
            return cell.x >= record.CellOrigin.x && cell.x < record.CellOrigin.x + record.Dimensions.x &&
                   cell.y >= record.CellOrigin.y && cell.y < record.CellOrigin.y + record.Dimensions.y;
        }

        private bool TryGetRecordForCell(Vector2Int cell, out ChunkRecord record)
        {
            foreach (var entry in chunkLookup)
            {
                ChunkRecord candidate = entry.Value;
                if (IsCellInsideRecord(cell, candidate))
                {
                    record = candidate;
                    return true;
                }
            }

            record = null;
            return false;
        }

        private static void Log(string message)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.Log($"[NavGridWorld] {message}");
        }
    }
}
