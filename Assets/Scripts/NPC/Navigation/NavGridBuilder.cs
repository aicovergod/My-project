using System;
using System.Collections.Generic;
using UnityEngine;
using NPC.Navigation;
#if UNITY_EDITOR
using UnityEditorInternal;
#endif

namespace NPC
{
    /// <summary>
    /// Builds a 2D navigation grid by sampling colliders in the scene and marking tiles as walkable or blocked.
    /// Grids align to the 64×64 OSRS tile spacing so NPC path requests can be resolved deterministically.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class NavGridBuilder : MonoBehaviour, ISerializationCallbackReceiver, INavGridData
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
        [SerializeField] private List<string> blockingTags = new List<string>();

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

        private static readonly HashSet<string> reportedMissingTags = new HashSet<string>();

        private bool[,] walkableGrid;
        private Vector2Int gridSize;
        private Vector2 gridOrigin;
        private Vector2 gridWorldSize;
        private bool gridDirty = true;
        private int blockedCellCount;
        private int revision;

        private readonly Dictionary<Vector2Int, bool> manualOverrides = new Dictionary<Vector2Int, bool>();
        [SerializeField, HideInInspector] private List<Vector2Int> serializedManualOverrideKeys = new List<Vector2Int>();
        [SerializeField, HideInInspector] private List<bool> serializedManualOverrideValues = new List<bool>();
        private readonly List<Vector2Int> overrideCleanupBuffer = new List<Vector2Int>();

        private Vector3 lastRecordedPosition;
        private Vector3 lastRecordedScale;

        /// <summary>
        /// Size of a single tile in world units.
        /// </summary>
        public float TileSize => tileSize;

        /// <inheritdoc />
        LayerMask INavGridData.BlockingLayerMask => blockingLayers;

        /// <inheritdoc />
        bool INavGridData.HasData => HasGrid;

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

        /// <inheritdoc />
        int INavGridData.Revision => Revision;

        /// <inheritdoc />
        bool INavGridData.TryGetCell(Vector2 worldPosition, out Vector2Int cell) => TryGetCell(worldPosition, out cell);

        /// <inheritdoc />
        Vector2Int INavGridData.WorldToCellClamped(Vector2 worldPosition) => WorldToCellClamped(worldPosition);

        /// <inheritdoc />
        Vector2 INavGridData.GetCellCenter(Vector2Int cell) => GetCellCenter(cell);

        /// <inheritdoc />
        bool INavGridData.IsCellWalkable(Vector2Int cell) => IsCellWalkable(cell);

        /// <inheritdoc />
        bool INavGridData.IsCellWithinBounds(Vector2Int cell) => IsCellWithinBounds(cell);

        /// <inheritdoc />
        bool INavGridData.HasClearLineBetweenCells(Vector2Int origin, Vector2Int goal) => HasClearLineBetweenCells(origin, goal);

        /// <inheritdoc />
        bool INavGridData.TryResolveChunkForCell(Vector2Int cell, out Vector2Int chunkCoordinates)
        {
            if (!IsCellWithinBounds(cell))
            {
                chunkCoordinates = default;
                return false;
            }

            chunkCoordinates = Vector2Int.zero;
            return true;
        }

        /// <inheritdoc />
        bool INavGridData.TryResolveLocalCell(Vector2Int cell, out Vector2Int chunkCoordinates, out Vector2Int localCell)
        {
            if (!IsCellWithinBounds(cell))
            {
                chunkCoordinates = default;
                localCell = default;
                return false;
            }

            chunkCoordinates = Vector2Int.zero;
            localCell = cell;
            return true;
        }

        /// <summary>
        /// True when the grid should be rebuilt before being used.
        /// </summary>
        public bool NeedsRebuild => gridDirty || !HasGrid;

        /// <summary>
        /// Tracks how many times the grid has been rebuilt. Consumers can cache this value and detect changes.
        /// </summary>
        public int Revision => revision;

        /// <summary>
        /// Manual walkable overrides keyed by cell coordinates. When populated the stored value takes priority over collider sampling.
        /// </summary>
        public IReadOnlyDictionary<Vector2Int, bool> ManualOverrides => manualOverrides;

        /// <inheritdoc />
        public void OnBeforeSerialize()
        {
            // Mirror the runtime dictionary into serializable lists so Unity can persist manual overrides in scenes/prefabs.
            serializedManualOverrideKeys.Clear();
            serializedManualOverrideValues.Clear();

            foreach (KeyValuePair<Vector2Int, bool> kvp in manualOverrides)
            {
                serializedManualOverrideKeys.Add(kvp.Key);
                serializedManualOverrideValues.Add(kvp.Value);
            }
        }

