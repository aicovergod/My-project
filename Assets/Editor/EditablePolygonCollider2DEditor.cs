using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PhysicsTools.TwoD;

/// <summary>
/// Custom inspector and Scene view tooling for <see cref="EditablePolygonCollider2D"/>.
/// Provides draggable handles, grid snapping, undo support, and polygon validation helpers.
/// </summary>
[CustomEditor(typeof(EditablePolygonCollider2D))]
public class EditablePolygonCollider2DEditor : UnityEditor.Editor
{
    private const float HandleScreenScale = 0.06f;
    private const float SelectedHandleScale = 1.35f;
    private const float OutlineThickness = 2.5f;
    private const float EdgeInsertScreenThreshold = 15f;

    private static readonly Color OutlineColor = new Color(0.1f, 0.6f, 1f, 0.9f);
    private static readonly Color OutlineInvalidColor = new Color(0.85f, 0.2f, 0.2f, 0.95f);
    private static readonly Color FillColor = new Color(0.1f, 0.6f, 1f, 0.05f);
    private static readonly Color SelectedHandleColor = new Color(1f, 0.85f, 0.2f, 1f);
    private static readonly Color HandleColor = new Color(0.1f, 0.9f, 0.3f, 1f);

    private SerializedProperty pointsProperty;
    private SerializedProperty showHandlesProperty;
    private SerializedProperty snapToGridProperty;
    private SerializedProperty snapSizeProperty;
    private SerializedProperty drawIndicesProperty;
    private SerializedProperty convexOnlyProperty;
    private SerializedProperty outputEdgeColliderProperty;

    private static int selectedIndex = -1;

    private enum ContextOperation
    {
        InsertBefore,
        InsertAfter,
        Remove,
        SnapToGrid
    }

    private sealed class ContextMenuData
    {
        public EditablePolygonCollider2DEditor Editor;
        public int Index;
        public Vector2 LocalPoint;
        public ContextOperation Operation;
    }

