using System;
using System.Collections.Generic;
using UnityEngine;
using Util;
using World;
using Player;

namespace NPC.Navigation
{
    /// <summary>
    /// Streams navigation chunks around the player so the pathfinding service can operate on large worlds
    /// without keeping every baked grid resident simultaneously.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("NPC/Navigation/Nav Grid Streaming Service")]
    public sealed class NavGridStreamingService : ScenePersistentObject, ITickable
    {
        /// <summary>
        /// Raised whenever a chunk is loaded into memory.
        /// </summary>
        public event Action<Vector2Int> ChunkLoaded;

        /// <summary>
        /// Raised whenever a chunk is unloaded from memory.
        /// </summary>
        public event Action<Vector2Int> ChunkUnloaded;

        /// <summary>
        /// Raised when the aggregated navigation data changes.
        /// </summary>
        public event Action<INavGridData> NavDataChanged;

        /// <summary>
        /// Raised when a streaming zone requests activation.
        /// </summary>
        public event Action<string, IReadOnlyList<string>> ZoneActivated;

        /// <summary>
        /// Raised when a streaming zone requests deactivation.
        /// </summary>
        public event Action<string> ZoneDeactivated;

        /// <summary>
        /// Active singleton instance.
        /// </summary>
        public static NavGridStreamingService Instance { get; private set; }

        [Header("Streaming Window")]
        [Tooltip("Number of chunk steps to keep loaded around the player's active chunk.")]
        [SerializeField, Min(0)] private int chunkRadius = 1;

        [Tooltip("Chunk dimensions expressed in tiles. Must match the baker output.")]
        [SerializeField] private Vector2Int chunkDimensions = new Vector2Int(256, 256);

        [Tooltip("World units per tile. Should align with the NavGridBuilder that produced the chunks.")]
        [SerializeField] private float tileSize = 1f;

        [Tooltip("World-space origin corresponding to chunk (0,0).")]
        [SerializeField] private Vector2 worldOrigin;

        [Header("Resources")]
        [Tooltip("Resources folder containing NavGridChunkDefinition assets.")]
        [SerializeField] private string resourceFolder = "NavGridChunks";

        [Header("Debug")]
        [Tooltip("Writes verbose logging for chunk streaming operations.")]
        [SerializeField] private bool enableDebugLogging;

        private static bool globalEnableDebugLogging;

        /// <summary>
        /// Globally toggles verbose logging for the streaming service so tooling can
        /// flip the flag before an instance exists.
        /// </summary>
        public static bool EnableDebugLogging
        {
            get => Instance != null ? Instance.enableDebugLogging : globalEnableDebugLogging;
            set
            {
                globalEnableDebugLogging = value;
                if (Instance != null)
                {
                    Instance.enableDebugLogging = value;
                }
            }
        }

        [Tooltip("Layers considered blocking for streamed navigation data.")]
        [SerializeField] private LayerMask blockingLayerMask;

        private readonly Dictionary<Vector2Int, NavGridChunkDefinition> loadedChunks = new Dictionary<Vector2Int, NavGridChunkDefinition>();
        private readonly Dictionary<string, HashSet<Vector2Int>> activeZoneChunks = new Dictionary<string, HashSet<Vector2Int>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Vector2Int> desiredChunks = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> unloadBuffer = new List<Vector2Int>();
        private NavGridWorld worldData;

        private Transform playerTransform;
        private Vector2Int activePlayerChunk;
        private bool hasActiveChunk;
        private bool subscribedToTicker;
        private Coroutine tickerRoutine;

        /// <summary>
        /// Aggregated navigation data exposed to consumers.
        /// </summary>
        public INavGridData ActiveData => worldData;

        /// <summary>
        /// Dimensions of each chunk in tiles.
        /// </summary>
        public Vector2Int ChunkDimensions => chunkDimensions;

        /// <summary>
        /// Size of an individual tile in world units.
        /// </summary>
        public float TileSize => tileSize;

        /// <summary>
        /// Origin applied when mapping chunk coordinates to world space.
        /// </summary>
        public Vector2 WorldOrigin => worldOrigin;

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            base.Awake();
            Instance = this;

