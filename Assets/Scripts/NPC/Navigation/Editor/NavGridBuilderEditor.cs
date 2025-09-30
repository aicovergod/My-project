#if UNITY_EDITOR
using NPC;
using UnityEditor;
using UnityEngine;

namespace NPC.Navigation.Editor
{
    /// <summary>
    /// Custom inspector and scene view tools for <see cref="NavGridBuilder"/> allowing manual walkability painting.
    /// </summary>
    [CustomEditor(typeof(NavGridBuilder))]
    public sealed class NavGridBuilderEditor : UnityEditor.Editor
    {
        private enum PaintMode
        {
            PaintWalkable,
            PaintBlocked,
            ClearOverride
        }

        private static readonly Color WalkablePreviewColor = new Color(0f, 0.8f, 0.2f, 0.9f);
        private static readonly Color BlockedPreviewColor = new Color(0.85f, 0.1f, 0.1f, 0.9f);
        private static readonly Color ClearPreviewColor = new Color(0.95f, 0.8f, 0.1f, 0.9f);

        private NavGridBuilder navGridBuilder;
        private PaintMode paintMode = PaintMode.PaintWalkable;
        private bool paintingEnabled;
        private bool hoveredCellValid;
        private Vector2Int hoveredCell;
        private Vector3 hoveredCellCenter;

        private void OnEnable()
        {
            // Cache the strongly typed target so we can call helper methods without repeated casts.
            navGridBuilder = (NavGridBuilder)target;
            // Listen for scene view GUI events so we can draw hover feedback and process painting clicks.
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            // Always remove scene view listeners to avoid leaking delegates when recompiling scripts.
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        public override void OnInspectorGUI()
        {
            // Render the standard inspector first so designers can tweak grid settings normally.
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Manual Overrides", EditorStyles.boldLabel);

            // Toggle enables/disables painting mode entirely without affecting the grid configuration.
            bool paintingToggle = EditorGUILayout.ToggleLeft("Enable Painting Mode", paintingEnabled);
            if (paintingToggle != paintingEnabled)
            {
                paintingEnabled = paintingToggle;
                // Force the scene view to refresh immediately so hover gizmos appear/disappear with the toggle.
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(!paintingEnabled))
            {
                EditorGUILayout.LabelField("Paint Operation", EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Toggle(paintMode == PaintMode.PaintWalkable, "Paint Walkable", "Button"))
                    {
                        paintMode = PaintMode.PaintWalkable;
                    }

                    if (GUILayout.Toggle(paintMode == PaintMode.PaintBlocked, "Paint Blocked", "Button"))
                    {
                        paintMode = PaintMode.PaintBlocked;
                    }

                    if (GUILayout.Toggle(paintMode == PaintMode.ClearOverride, "Clear Override", "Button"))
                    {
                        paintMode = PaintMode.ClearOverride;
                    }
                }

                // Display the hovered cell so level designers know which coordinates will be affected before clicking.
                string hoverLabel = hoveredCellValid ? hoveredCell.ToString() : "None";
                EditorGUILayout.LabelField("Hovered Cell", hoverLabel);

                // Keep the inspector repainting so hover information stays up to date while painting is active.
                if (paintingEnabled)
                {
                    Repaint();
                    SceneView.RepaintAll();
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (navGridBuilder == null || !navGridBuilder.HasGrid)
            {
                hoveredCellValid = false;
                return;
            }

            Event guiEvent = Event.current;
            if (guiEvent == null)
            {
                return;
            }

            // Convert the current mouse position into a world-space hit on the grid plane.
            if (!TryGetMouseWorldPosition(guiEvent.mousePosition, out Vector3 worldPosition))
            {
                hoveredCellValid = false;
                return;
            }

            Vector2 world2D = new Vector2(worldPosition.x, worldPosition.y);
            hoveredCellValid = navGridBuilder.TryGetCell(world2D, out Vector2Int exactCell);
            hoveredCell = hoveredCellValid ? exactCell : navGridBuilder.WorldToCellClamped(world2D);
            Vector2 cellCenter = navGridBuilder.GetCellCenter(hoveredCell);
            hoveredCellCenter = new Vector3(cellCenter.x, cellCenter.y, navGridBuilder.transform.position.z);

            // Reserve control while painting so normal scene selection does not interfere with editing overrides.
            if (paintingEnabled && guiEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            DrawHoverPreview();
            HandlePainting(guiEvent);
        }

        private void DrawHoverPreview()
        {
            if (!paintingEnabled || !navGridBuilder.HasGrid || !hoveredCellValid)
            {
                return;
            }

            Color previewColor = paintMode switch
            {
                PaintMode.PaintWalkable => WalkablePreviewColor,
                PaintMode.PaintBlocked => BlockedPreviewColor,
                PaintMode.ClearOverride => ClearPreviewColor,
                _ => Color.white
            };

            // Shift the preview slightly forward on Z so it renders above the tile sprites in 2D mode.
            Vector3 previewCenter = hoveredCellCenter + navGridBuilder.transform.forward * 0.02f;
            Vector3 previewSize = new Vector3(navGridBuilder.TileSize, navGridBuilder.TileSize, navGridBuilder.TileSize * 0.1f);

            using (new Handles.DrawingScope(previewColor))
            {
                Handles.DrawWireCube(previewCenter, previewSize);
            }
        }

        private void HandlePainting(Event guiEvent)
        {
            if (!paintingEnabled || !hoveredCellValid)
            {
                return;
            }

            bool pressed = guiEvent.type == EventType.MouseDown && guiEvent.button == 0;
            bool dragging = guiEvent.type == EventType.MouseDrag && guiEvent.button == 0;
            if (!pressed && !dragging)
            {
                return;
            }

            bool changed = false;
            string undoLabel = paintMode switch
            {
                PaintMode.PaintWalkable => "Paint Walkable Cell",
                PaintMode.PaintBlocked => "Paint Blocked Cell",
                PaintMode.ClearOverride => "Clear Nav Override",
                _ => "Edit Nav Cell"
            };

            // Register the state change with Unity's undo stack so designers can revert mis-clicks instantly.
            Undo.RegisterCompleteObjectUndo(navGridBuilder, undoLabel);

            switch (paintMode)
            {
                case PaintMode.PaintWalkable:
                    changed = navGridBuilder.TrySetManualOverride(hoveredCell, true);
                    break;
                case PaintMode.PaintBlocked:
                    changed = navGridBuilder.TrySetManualOverride(hoveredCell, false);
                    break;
                case PaintMode.ClearOverride:
                    changed = navGridBuilder.ClearManualOverride(hoveredCell);
                    break;
            }

            if (changed)
            {
                // Mark the asset dirty so the manual override persists after the editor saves the scene.
                EditorUtility.SetDirty(navGridBuilder);
                SceneView.RepaintAll();
            }

            // Consume the mouse event so default selection behaviour does not trigger while painting.
            guiEvent.Use();
        }

        private bool TryGetMouseWorldPosition(Vector2 guiPosition, out Vector3 worldPosition)
        {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(guiPosition);
            Plane gridPlane = new Plane(navGridBuilder.transform.forward, navGridBuilder.transform.position);
            if (gridPlane.Raycast(worldRay, out float distance))
            {
                worldPosition = worldRay.GetPoint(distance);
                return true;
            }

            worldPosition = Vector3.zero;
            return false;
        }

    }
}
#endif
