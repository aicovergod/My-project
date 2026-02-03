using System;
using System.Collections.Generic;
using UnityEngine;

namespace NPC.Navigation
{
    /// <summary>
    /// ScriptableObject that stores the baked navigation data for a single grid chunk so runtime systems
    /// can stream nav information without triggering an expensive rebake. Chunks always align to the global
    /// nav lattice so NPC movement remains deterministic across streamed regions.
    /// </summary>
    public sealed class NavGridChunkDefinition : ScriptableObject
    {
        [Header("Chunk Metadata")]
        [SerializeField] private Vector2Int chunkCoordinates;

        [Tooltip("Unique identifier generated from the chunk coordinates (chunk_X_Y).")]
        [SerializeField] private string chunkId;

        [Tooltip("World-space origin (bottom-left corner) of the chunk in units.")]
        [SerializeField] private Vector2 chunkOrigin;

        [Tooltip("Size of a single nav tile in world units. Should align with the 64×64px OSRS tile size.")]
        [SerializeField] private float tileSize;

        [Tooltip("Dimensions of the chunk grid in tiles.")]
        [SerializeField] private Vector2Int gridDimensions;

        [Header("Baked Data")]
        [Tooltip("Packed walkability data stored as a bitset where 1 = walkable and 0 = blocked.")]
        [SerializeField] private byte[] walkableBitset = Array.Empty<byte>();

        [Tooltip("Neighbour chunk coordinates used for streaming adjacency lookups.")]
        [SerializeField] private List<Vector2Int> neighbourChunkCoordinates = new List<Vector2Int>();

        [Tooltip("Neighbour chunk identifiers matching the coordinate list.")]
        [SerializeField] private List<string> neighbourChunkIds = new List<string>();

        private static readonly Vector2Int[] CardinalDirections =
        {
            new Vector2Int(-1, 0),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(0, 1)
        };

        /// <summary>
        /// Coordinates of this chunk in chunk space.
        /// </summary>
        public Vector2Int ChunkCoordinates => chunkCoordinates;

        /// <summary>
        /// Unique identifier for the chunk. Defaults to <c>chunk_X_Y</c> if the serialized value is empty.
        /// </summary>
        public string ChunkId => string.IsNullOrEmpty(chunkId) ? ComposeChunkId(chunkCoordinates) : chunkId;

        /// <summary>
        /// World-space origin (bottom-left corner) for the chunk.
        /// </summary>
        public Vector2 ChunkOrigin => chunkOrigin;

        /// <summary>
        /// Size of each nav tile in world units.
        /// </summary>
        public float TileSize => tileSize;

        /// <summary>
        /// Width/height of the chunk in tiles.
        /// </summary>
        public Vector2Int GridDimensions => gridDimensions;

        /// <summary>
        /// Bounding rectangle of the chunk in world space.
        /// </summary>
        public Rect WorldBounds => new Rect(chunkOrigin, new Vector2(gridDimensions.x * tileSize, gridDimensions.y * tileSize));

        /// <summary>
        /// List of neighbour chunk coordinates.
        /// </summary>
        public IReadOnlyList<Vector2Int> NeighbourChunkCoordinates => neighbourChunkCoordinates;

        /// <summary>
        /// List of neighbour chunk identifiers.
        /// </summary>
        public IReadOnlyList<string> NeighbourChunkIds => neighbourChunkIds;

        /// <summary>
        /// Returns whether the supplied local cell is marked as walkable.
        /// </summary>
        public bool IsCellWalkable(Vector2Int localCell)
        {
            return TryGetWalkable(localCell, out bool isWalkable) && isWalkable;
        }

        /// <summary>
        /// Returns whether the supplied world position maps to a walkable tile inside this chunk.
        /// </summary>
        public bool IsWorldPositionWalkable(Vector2 worldPosition)
        {
            return TryGetLocalCell(worldPosition, out Vector2Int localCell) && IsCellWalkable(localCell);
        }