        /// <inheritdoc />
        public void OnAfterDeserialize()
        {
            // Restore the dictionary from the serialized lists. We clamp to the shorter list length in case of mismatched data.
            manualOverrides.Clear();
            int restoreCount = Mathf.Min(serializedManualOverrideKeys.Count, serializedManualOverrideValues.Count);
            for (int i = 0; i < restoreCount; i++)
            {
                manualOverrides[serializedManualOverrideKeys[i]] = serializedManualOverrideValues[i];
            }

            // Lists are only used during serialization, so clear them to avoid leaking duplicate state during edits.
            serializedManualOverrideKeys.Clear();
            serializedManualOverrideValues.Clear();
        }

        /// <summary>
        /// Physics mask used to determine which layers block navigation during grid baking.
        /// Exposed so other systems (combat, LOS checks) can remain aligned with the baked data.
        /// </summary>
        public LayerMask BlockingLayerMask => blockingLayers;

        private void Reset()
        {
            SanitizeBlockingTags();
            lastRecordedPosition = transform.position;
            lastRecordedScale = transform.lossyScale;
        }

        private void Awake()
        {
            SanitizeBlockingTags();
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
            SanitizeBlockingTags();
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

            Vector2 samplingSize = GetSamplingSize();
            // OverlapBox expects a size vector rather than half extents, so we pass the computed sampling size directly.

            for (int y = 0; y < gridSize.y; y++)
            {
                for (int x = 0; x < gridSize.x; x++)
                {
                    Vector2 cellCenter = GetCellCenter(new Vector2Int(x, y));
                    bool blocked = SampleBlocked(cellCenter, samplingSize);
                    walkableGrid[x, y] = !blocked;
                }
            }

            ApplyManualOverridesToGrid();

            FinalizeGridMutation(logSummary: true);
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
        /// Determines whether a straight corridor between two grid cells remains free of blockers.
        /// The evaluation walks a Bresenham line and ignores the final goal cell so interactions that target
        /// a blocked tile (such as a fence) can still approach the closest open neighbour.
        /// </summary>
        public bool HasClearLineBetweenCells(Vector2Int origin, Vector2Int goal)
        {
            if (!HasGrid)
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

        /// <summary>
        /// Computes the overlap box size used when sampling colliders for a grid cell.
        /// </summary>
        private Vector2 GetSamplingSize()
        {
            float clampedPadding = Mathf.Clamp(samplingPadding, 0.1f, 1.2f);
            float halfTile = Mathf.Max(0.0001f, tileSize * 0.5f);
            return Vector2.one * halfTile * clampedPadding * 2f;
        }

        /// <summary>
        /// Applies cached manual overrides to the active walkable grid, removing entries that no longer fit inside the bounds.
        /// </summary>
        private void ApplyManualOverridesToGrid()
        {
            if (!HasGrid || manualOverrides.Count == 0)
            {
                return;
            }

            overrideCleanupBuffer.Clear();

            foreach (KeyValuePair<Vector2Int, bool> kvp in manualOverrides)
            {
                Vector2Int storedCell = kvp.Key;
                if (!IsCellWithinBounds(storedCell))
                {
                    overrideCleanupBuffer.Add(storedCell);
                    continue;
                }

                Vector2Int clampedCell = ClampToBounds(storedCell);
                walkableGrid[clampedCell.x, clampedCell.y] = kvp.Value;
            }

            for (int i = 0; i < overrideCleanupBuffer.Count; i++)
            {
                manualOverrides.Remove(overrideCleanupBuffer[i]);
            }

            overrideCleanupBuffer.Clear();
        }

        /// <summary>
        /// Attempts to add or update a manual walkability override for the supplied cell.
        /// </summary>
        public bool TrySetManualOverride(Vector2Int cell, bool walkable)
        {
            if (!HasGrid || !IsCellWithinBounds(cell))
            {
                return false;
            }

            manualOverrides[cell] = walkable;
            ApplyManualOverrideToGrid(cell, walkable);
            FinalizeGridMutation(logSummary: false);
            return true;
        }

        /// <summary>
        /// Attempts to add or update a manual walkability override for the cell mapped from the supplied world position.
        /// </summary>
        public bool TrySetManualOverride(Vector2 worldPosition, bool walkable)
        {
            if (!TryGetCell(worldPosition, out Vector2Int cell))
            {
                return false;
            }

            return TrySetManualOverride(cell, walkable);
        }

        /// <summary>
        /// Clears an existing manual override for the supplied cell and restores the collider-sampled state.
        /// </summary>
        public bool ClearManualOverride(Vector2Int cell)
        {
            if (!HasGrid || !IsCellWithinBounds(cell))
            {
                return false;
            }

            if (!manualOverrides.Remove(cell))
            {
                return false;
            }

            bool walkable = SampleAutomaticWalkableState(cell);
            walkableGrid[cell.x, cell.y] = walkable;
            FinalizeGridMutation(logSummary: false);
            return true;
        }

        /// <summary>
        /// Clears a manual override for the cell mapped from the supplied world position, restoring the baked state.
        /// </summary>
        public bool ClearManualOverride(Vector2 worldPosition)
        {
            if (!TryGetCell(worldPosition, out Vector2Int cell))
            {
                return false;
            }

            return ClearManualOverride(cell);
        }

        /// <summary>
        /// Toggles the manual override state for the supplied cell. When no override exists the current walkability is inverted.
        /// </summary>
        public bool ToggleManualOverride(Vector2Int cell)
        {
            if (!HasGrid || !IsCellWithinBounds(cell))
            {
                return false;
            }

            bool newState = manualOverrides.TryGetValue(cell, out bool existing)
                ? !existing
                : !walkableGrid[cell.x, cell.y];

            manualOverrides[cell] = newState;
            ApplyManualOverrideToGrid(cell, newState);
            FinalizeGridMutation(logSummary: false);
            return true;
        }

        /// <summary>
        /// Toggles the manual override mapped from the supplied world position.
        /// </summary>
        public bool ToggleManualOverride(Vector2 worldPosition)
        {
            if (!TryGetCell(worldPosition, out Vector2Int cell))
            {
                return false;
            }

            return ToggleManualOverride(cell);
        }

        private void ApplyManualOverrideToGrid(Vector2Int cell, bool walkable)
        {
            Vector2Int clampedCell = ClampToBounds(cell);
            walkableGrid[clampedCell.x, clampedCell.y] = walkable;
        }

        private bool SampleAutomaticWalkableState(Vector2Int cell)
        {
            Vector2 samplingSize = GetSamplingSize();
            Vector2 cellCenter = GetCellCenter(cell);
            bool blocked = SampleBlocked(cellCenter, samplingSize);
            return !blocked;
        }

        private void FinalizeGridMutation(bool logSummary)
        {
            if (!HasGrid)
            {
                return;
            }

            blockedCellCount = CountBlockedCells();
            gridDirty = false;
            revision++;

            if (logSummary && enableDebugLogging)
            {
                int totalCells = gridSize.x * gridSize.y;
                int walkableCells = totalCells - blockedCellCount;
                Debug.Log($"NavGridBuilder rebuilt {name}: {walkableCells} walkable / {blockedCellCount} blocked (tile size {tileSize:F2}).", this);
            }

            RaiseGridRebuilt();
        }

        private int CountBlockedCells()
        {
            if (!HasGrid)
            {
                return 0;
            }

            int blocked = 0;
            for (int y = 0; y < gridSize.y; y++)
            {
                for (int x = 0; x < gridSize.x; x++)
                {
                    if (!walkableGrid[x, y])
                    {
                        blocked++;
                    }
                }
            }

            return blocked;
        }

        private void RaiseGridRebuilt()
        {
            GridRebuilt?.Invoke(this);
            if (Application.isPlaying)
            {
                PathfindingService.Instance?.RegisterNavGrid(this);
            }
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
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        continue;
                    }

                    if (DoesObjectMatchTag(obj, tag))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Safely compares an object's tag while tolerating undefined tags in the Tag Manager.
        /// When a tag is missing we ignore it and emit a single warning so designers know to add it back if required.
        /// </summary>
        private bool DoesObjectMatchTag(GameObject obj, string tag)
        {
            if (obj == null || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            if (!IsTagRegistered(tag))
            {
                ReportMissingTag(tag);
                return false;
            }

            return string.Equals(obj.tag, tag, StringComparison.Ordinal);
        }

        /// <summary>
        /// Removes blank or undefined tag entries from the serialized blocking tag list so we do not keep
        /// resaving invalid data to prefabs or scenes.
        /// </summary>
        private void SanitizeBlockingTags()
        {
            if (blockingTags == null)
            {
                return;
            }

            for (int i = blockingTags.Count - 1; i >= 0; i--)
            {
                string tag = blockingTags[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    blockingTags.RemoveAt(i);
                    continue;
                }

                if (!IsTagRegistered(tag))
                {
                    ReportMissingTag(tag);
                    blockingTags.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the supplied tag exists in the Unity Tag Manager. Outside of the editor we
        /// optimistically assume serialized tags are valid because the build no longer has access to the editor API.
        /// </summary>
        private bool IsTagRegistered(string tag)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            string[] tags = InternalEditorUtility.tags;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == tag)
                {
                    return true;
                }
            }

            return false;
#else
            return !string.IsNullOrEmpty(tag);
#endif
        }

        /// <summary>
        /// Emits a single warning the first time we encounter a missing tag so designers receive actionable feedback
        /// without flooding the console when grids are rebuilt repeatedly.
        /// </summary>
        private void ReportMissingTag(string tag)
        {
            if (!reportedMissingTags.Add(tag))
            {
                return;
            }

#if UNITY_EDITOR
            Debug.LogWarning($"NavGridBuilder blocking tag \"{tag}\" is not defined in the Tag Manager and will be ignored.", this);
#endif
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