    private void OnEnable()
    {
        pointsProperty = serializedObject.FindProperty("points");
        showHandlesProperty = serializedObject.FindProperty("showHandles");
        snapToGridProperty = serializedObject.FindProperty("snapToGrid");
        snapSizeProperty = serializedObject.FindProperty("snapSize");
        drawIndicesProperty = serializedObject.FindProperty("drawIndices");
        convexOnlyProperty = serializedObject.FindProperty("convexOnly");
        outputEdgeColliderProperty = serializedObject.FindProperty("outputEdgeCollider");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditablePolygonCollider2D collider = (EditablePolygonCollider2D)target;

        EditorGUILayout.PropertyField(showHandlesProperty);
        EditorGUILayout.PropertyField(snapToGridProperty);
        EditorGUI.indentLevel++;
        using (new EditorGUI.DisabledScope(!snapToGridProperty.boolValue))
        {
            EditorGUILayout.PropertyField(snapSizeProperty);
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.PropertyField(drawIndicesProperty);
        EditorGUILayout.PropertyField(convexOnlyProperty);
        EditorGUILayout.PropertyField(outputEdgeColliderProperty, new GUIContent("Output Edge Collider"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Polygon Points", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(pointsProperty, true);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Collider → Points"))
            {
                ApplyUndo("Load Points From Collider");
                collider.FromCollider();
                serializedObject.Update();
            }

            if (GUILayout.Button("Points → Collider"))
            {
                ApplyUndo("Write Points To Collider");
                collider.ApplyToCollider();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Seed Rectangle"))
            {
                ApplyUndo("Seed Polygon Rectangle");
                collider.FromRectBounds();
                serializedObject.Update();
            }

            if (GUILayout.Button("Center & Normalize"))
            {
                ApplyUndo("Center Polygon");
                collider.CenterAndNormalize();
                serializedObject.Update();
            }
        }

        if (GUILayout.Button("Validate & Fix"))
        {
            ApplyUndo("Validate Polygon");
            collider.ValidateAndFix();
            serializedObject.Update();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Vertex Count", collider.PointCount.ToString());

        if (!collider.HasValidPoints)
        {
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(collider.LastValidationError)
                    ? "Polygon points are invalid. Adjust the vertices to create a valid polygon."
                    : collider.LastValidationError,
                MessageType.Error);
        }
        else if (collider.HasSelfIntersection)
        {
            EditorGUILayout.HelpBox("Polygon contains self intersections. Adjust vertices to produce a simple polygon.", MessageType.Warning);
        }

        serializedObject.ApplyModifiedProperties();
    }

    public void OnSceneGUI()
    {
        if (targets.Length != 1)
        {
            return;
        }

        EditablePolygonCollider2D collider = (EditablePolygonCollider2D)target;
        if (!collider.ShowHandles || collider.PointCount == 0)
        {
            return;
        }

        serializedObject.Update();

        Transform transform = collider.transform;
        Handles.color = collider.HasSelfIntersection ? OutlineInvalidColor : OutlineColor;

        Matrix4x4 previousMatrix = Handles.matrix;
        Handles.matrix = transform.localToWorldMatrix;

        DrawPolygonFill(collider);
        DrawPolygonOutline(collider);

        Event currentEvent = Event.current;
        bool repaintNeeded = false;

        if (currentEvent.shift && currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
        {
            if (TryInsertPointFromMouse(collider, currentEvent.mousePosition))
            {
                currentEvent.Use();
                serializedObject.Update();
                collider.NotifyPointsChanged();
                repaintNeeded = true;
            }
        }

        if ((currentEvent.type == EventType.KeyDown) && (currentEvent.keyCode == KeyCode.Delete || currentEvent.keyCode == KeyCode.Backspace))
        {
            if (selectedIndex >= 0 && selectedIndex < pointsProperty.arraySize && pointsProperty.arraySize > 3)
            {
                ApplyUndo("Remove Polygon Vertex");
                pointsProperty.DeleteArrayElementAtIndex(selectedIndex);
                serializedObject.ApplyModifiedProperties();
                collider.NotifyPointsChanged();
                selectedIndex = Mathf.Clamp(selectedIndex, 0, pointsProperty.arraySize - 1);
                currentEvent.Use();
                repaintNeeded = true;
            }
        }

        if (currentEvent.type == EventType.ContextClick)
        {
            Vector3 world;
            if (TryGetMouseWorld(transform, currentEvent.mousePosition, out world))
            {
                Vector2 local = (Vector2)transform.InverseTransformPoint(world);
                int index = FindNearestVertexIndex(collider, world);
                if (index >= 0)
                {
                    ShowContextMenu(local, index);
                    currentEvent.Use();
                }
            }
        }

        for (int i = 0; i < pointsProperty.arraySize; i++)
        {
            SerializedProperty element = pointsProperty.GetArrayElementAtIndex(i);
            Vector2 point = element.vector2Value;
            Vector3 localHandlePosition = new Vector3(point.x, point.y, 0f);
            Vector3 worldPoint = transform.localToWorldMatrix.MultiplyPoint3x4(localHandlePosition);
            float handleSize = HandleUtility.GetHandleSize(worldPoint) * HandleScreenScale;
            bool isSelected = selectedIndex == i;
            float displaySize = handleSize * (isSelected ? SelectedHandleScale : 1f);

            Handles.color = isSelected ? SelectedHandleColor : HandleColor;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUI.BeginChangeCheck();
            Vector3 movedLocal = Handles.FreeMoveHandle(controlId, localHandlePosition, Quaternion.identity, displaySize, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyUndo("Move Polygon Vertex");
                Vector2 newPoint = new Vector2(movedLocal.x, movedLocal.y);
                newPoint = collider.SnapPoint(newPoint);
                element.vector2Value = newPoint;
                serializedObject.ApplyModifiedProperties();
                collider.NotifyPointsChanged();
                selectedIndex = i;
                repaintNeeded = true;
            }

            if (GUIUtility.hotControl == controlId)
            {
                selectedIndex = i;
            }

            if (collider.DrawIndices)
            {
                GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = Color.white }
                };
                Handles.Label(localHandlePosition, i.ToString(), style);
            }
        }

        if (repaintNeeded)
        {
            SceneView.RepaintAll();
        }

        Handles.matrix = previousMatrix;
    }

