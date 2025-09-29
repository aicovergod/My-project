using System;
using System.Collections.Generic;
using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Builds a 2D navigation grid by sampling colliders in the scene and marking tiles as walkable or blocked.
    /// Grids align to the 64×64 OSRS tile spacing so NPC path requests can be resolved deterministically.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class NavGridBuilder : MonoBehaviour
    {
        /// <summary>
        /// Event raised whenever the grid is rebuilt.
        /// </summary>
        public event Action<NavGridBuilder> GridRebuilt;

        [Header("Grid Dimensions")]
        [Tooltip("World-space size of the grid. The component centres the grid on this transform.")]
        [SerializeField] private Vector2 areaSize = new Vector2(32f, 32f);

        [Tooltip("World-space size of an individual tile. 1 unit corresponds to a 64×64px OSRS tile by convention.")]
        [SerializeField] private float tileSize = 1f;

        [Tooltip("Automatically rebuild the grid when entering play mode or when the component awakes.")]
        [SerializeField] private bool autoBuildOnEnable = true;

        [Tooltip("When enabled the grid origin is snapped to the tile size so cells align to the shared OSRS tile lattice.")]
        [SerializeField] private bool snapOriginToTile = true;

        [Header("Obstacle Detection")]
        [Tooltip("Layers treated as solid when baking the navigation grid.")]
        [SerializeField] private LayerMask blockingLayers = 0;

        [Tooltip("Optional tags that should mark colliders as blocking even when they sit outside the layer mask.")]
        [SerializeField] private List<string> blockingTags = new List<string> { "Obstacle", "Blocking", "Wall" };

        [Tooltip("Treat trigger colliders as solid when building the grid.")]
        [SerializeField] private bool includeTriggerColliders;

        [Tooltip("Sample colliders on inactive GameObjects. Disable to ignore temporarily disabled props during baking.")]
        [SerializeField] private bool includeInactiveObjects;

        [Tooltip("Padding applied to the overlap box when sampling blockers. Values below 1 shrink the sampling region slightly.")]
        [SerializeField, Range(0.1f, 1.2f)] private float samplingPadding = 0.92f;

        [Header("Validation & Debug")]
        [Tooltip("Logs summary information whenever the grid is rebuilt.")]
        [SerializeField] private bool enableDebugLogging;

        [Tooltip("Draws the walkable grid overlay in the editor and play mode.")]
        [SerializeField] private bool drawDebugGizmos = true;

        [Tooltip("Color used for walkable cells when drawing gizmos.")]
        [SerializeField] private Color walkableColor = new Color(0f, 0.75f, 0.2f, 0.12f);

        [Tooltip("Color used for blocked cells when drawing gizmos.")]
        [SerializeField] private Color blockedColor = new Color(0.75f, 0f, 0.1f, 0.3f);

        [Tooltip("Elevates gizmos slightly so they do not z-fight with sprites in the scene.")]
        [SerializeField] private float gizmoHeightOffset = 0.05f;

        private bool[,] walkableGrid;
        private Vector2Int gridSize;
        private Vector2 gridOrigin;
        private Vector2 gridWorldSize;
        private bool gridDirty = true;
        private int blockedCellCount;
        private int revision;

        private Vector3 lastRecordedPosition;
        private Vector3 lastRecordedScale;

        /// <summary>
        /// Size of a single tile in world units.
        /// </summary>
        public float TileSize => tileSize;

        /// <summary>
        /// Number of tiles along the X/Y axes.
        /// </summary>
        public Vector2Int GridSize => gridSize;

        /// <summary>
        /// World-space origin of the grid (bottom-left corner).
        /// </summary>
        public Vector2 GridOrigin => gridOrigin;

        /// <summary>
        /// Total world-space dimensions of the baked grid.
        /// </summary>
        public Vector2 GridWorldSize => gridWorldSize;

        /// <summary>
        /// Indicates whether a valid grid is currently cached.
        /// </summary>
        public bool HasGrid => walkableGrid != null && gridSize.x > 0 && gridSize.y > 0;

        /// <summary>
        /// True when the grid should be rebuilt before being used.
        /// </summary>
        public bool NeedsRebuild => gridDirty || !HasGrid;

        /// <summary>
        /// Tracks how many times the grid has been rebuilt. Consumers can cache this value and detect changes.
        /// </summary>
        public int Revision => revision;

        private void Reset()
        {
            lastRecordedPosition = transform.position;
            lastRecordedScale = transform.lossyScale;
        }

        private void Awake()
        {
            lastRecordedPosition = transform.position;
            lastRecordedScale = transform.lossyScale;
            if (autoBuildOnEnable)
            {
                BuildGrid();
            }
        }

        private void OnEnable()
        {
            if (autoBuildOnEnable && NeedsRebuild)
            {
                BuildGrid();
            }
            if (Application.isPlaying)
            {
                PathfindingService.Instance?.RegisterNavGrid(this);
            }
        }

        private void OnValidate()
        {
            tileSize = Mathf.Max(0.01f, tileSize);
            areaSize.x = Mathf.Max(tileSize, Mathf.Abs(areaSize.x));
            areaSize.y = Mathf.Max(tileSize, Mathf.Abs(areaSize.y));
            samplingPadding = Mathf.Clamp(samplingPadding, 0.1f, 1.2f);
            gridDirty = true;

            if (!Application.isPlaying && autoBuildOnEnable)
            {
                BuildGrid();
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                if (transform.position != lastRecordedPosition || transform.lossyScale != lastRecordedScale)
                {
                    lastRecordedPosition = transform.position;
                    lastRecordedScale = transform.lossyScale;
                    gridDirty = true;
                    if (autoBuildOnEnable)
                    {
                        BuildGrid();
                    }
                }
            }
        }

        /// <summary>
        /// Forces the grid to rebuild immediately.
        /// </summary>
        [ContextMenu("Rebuild Navigation Grid")]
        public void BuildGrid()
        {
            if (tileSize <= 0f)
            {
                Debug.LogWarning("Tile size must be positive before building the navigation grid.", this);
                tileSize = 1f;
            }

            gridSize = new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(areaSize.x / tileSize)),
                Mathf.Max(1, Mathf.CeilToInt(areaSize.y / tileSize)));

            gridWorldSize = new Vector2(gridSize.x * tileSize, gridSize.y * tileSize);
            gridOrigin = (Vector2)transform.position - gridWorldSize * 0.5f;
            if (snapOriginToTile)
            {
                gridOrigin.x = Mathf.Floor(gridOrigin.x / tileSize) * tileSize;
                gridOrigin.y = Mathf.Floor(gridOrigin.y / tileSize) * tileSize;
            }

            walkableGrid = new bool[gridSize.x, gridSize.y];
            blockedCellCount = 0;

            float halfTile = tileSize * 0.5f;
            Vector2 sampleExtents = Vector2.one * halfTile * samplingPadding * 2f;
            // Because OverlapBox expects size rather than extents we double the extents.

            for (int y = 0; y < gridSize.y; y++)
            {
                for (int x = 0; x < gridSize.x; x++)
                {
                    Vector2 cellCenter = GetCellCenter(new Vector2Int(x, y));
                    bool blocked = SampleBlocked(cellCenter, sampleExtents);
                    walkableGrid[x, y] = !blocked;
                    if (blocked)
                    {
                        blockedCellCount++;
                    }
                }
            }

            gridDirty = false;
            revision++;

            if (enableDebugLogging)
            {
                int totalCells = gridSize.x * gridSize.y;
                int walkableCells = totalCells - blockedCellCount;
                Debug.Log($"NavGridBuilder rebuilt {name}: {walkableCells} walkable / {blockedCellCount} blocked (tile size {tileSize:F2}).", this);
            }

            GridRebuilt?.Invoke(this);
            if (Application.isPlaying)
            {
                PathfindingService.Instance?.RegisterNavGrid(this);
            }
        }

        /// <summary>
        /// Returns whether the supplied cell is inside the cached grid bounds.
        /// </summary>
        public bool IsCellWithinBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < gridSize.x && cell.y >= 0 && cell.y < gridSize.y;
        }

        /// <summary>
        /// Returns whether a specific cell is considered walkable. Returns <c>false</c> for out-of-bounds cells.
        /// </summary>
        public bool IsCellWalkable(Vector2Int cell)
        {
            if (!HasGrid || !IsCellWithinBounds(cell))
            {
                return false;
            }

            return walkableGrid[cell.x, cell.y];
        }

        /// <summary>
        /// Converts a world-space position to a grid cell. Returns <c>false</c> when the position lies outside the grid.
        /// </summary>
        public bool TryGetCell(Vector2 worldPosition, out Vector2Int cell)
        {
            if (!HasGrid)
            {
                cell = Vector2Int.zero;
                return false;
            }

            Vector2 offset = worldPosition - gridOrigin;
            if (offset.x < 0f || offset.y < 0f)
            {
                cell = Vector2Int.zero;
                return false;
            }

            int x = Mathf.FloorToInt(offset.x / tileSize);
            int y = Mathf.FloorToInt(offset.y / tileSize);

            cell = new Vector2Int(x, y);
            if (!IsCellWithinBounds(cell))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Clamps the supplied cell coordinates to the grid bounds.
        /// </summary>
        public Vector2Int ClampToBounds(Vector2Int cell)
        {
            if (!HasGrid)
            {
                return Vector2Int.zero;
            }

            int x = Mathf.Clamp(cell.x, 0, gridSize.x - 1);
            int y = Mathf.Clamp(cell.y, 0, gridSize.y - 1);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// Converts a cell coordinate back to a world-space tile centre.
        /// </summary>
        public Vector2 GetCellCenter(Vector2Int cell)
        {
            Vector2 clamped = ClampToBounds(cell);
            return new Vector2(
                gridOrigin.x + (clamped.x + 0.5f) * tileSize,
                gridOrigin.y + (clamped.y + 0.5f) * tileSize);
        }

        /// <summary>
        /// Converts a world position into the nearest cell, clamped to the grid bounds.
        /// </summary>
        public Vector2Int WorldToCellClamped(Vector2 worldPosition)
        {
            if (!HasGrid)
            {
                return Vector2Int.zero;
            }

            Vector2 offset = worldPosition - gridOrigin;
            int x = Mathf.FloorToInt(offset.x / tileSize);
            int y = Mathf.FloorToInt(offset.y / tileSize);
            return ClampToBounds(new Vector2Int(x, y));
        }

        /// <summary>
        /// Returns true if the world position maps to a walkable cell.
        /// </summary>
        public bool IsWorldPositionWalkable(Vector2 worldPosition)
        {
            return TryGetCell(worldPosition, out var cell) && IsCellWalkable(cell);
        }

        private bool SampleBlocked(Vector2 centre, Vector2 size)
        {
            bool blocked = false;

            Collider2D[] hits2D = Physics2D.OverlapBoxAll(centre, size, 0f);
            for (int i = 0; i < hits2D.Length; i++)
            {
                var hit = hits2D[i];
                if (hit == null)
                {
                    continue;
                }

                if (!includeTriggerColliders && hit.isTrigger)
                {
                    continue;
                }

                if (!includeInactiveObjects && !hit.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (IsColliderBlocking(hit.gameObject))
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                Collider[] hits3D = Physics.OverlapBox(new Vector3(centre.x, centre.y, transform.position.z), new Vector3(size.x, size.y, tileSize) * 0.5f, Quaternion.identity);
                for (int i = 0; i < hits3D.Length; i++)
                {
                    var hit = hits3D[i];
                    if (hit == null)
                    {
                        continue;
                    }

                    if (!includeTriggerColliders && hit.isTrigger)
                    {
                        continue;
                    }

                    if (!includeInactiveObjects && !hit.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    if (IsColliderBlocking(hit.gameObject))
                    {
                        blocked = true;
                        break;
                    }
                }
            }

            return blocked;
        }

        private bool IsColliderBlocking(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            int layerMask = 1 << obj.layer;
            if ((blockingLayers.value & layerMask) != 0)
            {
                return true;
            }

            if (blockingTags != null)
            {
                for (int i = 0; i < blockingTags.Count; i++)
                {
                    string tag = blockingTags[i];
                    if (!string.IsNullOrEmpty(tag) && obj.CompareTag(tag))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawDebugGizmos || !HasGrid)
            {
                return;
            }

            float z = transform.position.z + gizmoHeightOffset;
            Vector3 size = new Vector3(tileSize, tileSize, 0f);
            for (int y = 0; y < gridSize.y; y++)
            {
                for (int x = 0; x < gridSize.x; x++)
                {
                    Vector3 centre = new Vector3(gridOrigin.x + (x + 0.5f) * tileSize, gridOrigin.y + (y + 0.5f) * tileSize, z);
                    Gizmos.color = walkableGrid[x, y] ? walkableColor : blockedColor;
                    Gizmos.DrawCube(centre, size);
                }
            }
        }
#endif
    }
}