        /// <summary>
        /// Attempts to convert the supplied world position into a chunk-local cell coordinate.
        /// </summary>
        public bool TryGetLocalCell(Vector2 worldPosition, out Vector2Int localCell)
        {
            Vector2 relative = worldPosition - chunkOrigin;
            int x = Mathf.FloorToInt(relative.x / Mathf.Max(tileSize, 0.0001f));
            int y = Mathf.FloorToInt(relative.y / Mathf.Max(tileSize, 0.0001f));

            localCell = new Vector2Int(x, y);
            if (!IsCellWithinBounds(localCell))
            {
                localCell = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to fetch the walkable flag for a local cell coordinate.
        /// </summary>
        public bool TryGetWalkable(Vector2Int localCell, out bool isWalkable)
        {
            if (!IsCellWithinBounds(localCell) || walkableBitset == null || walkableBitset.Length == 0)
            {
                isWalkable = false;
                return false;
            }

            int flatIndex = GetFlatIndex(localCell);
            isWalkable = ReadBit(flatIndex);
            return true;
        }

        /// <summary>
        /// Returns the world-space centre of a local cell.
        /// </summary>
        public Vector2 GetCellCenter(Vector2Int localCell)
        {
            return new Vector2(
                chunkOrigin.x + (localCell.x + 0.5f) * tileSize,
                chunkOrigin.y + (localCell.y + 0.5f) * tileSize);
        }

        private bool IsCellWithinBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < gridDimensions.x && cell.y >= 0 && cell.y < gridDimensions.y;
        }

        private int GetFlatIndex(Vector2Int cell)
        {
            return cell.y * Mathf.Max(1, gridDimensions.x) + cell.x;
        }

        private bool ReadBit(int index)
        {
            if (index < 0)
            {
                return false;
            }

            int byteIndex = index >> 3;
            if (walkableBitset == null || byteIndex < 0 || byteIndex >= walkableBitset.Length)
            {
                return false;
            }

            int bitIndex = index & 7;
            return (walkableBitset[byteIndex] & (1 << bitIndex)) != 0;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Applies freshly baked grid data to the asset. Editor-only because baking never occurs at runtime.
        /// </summary>
        internal void ApplyBakedData(Vector2Int coordinates, Vector2 origin, float size, Vector2Int dimensions, bool[,] walkable)
        {
            chunkCoordinates = coordinates;
            chunkId = ComposeChunkId(coordinates);
            chunkOrigin = origin;
            tileSize = Mathf.Max(0.0001f, size);
            gridDimensions = new Vector2Int(Mathf.Max(1, dimensions.x), Mathf.Max(1, dimensions.y));
            walkableBitset = PackWalkableData(walkable, gridDimensions);
        }

        /// <summary>
        /// Assigns neighbour metadata so streaming systems can resolve adjacency without scanning directories.
        /// </summary>
        internal void ApplyNeighbourMetadata(IReadOnlyList<Vector2Int> neighbours)
        {
            neighbourChunkCoordinates.Clear();
            neighbourChunkIds.Clear();

            if (neighbours == null)
            {
                return;
            }

            for (int i = 0; i < neighbours.Count; i++)
            {
                Vector2Int neighbour = neighbours[i];
                if (neighbourChunkCoordinates.Contains(neighbour))
                {
                    continue;
                }

                neighbourChunkCoordinates.Add(neighbour);
                neighbourChunkIds.Add(ComposeChunkId(neighbour));
            }
        }
#endif

        private static byte[] PackWalkableData(bool[,] walkable, Vector2Int dimensions)
        {
            if (walkable == null || dimensions.x <= 0 || dimensions.y <= 0)
            {
                return Array.Empty<byte>();
            }

            int width = Mathf.Min(dimensions.x, walkable.GetLength(0));
            int height = Mathf.Min(dimensions.y, walkable.GetLength(1));
            int totalCells = Mathf.Max(0, width * height);
            byte[] bitset = new byte[(totalCells + 7) / 8];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!walkable[x, y])
                    {
                        continue;
                    }

                    int flatIndex = y * dimensions.x + x;
                    int byteIndex = flatIndex >> 3;
                    int bitIndex = flatIndex & 7;
                    bitset[byteIndex] = (byte)(bitset[byteIndex] | (1 << bitIndex));
                }
            }

            return bitset;
        }

        /// <summary>
        /// Builds a stable chunk identifier from the supplied coordinates.
        /// </summary>
        public static string ComposeChunkId(Vector2Int coordinates)
        {
            return $"chunk_{coordinates.x}_{coordinates.y}";
        }

        /// <summary>
        /// Attempts to parse a chunk identifier back into chunk coordinates.
        /// </summary>
        public static bool TryParseChunkId(string value, out Vector2Int coordinates)
        {
            coordinates = default;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string[] parts = value.Split('_');
            if (parts.Length != 3 || !string.Equals(parts[0], "chunk", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y))
            {
                return false;
            }

            coordinates = new Vector2Int(x, y);
            return true;
        }

        /// <summary>
        /// Returns the cardinal neighbour coordinates for the supplied chunk coordinate.
        /// </summary>
        public static IEnumerable<Vector2Int> EnumerateCardinalNeighbours(Vector2Int coordinates)
        {
            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                yield return coordinates + CardinalDirections[i];
            }
        }
    }
}