    private void DrawPolygonOutline(EditablePolygonCollider2D collider)
    {
        int count = collider.PointCount;
        if (count < 2)
        {
            return;
        }

        Vector3[] positions = new Vector3[count + 1];
        for (int i = 0; i < count; i++)
        {
            Vector2 point = collider.GetPoint(i);
            positions[i] = new Vector3(point.x, point.y, 0f);
        }

        positions[count] = positions[0];
        Handles.DrawAAPolyLine(OutlineThickness, positions);
    }

    private void DrawPolygonFill(EditablePolygonCollider2D collider)
    {
        if (!collider.HasValidPoints || collider.HasSelfIntersection)
        {
            return;
        }

        int count = collider.PointCount;
        if (count < 3)
        {
            return;
        }

        Vector3[] positions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 point = collider.GetPoint(i);
            positions[i] = new Vector3(point.x, point.y, 0f);
        }

        Color previous = Handles.color;
        Handles.color = FillColor;
        Handles.DrawAAConvexPolygon(positions);
        Handles.color = previous;
    }

    private bool TryInsertPointFromMouse(EditablePolygonCollider2D collider, Vector2 mousePosition)
    {
        Transform transform = collider.transform;
        Vector3 worldPoint;
        if (!TryGetMouseWorld(transform, mousePosition, out worldPoint))
        {
            return false;
        }

        Vector2 localPoint = (Vector2)transform.InverseTransformPoint(worldPoint);
        int insertIndex = -1;
        Vector2 insertPoint = localPoint;
        float bestDistance = float.MaxValue;

        IReadOnlyList<Vector2> points = collider.Points;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 aWorld = transform.localToWorldMatrix.MultiplyPoint3x4(points[i]);
            Vector3 bWorld = transform.localToWorldMatrix.MultiplyPoint3x4(points[(i + 1) % points.Count]);
            Vector3 closestWorld = ClosestPointOnSegment(aWorld, bWorld, worldPoint);
            float screenDistance = Vector2.Distance(HandleUtility.WorldToGUIPoint(closestWorld), mousePosition);
            if (screenDistance < bestDistance)
            {
                bestDistance = screenDistance;
                insertIndex = i;
                insertPoint = (Vector2)transform.InverseTransformPoint(closestWorld);
            }
        }

        if (insertIndex < 0 || bestDistance > EdgeInsertScreenThreshold)
        {
            return false;
        }

        ApplyUndo("Insert Polygon Vertex");
        Vector2 snapped = collider.SnapPoint(insertPoint);
        int targetIndex = Mathf.Clamp(insertIndex + 1, 0, pointsProperty.arraySize);
        pointsProperty.InsertArrayElementAtIndex(targetIndex);
        SerializedProperty element = pointsProperty.GetArrayElementAtIndex(Mathf.Clamp(targetIndex, 0, pointsProperty.arraySize - 1));
        element.vector2Value = snapped;
        serializedObject.ApplyModifiedProperties();
        collider.NotifyPointsChanged();
        selectedIndex = Mathf.Clamp(targetIndex, 0, pointsProperty.arraySize - 1);
        return true;
    }

    private void ShowContextMenu(Vector2 localPoint, int index)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Insert Before"), false, OnContextMenuSelected, new ContextMenuData
        {
            Editor = this,
            Index = index,
            LocalPoint = localPoint,
            Operation = ContextOperation.InsertBefore
        });

        menu.AddItem(new GUIContent("Insert After"), false, OnContextMenuSelected, new ContextMenuData
        {
            Editor = this,
            Index = index,
            LocalPoint = localPoint,
            Operation = ContextOperation.InsertAfter
        });

        menu.AddItem(new GUIContent("Remove"), false, OnContextMenuSelected, new ContextMenuData
        {
            Editor = this,
            Index = index,
            LocalPoint = localPoint,
            Operation = ContextOperation.Remove
        });

        menu.AddItem(new GUIContent("Snap to Grid"), false, OnContextMenuSelected, new ContextMenuData
        {
            Editor = this,
            Index = index,
            LocalPoint = localPoint,
            Operation = ContextOperation.SnapToGrid
        });

        menu.ShowAsContext();
    }

    private static void OnContextMenuSelected(object context)
    {
        if (context is not ContextMenuData data || data.Editor == null)
        {
            return;
        }

        data.Editor.HandleContextOperation(data);
    }

    private void HandleContextOperation(ContextMenuData data)
    {
        EditablePolygonCollider2D collider = (EditablePolygonCollider2D)target;
        switch (data.Operation)
        {
            case ContextOperation.InsertBefore:
                InsertVertex(Mathf.Max(data.Index, 0), collider.SnapPoint(data.LocalPoint), collider);
                break;
            case ContextOperation.InsertAfter:
                InsertVertex(Mathf.Min(data.Index + 1, collider.PointCount), collider.SnapPoint(data.LocalPoint), collider);
                break;
            case ContextOperation.Remove:
                RemoveVertex(data.Index, collider);
                break;
            case ContextOperation.SnapToGrid:
                SnapVertex(data.Index, collider);
                break;
        }

        SceneView.RepaintAll();
    }

    private void InsertVertex(int insertIndex, Vector2 value, EditablePolygonCollider2D collider)
    {
        insertIndex = Mathf.Clamp(insertIndex, 0, pointsProperty.arraySize);
        ApplyUndo("Insert Polygon Vertex");
        pointsProperty.InsertArrayElementAtIndex(insertIndex);
        SerializedProperty element = pointsProperty.GetArrayElementAtIndex(Mathf.Clamp(insertIndex, 0, pointsProperty.arraySize - 1));
        element.vector2Value = value;
        serializedObject.ApplyModifiedProperties();
        collider.NotifyPointsChanged();
        selectedIndex = Mathf.Clamp(insertIndex, 0, pointsProperty.arraySize - 1);
    }

    private void RemoveVertex(int index, EditablePolygonCollider2D collider)
    {
        if (pointsProperty.arraySize <= 3)
        {
            return;
        }

        ApplyUndo("Remove Polygon Vertex");
        pointsProperty.DeleteArrayElementAtIndex(index);
        serializedObject.ApplyModifiedProperties();
        collider.NotifyPointsChanged();
        selectedIndex = Mathf.Clamp(index, 0, pointsProperty.arraySize - 1);
    }

    private void SnapVertex(int index, EditablePolygonCollider2D collider)
    {
        if (index < 0 || index >= pointsProperty.arraySize)
        {
            return;
        }

        SerializedProperty element = pointsProperty.GetArrayElementAtIndex(index);
        Vector2 point = element.vector2Value;
        Vector2 snapped = collider.SnapPoint(point);
        if (snapped == point)
        {
            return;
        }

        ApplyUndo("Snap Polygon Vertex");
        element.vector2Value = snapped;
        serializedObject.ApplyModifiedProperties();
        collider.NotifyPointsChanged();
    }

    private static bool TryGetMouseWorld(Transform transform, Vector2 mousePosition, out Vector3 worldPoint)
    {
        Plane plane = new Plane(transform.forward, transform.position);
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        if (plane.Raycast(ray, out float enter))
        {
            worldPoint = ray.GetPoint(enter);
            return true;
        }

        worldPoint = Vector3.zero;
        return false;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float lengthSquared = Vector3.Dot(ab, ab);
        if (lengthSquared <= Mathf.Epsilon)
        {
            return a;
        }

        float t = Vector3.Dot(point - a, ab) / lengthSquared;
        t = Mathf.Clamp01(t);
        return a + (ab * t);
    }

    private int FindNearestVertexIndex(EditablePolygonCollider2D collider, Vector3 worldPoint)
    {
        Transform transform = collider.transform;
        int count = collider.PointCount;
        float bestDistance = float.MaxValue;
        int bestIndex = -1;

        for (int i = 0; i < count; i++)
        {
            Vector3 worldVertex = transform.localToWorldMatrix.MultiplyPoint3x4(collider.GetPoint(i));
            float distance = Vector3.Distance(worldPoint, worldVertex);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void ApplyUndo(string actionName)
    {
        foreach (UnityEngine.Object obj in targets)
        {
            Undo.RecordObject(obj, actionName);
            if (obj is EditablePolygonCollider2D component)
            {
                Undo.RecordObject(component.transform, actionName);
                if (component.ActiveCollider != null)
                {
                    Undo.RecordObject(component.ActiveCollider, actionName);
                }
            }
        }
    }
}
