#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NPC.Navigation.Editor
{
    /// <summary>
    /// Editor window that bakes the active navigation grid into chunked <see cref="NavGridChunkDefinition"/> assets
    /// so navigation can be streamed at runtime without rebaking in play mode. The window can harvest data from an
    /// existing <see cref="NavGridBuilder"/> or create a temporary builder that aligns to tilemap bounds.
    /// </summary>
    public sealed class NavGridChunkBaker : EditorWindow
    {
        private const string DefaultOutputFolder = "Assets/Resources/NavGridChunks";
        private static readonly Vector2Int DefaultChunkDimensions = new Vector2Int(256, 256);
        private static readonly Vector2Int[] NeighbourOffsets =
        {
            new Vector2Int(-1, 0),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(0, 1),
            new Vector2Int(-1, -1),
            new Vector2Int(-1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(1, 1)
        };

        private static readonly Color PreviewFillColor = new Color(0f, 0.65f, 0.2f, 0.15f);
        private static readonly Color PreviewOutlineColor = new Color(0f, 0.55f, 0.2f, 0.8f);

        [Serializable]
        private struct ChunkPreview
        {
            public Rect Rect;
            public Vector2Int Coordinates;
        }

        private NavGridBuilder masterBuilder;
        private Tilemap tilemapSource;
        private Vector2Int chunkDimensions = DefaultChunkDimensions;
        private string outputFolder = DefaultOutputFolder;
        private bool visualizeChunks = true;
        private bool labelChunkCoordinates = true;
        private float previewElevation = 0.05f;
        private Vector2 scrollPosition;
        private string lastBakeSummary;
        private readonly List<string> generatedAssetPaths = new List<string>();
        private readonly List<ChunkPreview> chunkPreviews = new List<ChunkPreview>();
        private readonly List<Vector2Int> neighbourBuffer = new List<Vector2Int>();

        [MenuItem("NPC/Navigation/Nav Grid Chunk Baker")]
        public static void OpenWindow()
        {
            NavGridChunkBaker window = GetWindow<NavGridChunkBaker>(false, "Nav Grid Chunk Baker");
            window.minSize = new Vector2(420f, 320f);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Chunk Configuration", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                masterBuilder = (NavGridBuilder)EditorGUILayout.ObjectField(
                    new GUIContent("Master Builder", "Existing NavGridBuilder used for baking or copying configuration."),
                    masterBuilder,
                    typeof(NavGridBuilder),
                    true);

                tilemapSource = (Tilemap)EditorGUILayout.ObjectField(
                    new GUIContent("Tilemap Bounds", "Optional tilemap that defines the baking bounds."),
                    tilemapSource,
                    typeof(Tilemap),
                    true);

                chunkDimensions = EditorGUILayout.Vector2IntField(
                    new GUIContent("Chunk Dimensions", "Number of tiles per baked chunk."),
                    chunkDimensions);
                chunkDimensions.x = Mathf.Max(1, chunkDimensions.x);
                chunkDimensions.y = Mathf.Max(1, chunkDimensions.y);

                outputFolder = EditorGUILayout.TextField(
                    new GUIContent("Output Folder", "Destination folder for the baked chunk assets."),
                    outputFolder);

                visualizeChunks = EditorGUILayout.Toggle(
                    new GUIContent("Visualize Chunks", "Draw chunk bounds in the scene view after baking."),
                    visualizeChunks);

                if (visualizeChunks)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        labelChunkCoordinates = EditorGUILayout.Toggle(
                            new GUIContent("Label Coordinates", "Draw the chunk coordinate next to each preview rectangle."),
                            labelChunkCoordinates);
                        previewElevation = EditorGUILayout.FloatField(
                            new GUIContent("Preview Elevation", "Z-offset applied to preview gizmos to avoid z-fighting."),
                            previewElevation);
                    }
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = masterBuilder != null;
                if (GUILayout.Button("Bake Using Builder"))
                {
                    BakeFromBuilder();
                }

                GUI.enabled = tilemapSource != null;
                if (GUILayout.Button("Bake From Tilemap"))
                {
                    BakeFromTilemap();
                }

                GUI.enabled = true;
            }

            EditorGUILayout.Space();

            if (string.IsNullOrEmpty(outputFolder) || !outputFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox("Output folder must be inside the Assets directory so Unity can import the generated chunk assets.", MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(lastBakeSummary))
            {
                EditorGUILayout.HelpBox(lastBakeSummary, MessageType.Info);
            }

            EditorGUILayout.LabelField("Generated Assets", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
            {
                scrollPosition = scroll.scrollPosition;
                if (generatedAssetPaths.Count == 0)
                {
                    EditorGUILayout.LabelField("No assets baked yet.", EditorStyles.miniLabel);
                }
                else
                {
                    for (int i = 0; i < generatedAssetPaths.Count; i++)
                    {
                        EditorGUILayout.SelectableLabel(generatedAssetPaths[i], EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f));
                    }
                }
            }
        }

        private void BakeFromBuilder()
        {
            if (masterBuilder == null)
            {
                EditorUtility.DisplayDialog("Nav Grid Chunk Baker", "Assign a NavGridBuilder before baking.", "OK");
                return;
            }

            EnsureGridBuilt(masterBuilder);
            if (!masterBuilder.HasGrid)
            {
                EditorUtility.DisplayDialog("Nav Grid Chunk Baker", "The selected NavGridBuilder does not have a valid grid to bake.", "OK");
                return;
            }

            previewElevation = Mathf.Max(previewElevation, masterBuilder.transform.position.z + 0.05f);
            ProcessBuilder(masterBuilder);
        }

        private void BakeFromTilemap()
        {
            if (tilemapSource == null)
            {
                EditorUtility.DisplayDialog("Nav Grid Chunk Baker", "Assign a Tilemap before baking.", "OK");
                return;
            }

            NavGridBuilder template = masterBuilder != null ? masterBuilder : FindObjectOfType<NavGridBuilder>();
            GameObject tempRoot = new GameObject("NavGridChunkBaker_Temp");
            tempRoot.hideFlags = HideFlags.HideAndDontSave;
            NavGridBuilder builder = tempRoot.AddComponent<NavGridBuilder>();

            try
            {
                if (template != null)
                {
                    EditorUtility.CopySerialized(template, builder);
                }

                ConfigureBuilderForTilemap(builder, tilemapSource);
                EnsureGridBuilt(builder);
                if (!builder.HasGrid)
                {
                    EditorUtility.DisplayDialog("Nav Grid Chunk Baker", "Unable to bake tilemap bounds because the temporary builder did not produce a grid.", "OK");
                    return;
                }

                previewElevation = Mathf.Max(previewElevation, builder.transform.position.z + 0.05f);
                ProcessBuilder(builder);
            }
            finally
            {
                DestroyImmediate(tempRoot);
            }
        }

        private static void EnsureGridBuilt(NavGridBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            if (builder.NeedsRebuild)
            {
                builder.BuildGrid();
            }
        }

        private void ConfigureBuilderForTilemap(NavGridBuilder builder, Tilemap tilemap)
        {
            BoundsInt cellBounds = tilemap.cellBounds;
            Vector3 cellSize = tilemap.layoutGrid != null ? tilemap.layoutGrid.cellSize : Vector3.one;
            float tileSize = Mathf.Abs(cellSize.x) > 0f ? Mathf.Abs(cellSize.x) : 1f;
            Vector2 areaSize = new Vector2(Mathf.Max(1, cellBounds.size.x) * tileSize, Mathf.Max(1, cellBounds.size.y) * tileSize);
            Vector3Int min = cellBounds.min;
            Vector3 worldOrigin = tilemap.layoutGrid != null ? tilemap.layoutGrid.CellToWorld(min) : tilemap.CellToWorld(min);
            Vector2 center = new Vector2(worldOrigin.x + areaSize.x * 0.5f, worldOrigin.y + areaSize.y * 0.5f);

            builder.transform.position = new Vector3(center.x, center.y, builder.transform.position.z);

            SerializedObject serializedBuilder = new SerializedObject(builder);
            serializedBuilder.FindProperty("areaSize").vector2Value = areaSize;
            serializedBuilder.FindProperty("tileSize").floatValue = tileSize;
            serializedBuilder.ApplyModifiedPropertiesWithoutUndo();
        }

        private void ProcessBuilder(NavGridBuilder builder)
        {
            EnsureFolderExists(outputFolder);

            Vector2Int gridSize = builder.GridSize;
            Vector2 gridOrigin = builder.GridOrigin;
            float tileSize = builder.TileSize;

            if (gridSize.x <= 0 || gridSize.y <= 0)
            {
                EditorUtility.DisplayDialog("Nav Grid Chunk Baker", "Grid dimensions are zero – there is nothing to bake.", "OK");
                return;
            }

            chunkPreviews.Clear();
            generatedAssetPaths.Clear();

            Dictionary<Vector2Int, NavGridChunkDefinition> chunkLookup = new Dictionary<Vector2Int, NavGridChunkDefinition>();
            int totalChunks = 0;
            int totalWalkableCells = 0;

            Vector2Int chunkSize = chunkDimensions;
            int chunkCountX = Mathf.CeilToInt(gridSize.x / (float)chunkSize.x);
            int chunkCountY = Mathf.CeilToInt(gridSize.y / (float)chunkSize.y);

            for (int cy = 0; cy < chunkCountY; cy++)
            {
                for (int cx = 0; cx < chunkCountX; cx++)
                {
                    Vector2Int chunkCoord = new Vector2Int(cx, cy);
                    Vector2Int startCell = new Vector2Int(cx * chunkSize.x, cy * chunkSize.y);
                    Vector2Int localDimensions = new Vector2Int(
                        Mathf.Min(chunkSize.x, gridSize.x - startCell.x),
                        Mathf.Min(chunkSize.y, gridSize.y - startCell.y));

                    bool[,] walkable = ExtractChunkWalkable(builder, startCell, localDimensions, out int walkableCellsInChunk);
                    totalWalkableCells += walkableCellsInChunk;

                    Vector2 origin = new Vector2(
                        gridOrigin.x + startCell.x * tileSize,
                        gridOrigin.y + startCell.y * tileSize);

                    string chunkId = NavGridChunkDefinition.ComposeChunkId(chunkCoord);
                    string assetPath = ComposeChunkAssetPath(chunkId);
                    NavGridChunkDefinition chunkAsset = LoadOrCreateChunkAsset(assetPath, chunkId);
                    chunkAsset.ApplyBakedData(chunkCoord, origin, tileSize, localDimensions, walkable);
                    chunkLookup[chunkCoord] = chunkAsset;
                    EditorUtility.SetDirty(chunkAsset);
                    generatedAssetPaths.Add(assetPath);

                    chunkPreviews.Add(new ChunkPreview
                    {
                        Coordinates = chunkCoord,
                        Rect = new Rect(origin, new Vector2(localDimensions.x * tileSize, localDimensions.y * tileSize))
                    });

                    totalChunks++;
                }
            }

            ApplyNeighbourMetadata(chunkLookup);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            lastBakeSummary = $"Baked {totalChunks} chunks ({gridSize.x}×{gridSize.y} tiles, {totalWalkableCells} walkable) to {outputFolder}.";
            Repaint();
            SceneView.RepaintAll();
        }

        private bool[,] ExtractChunkWalkable(NavGridBuilder builder, Vector2Int startCell, Vector2Int size, out int walkableCount)
        {
            bool[,] chunkData = new bool[size.x, size.y];
            walkableCount = 0;

            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int cell = new Vector2Int(startCell.x + x, startCell.y + y);
                    bool walkable = builder.IsCellWalkable(cell);
                    chunkData[x, y] = walkable;
                    if (walkable)
                    {
                        walkableCount++;
                    }
                }
            }

            return chunkData;
        }

        private NavGridChunkDefinition LoadOrCreateChunkAsset(string assetPath, string chunkId)
        {
            NavGridChunkDefinition chunk = AssetDatabase.LoadAssetAtPath<NavGridChunkDefinition>(assetPath);
            if (chunk != null)
            {
                return chunk;
            }

            chunk = ScriptableObject.CreateInstance<NavGridChunkDefinition>();
            chunk.name = chunkId;
            AssetDatabase.CreateAsset(chunk, assetPath);
            return chunk;
        }

        private string ComposeChunkAssetPath(string chunkId)
        {
            string sanitizedFolder = outputFolder.Replace('\\', '/').TrimEnd('/');
            return $"{sanitizedFolder}/{chunkId}.asset";
        }

        private void ApplyNeighbourMetadata(Dictionary<Vector2Int, NavGridChunkDefinition> chunkLookup)
        {
            foreach (KeyValuePair<Vector2Int, NavGridChunkDefinition> kvp in chunkLookup)
            {
                neighbourBuffer.Clear();
                for (int i = 0; i < NeighbourOffsets.Length; i++)
                {
                    Vector2Int neighbourCoord = kvp.Key + NeighbourOffsets[i];
                    if (chunkLookup.ContainsKey(neighbourCoord))
                    {
                        neighbourBuffer.Add(neighbourCoord);
                    }
                }

                kvp.Value.ApplyNeighbourMetadata(neighbourBuffer);
                EditorUtility.SetDirty(kvp.Value);
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!visualizeChunks || chunkPreviews.Count == 0)
            {
                return;
            }

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            for (int i = 0; i < chunkPreviews.Count; i++)
            {
                ChunkPreview preview = chunkPreviews[i];
                Vector3[] verts =
                {
                    new Vector3(preview.Rect.xMin, preview.Rect.yMin, previewElevation),
                    new Vector3(preview.Rect.xMax, preview.Rect.yMin, previewElevation),
                    new Vector3(preview.Rect.xMax, preview.Rect.yMax, previewElevation),
                    new Vector3(preview.Rect.xMin, preview.Rect.yMax, previewElevation)
                };

                Handles.DrawSolidRectangleWithOutline(verts, PreviewFillColor, PreviewOutlineColor);
                if (labelChunkCoordinates)
                {
                    Vector3 labelPos = new Vector3(preview.Rect.center.x, preview.Rect.center.y, previewElevation);
                    Handles.Label(labelPos, preview.Coordinates.ToString());
                }
            }
        }

        private void EnsureFolderExists(string folder)
        {
            string sanitized = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(sanitized))
            {
                return;
            }

            string parent = Path.GetDirectoryName(sanitized);
            string folderName = Path.GetFileName(sanitized);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException($"Cannot create folder outside the Assets directory: {sanitized}");
            }

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }

            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
#endif