            if (globalEnableDebugLogging)
            {
                enableDebugLogging = true;
            }

            globalEnableDebugLogging = enableDebugLogging;

            worldData = new NavGridWorld(chunkDimensions, tileSize, worldOrigin, blockingLayerMask);
        }

        private void OnEnable()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            if (worldData == null)
            {
                worldData = new NavGridWorld(chunkDimensions, tileSize, worldOrigin, blockingLayerMask);
            }

            SubscribeToTicker();
            worldData.UpdateConfiguration(chunkDimensions, tileSize, worldOrigin);
            worldData.UpdateBlockingMask(blockingLayerMask);
        }

        private void OnDisable()
        {
            UnsubscribeFromTicker();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                UnsubscribeFromTicker();
                Instance = null;
            }
        }

        /// <inheritdoc />
        public void OnTick()
        {
            if (!EnsurePlayerTransform())
            {
                return;
            }

            Vector2 playerPosition = playerTransform.position;
            Vector2Int chunk = ResolveChunkFromWorld(playerPosition);
            if (!hasActiveChunk || chunk != activePlayerChunk)
            {
                activePlayerChunk = chunk;
                hasActiveChunk = true;
                Log($"Active chunk -> {activePlayerChunk}.");
            }

            RefreshStreamingTargets();
        }

        /// <summary>
        /// Notifies the service that a streaming zone became active.
        /// </summary>
        public void ActivateZone(string zoneId, IReadOnlyList<string> chunkIds)
        {
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                return;
            }

            var chunkSet = new HashSet<Vector2Int>();
            if (chunkIds != null)
            {
                for (int i = 0; i < chunkIds.Count; i++)
                {
                    if (NavGridChunkDefinition.TryParseChunkId(chunkIds[i], out var coords))
                    {
                        chunkSet.Add(coords);
                    }
                }
            }

            activeZoneChunks[zoneId] = chunkSet;
            ZoneActivated?.Invoke(zoneId, chunkIds ?? Array.Empty<string>());
            Log($"Zone '{zoneId}' activated with {chunkSet.Count} chunk(s).");
            RefreshStreamingTargets();
        }

        /// <summary>
        /// Notifies the service that a streaming zone was exited.
        /// </summary>
        public void DeactivateZone(string zoneId)
        {
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                return;
            }

            if (activeZoneChunks.Remove(zoneId))
            {
                ZoneDeactivated?.Invoke(zoneId);
                Log($"Zone '{zoneId}' deactivated.");
                RefreshStreamingTargets();
            }
        }

        /// <summary>
        /// Attempts to resolve the chunk currently backing a global cell.
        /// </summary>
        public bool TryResolveChunkForCell(Vector2Int cell, out Vector2Int chunk)
        {
            return worldData.TryResolveChunkForCell(cell, out chunk);
        }

        private bool EnsurePlayerTransform()
        {
            if (playerTransform != null)
            {
                return true;
            }

            if (PlayerLocator.TryFindPlayer(out var player))
            {
                playerTransform = player.transform;
                return true;
            }

            return false;
        }

        private Vector2Int ResolveChunkFromWorld(Vector2 worldPosition)
        {
            Vector2 relative = worldPosition - worldOrigin;
            float chunkWidth = Mathf.Max(1f, chunkDimensions.x) * Mathf.Max(0.0001f, tileSize);
            float chunkHeight = Mathf.Max(1f, chunkDimensions.y) * Mathf.Max(0.0001f, tileSize);
            int x = Mathf.FloorToInt(relative.x / chunkWidth);
            int y = Mathf.FloorToInt(relative.y / chunkHeight);
            return new Vector2Int(x, y);
        }

        private void RefreshStreamingTargets()
        {
            desiredChunks.Clear();

            foreach (var entry in activeZoneChunks)
            {
                foreach (var chunk in entry.Value)
                {
                    desiredChunks.Add(chunk);
                }
            }

            if (hasActiveChunk)
            {
                for (int dx = -chunkRadius; dx <= chunkRadius; dx++)
                {
                    for (int dy = -chunkRadius; dy <= chunkRadius; dy++)
                    {
                        desiredChunks.Add(new Vector2Int(activePlayerChunk.x + dx, activePlayerChunk.y + dy));
                    }
                }
            }

            foreach (Vector2Int coord in desiredChunks)
            {
                EnsureChunkLoaded(coord);
            }

            unloadBuffer.Clear();
            foreach (var entry in loadedChunks)
            {
                if (!desiredChunks.Contains(entry.Key))
                {
                    unloadBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < unloadBuffer.Count; i++)
            {
                UnloadChunk(unloadBuffer[i]);
            }

            unloadBuffer.Clear();
        }

        private void EnsureChunkLoaded(Vector2Int coordinates)
        {
            if (loadedChunks.ContainsKey(coordinates))
            {
                return;
            }

            string chunkId = NavGridChunkDefinition.ComposeChunkId(coordinates);
            string resourcePath = string.IsNullOrWhiteSpace(resourceFolder)
                ? chunkId
                : $"{resourceFolder.TrimEnd('/')}/{chunkId}";
            NavGridChunkDefinition chunk = Resources.Load<NavGridChunkDefinition>(resourcePath);
            if (chunk == null)
            {
                LogWarning($"Could not locate chunk asset '{resourcePath}'.");
                return;
            }

            loadedChunks[coordinates] = chunk;
            SynchroniseConfiguration(chunk);
            worldData.AddOrUpdateChunk(chunk);
            ChunkLoaded?.Invoke(coordinates);
            NavDataChanged?.Invoke(worldData);

            Log($"Loaded nav chunk {coordinates} from {resourcePath}.");
        }

        private void UnloadChunk(Vector2Int coordinates)
        {
            if (!loadedChunks.TryGetValue(coordinates, out var chunk))
            {
                return;
            }

            loadedChunks.Remove(coordinates);
            worldData.RemoveChunk(coordinates);
            ChunkUnloaded?.Invoke(coordinates);
            NavDataChanged?.Invoke(worldData);

            if (chunk != null)
            {
                Resources.UnloadAsset(chunk);
            }

            Log($"Unloaded nav chunk {coordinates}.");
        }

        private void SynchroniseConfiguration(NavGridChunkDefinition chunk)
        {
            if (chunk == null)
            {
                return;
            }

            if (chunk.TileSize > 0f)
            {
                tileSize = chunk.TileSize;
            }

            if (chunk.GridDimensions.x > 0 && chunk.GridDimensions.y > 0)
            {
                chunkDimensions = chunk.GridDimensions;
            }

            Vector2 expectedOrigin = chunk.ChunkOrigin - new Vector2(
                chunk.ChunkCoordinates.x * chunkDimensions.x * tileSize,
                chunk.ChunkCoordinates.y * chunkDimensions.y * tileSize);
            worldOrigin = expectedOrigin;
            worldData.UpdateConfiguration(chunkDimensions, tileSize, worldOrigin);
            worldData.UpdateBlockingMask(blockingLayerMask);
        }

        private void SubscribeToTicker()
        {
            if (subscribedToTicker)
            {
                return;
            }

            if (Ticker.Instance == null)
            {
                if (tickerRoutine == null && isActiveAndEnabled)
                {
                    tickerRoutine = StartCoroutine(WaitForTicker());
                }

                return;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
        }

        private void UnsubscribeFromTicker()
        {
            if (tickerRoutine != null)
            {
                StopCoroutine(tickerRoutine);
                tickerRoutine = null;
            }

            if (!subscribedToTicker)
            {
                return;
            }

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }

            subscribedToTicker = false;
        }

        private System.Collections.IEnumerator WaitForTicker()
        {
            while (Ticker.Instance == null)
            {
                yield return null;
            }

            tickerRoutine = null;

            if (!isActiveAndEnabled)
            {
                yield break;
            }

            Ticker.Instance.Subscribe(this);
            subscribedToTicker = true;
        }

        private void Log(string message)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.Log($"[NavGridStreaming] {message}", this);
        }

        private void LogWarning(string message)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.LogWarning($"[NavGridStreaming] {message}", this);
        }
    }
}
