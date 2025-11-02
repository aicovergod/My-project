using System.Collections.Generic;
using UnityEngine;

namespace PhysicsTools
{
    /// <summary>
    /// Editable polygon collider wrapper that exposes a serialized point list and
    /// coordinates syncing the data back to a <see cref="PolygonCollider2D"/> (or optional <see cref="EdgeCollider2D"/>).
    /// The component is intended for OSRS-style 2D projects that expect 1 unit = 1 tile (64×64px art) and supports
    /// in-editor vertex manipulation through the companion custom editor.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PolygonCollider2D))]
    public sealed class EditablePolygonCollider2D : MonoBehaviour
    {
        private const float DuplicatePointEpsilon = 0.0001f;
        private const float CollinearEpsilon = 0.00001f;

        [SerializeField]
        [Tooltip("Ordered polygon vertices stored in local space.")]
        private List<Vector2> points = new List<Vector2>
        {
            new Vector2(-0.5f, -0.5f),
            new Vector2(0.5f, -0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-0.5f, 0.5f)
        };

        [SerializeField] private bool showHandles = true;
        [SerializeField] private bool snapToGrid = true;
        [SerializeField] private float snapSize = 0.125f;
        [SerializeField] private bool drawIndices = true;
        [SerializeField] private bool convexOnly = false;
        [SerializeField, Tooltip("If enabled the polygon points will be written to an EdgeCollider2D instead of a PolygonCollider2D.")]
        private bool outputEdgeCollider = false;

        private PolygonCollider2D polygonCollider;
        private EdgeCollider2D edgeCollider;

        private bool hasValidPoints = true;
        private bool hasSelfIntersection;
        private string lastValidationError = string.Empty;

        /// <summary>
        /// Provides a read-only snapshot of the current polygon vertices.
        /// </summary>
        public IReadOnlyList<Vector2> Points => points;

        /// <summary>
        /// Gets the active collider currently driven by this component (PolygonCollider2D or EdgeCollider2D).
        /// </summary>
        public Collider2D ActiveCollider => outputEdgeCollider ? (Collider2D)edgeCollider : polygonCollider;

        public bool ShowHandles => showHandles;
        public bool SnapToGrid => snapToGrid;
        public float SnapSize => Mathf.Max(0.0001f, snapSize);
        public bool DrawIndices => drawIndices;
        public bool ConvexOnly => convexOnly;
        public bool OutputEdgeCollider
        {
            get => outputEdgeCollider;
            set
            {
                if (outputEdgeCollider == value)
                {
                    return;
                }

                outputEdgeCollider = value;
                EnsureColliderSetup();
                ApplyToCollider();
            }
        }

        public bool HasValidPoints => hasValidPoints;
        public bool HasSelfIntersection => hasSelfIntersection;
        public string LastValidationError => lastValidationError;

        private void Reset()
        {
            EnsureColliderSetup();
            FromCollider();
        }

        private void Awake()
        {
            EnsureColliderSetup();
            ApplyToCollider();
        }

        private void OnEnable()
        {
            EnsureColliderSetup();
        }

        private void OnValidate()
        {
            snapSize = Mathf.Max(0.0001f, snapSize);
            EnsureColliderSetup();
            ValidateSerializedPoints();
            ApplyToCollider();
        }

        /// <summary>
        /// Applies the serialized point list to the active collider when the polygon is valid.
        /// </summary>
        public bool ApplyToCollider()
        {
            EnsureColliderSetup();

            if (points == null)
            {
                points = new List<Vector2>();
            }

            var sanitized = SanitizePoints(points);
            if (convexOnly)
            {
                sanitized = ReorderConvex(sanitized);
            }

            if (!ValidatePolygon(sanitized, out string error))
            {
                hasValidPoints = false;
                lastValidationError = error;
                return false;
            }

            hasValidPoints = true;
            lastValidationError = string.Empty;

            // Persist sanitized data back to the serialized list to keep inspector & undo in sync.
            OverwriteSerializedPoints(sanitized);

            // Write to collider only if the polygon is valid.
            WriteColliderPoints(sanitized);
            return true;
        }

        /// <summary>
        /// Reorders the polygon vertices to form a convex hull (monotone chain) when convex-only mode is enabled.
        /// </summary>
        public List<Vector2> ReorderConvex(List<Vector2> source)
        {
            if (source == null)
            {
                return new List<Vector2>();
            }

            if (source.Count <= 3)
            {
                return new List<Vector2>(source);
            }

            // Monotone chain convex hull.
            List<Vector2> sorted = new List<Vector2>(source);
            sorted.Sort((a, b) =>
            {
                int compareX = a.x.CompareTo(b.x);
                return compareX != 0 ? compareX : a.y.CompareTo(b.y);
            });

            List<Vector2> hull = new List<Vector2>();

            foreach (Vector2 point in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(point);
            }

            int lowerCount = hull.Count;
            for (int i = sorted.Count - 2; i >= 0; i--)
            {
                Vector2 point = sorted[i];
                while (hull.Count > lowerCount && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(point);
            }

            if (hull.Count > 1)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            return hull;
        }

        /// <summary>
        /// Populates the serialized points list from the currently attached collider path.
        /// </summary>
        public void FromCollider()
        {
            EnsureColliderSetup();

            if (outputEdgeCollider && edgeCollider != null)
            {
                var colliderPoints = edgeCollider.points;
                points = new List<Vector2>(colliderPoints.Length);
                foreach (Vector2 point in colliderPoints)
                {
                    points.Add(point);
                }

                RemoveTerminalDuplicate();
            }
            else if (polygonCollider != null)
            {
                if (polygonCollider.pathCount == 0)
                {
                    polygonCollider.pathCount = 1;
                }

                Vector2[] path = polygonCollider.GetPath(0);
                points = new List<Vector2>(path.Length);
                points.AddRange(path);
            }
            else
            {
                points ??= new List<Vector2>();
            }

            ValidateSerializedPoints();
            ApplyToCollider();
        }

        /// <summary>
        /// Seeds a rectangular polygon around the bounds of a SpriteRenderer or RectTransform if available.
        /// </summary>
        public void FromRectBounds()
        {
            Bounds bounds;
            bool foundBounds = false;

            SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                bounds = spriteRenderer.bounds;
                foundBounds = true;
            }
            else if (TryGetComponent(out RectTransform rectTransform))
            {
                Rect rect = rectTransform.rect;
                Vector3 center = rectTransform.TransformPoint(rect.center);
                bounds = new Bounds(center, new Vector3(rect.width * rectTransform.lossyScale.x, rect.height * rectTransform.lossyScale.y, 1f));
                foundBounds = true;
            }
            else if (TryGetComponent(out Renderer renderer))
            {
                bounds = renderer.bounds;
                foundBounds = true;
            }
            else
            {
                bounds = new Bounds(transform.position, Vector3.one);
            }

            Vector3 minWorld = bounds.min;
            Vector3 maxWorld = bounds.max;

            Vector3 bottomLeft = new Vector3(minWorld.x, minWorld.y, transform.position.z);
            Vector3 bottomRight = new Vector3(maxWorld.x, minWorld.y, transform.position.z);
            Vector3 topRight = new Vector3(maxWorld.x, maxWorld.y, transform.position.z);
            Vector3 topLeft = new Vector3(minWorld.x, maxWorld.y, transform.position.z);

            Vector2 localBL = transform.InverseTransformPoint(bottomLeft);
            Vector2 localBR = transform.InverseTransformPoint(bottomRight);
            Vector2 localTR = transform.InverseTransformPoint(topRight);
            Vector2 localTL = transform.InverseTransformPoint(topLeft);

            points = new List<Vector2> { localBL, localBR, localTR, localTL };

            ApplyToCollider();
        }

        /// <summary>
        /// Recenters the polygon around the local origin without changing the world-space collider shape.
        /// </summary>
        public void CenterAndNormalize()
        {
            if (points == null || points.Count == 0)
            {
                return;
            }

            Vector2 centroid = ComputeCentroid(points);
            if (centroid == Vector2.zero)
            {
                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                points[i] -= centroid;
            }

            Vector3 worldOffset = transform.TransformVector(centroid);
            transform.position += worldOffset;

            ApplyToCollider();
        }

        /// <summary>
        /// Attempts to sanitize and repair the polygon (dedupe, remove collinear, enforce winding, etc.).
        /// </summary>
        public bool ValidateAndFix()
        {
            var sanitized = SanitizePoints(points);
            if (convexOnly)
            {
                sanitized = ReorderConvex(sanitized);
            }

            if (!ValidatePolygon(sanitized, out string error))
            {
                hasValidPoints = false;
                lastValidationError = error;
                return false;
            }

            hasValidPoints = true;
            lastValidationError = string.Empty;
            OverwriteSerializedPoints(sanitized);
            WriteColliderPoints(sanitized);
            return true;
        }

        /// <summary>
        /// Forces the polygon data to reapply to the collider when the serialized list is modified externally.
        /// </summary>
        public void NotifyPointsChanged()
        {
            ApplyToCollider();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                if (ActiveCollider != null)
                {
                    UnityEditor.EditorUtility.SetDirty(ActiveCollider);
                }
            }
#endif
        }

        /// <summary>
        /// Snaps a point to the configured snap grid if snapping is enabled.
        /// </summary>
        public Vector2 SnapPoint(Vector2 point)
        {
            if (!snapToGrid)
            {
                return point;
            }

            float size = SnapSize;
            return new Vector2(
                Mathf.Round(point.x / size) * size,
                Mathf.Round(point.y / size) * size);
        }

        public int PointCount => points?.Count ?? 0;

        public Vector2 GetPoint(int index)
        {
            return points[index];
        }

        private void ValidateSerializedPoints()
        {
            if (points == null)
            {
                points = new List<Vector2>();
            }

            for (int i = points.Count - 1; i >= 0; i--)
            {
                Vector2 point = points[i];
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y))
                {
                    points.RemoveAt(i);
                }
            }
        }

        private void EnsureColliderSetup()
        {
            if (outputEdgeCollider)
            {
                if (polygonCollider != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(polygonCollider);
                    }
                    else
                    {
                        DestroyImmediate(polygonCollider);
                    }

                    polygonCollider = null;
                }

                if (edgeCollider == null)
                {
                    edgeCollider = GetComponent<EdgeCollider2D>();
                    if (edgeCollider == null)
                    {
                        edgeCollider = gameObject.AddComponent<EdgeCollider2D>();
                    }
                }
            }
            else
            {
                if (edgeCollider != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(edgeCollider);
                    }
                    else
                    {
                        DestroyImmediate(edgeCollider);
                    }

                    edgeCollider = null;
                }

                if (polygonCollider == null)
                {
                    polygonCollider = GetComponent<PolygonCollider2D>();
                    if (polygonCollider == null)
                    {
                        polygonCollider = gameObject.AddComponent<PolygonCollider2D>();
                    }
                }
            }
        }

        private List<Vector2> SanitizePoints(List<Vector2> source)
        {
            List<Vector2> sanitized = new List<Vector2>(source.Count);

            foreach (Vector2 point in source)
            {
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y))
                {
                    continue;
                }

                bool duplicate = false;
                for (int i = 0; i < sanitized.Count; i++)
                {
                    if ((sanitized[i] - point).sqrMagnitude < DuplicatePointEpsilon)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    sanitized.Add(point);
                }
            }

            if (sanitized.Count == 0)
            {
                sanitized.AddRange(DefaultRectangle());
            }

            Collider2DUtils.RemoveCollinear(sanitized, CollinearEpsilon);

            float area = SignedArea(sanitized);
            if (area < 0f)
            {
                sanitized.Reverse();
            }

            return sanitized;
        }

        private bool ValidatePolygon(List<Vector2> polygon, out string error)
        {
            if (polygon.Count < 3)
            {
                error = "Polygon requires at least three unique points.";
                hasSelfIntersection = false;
                return false;
            }

            if (Mathf.Abs(SignedArea(polygon)) < CollinearEpsilon)
            {
                error = "Polygon area is too small.";
                hasSelfIntersection = false;
                return false;
            }

            hasSelfIntersection = Collider2DUtils.HasSelfIntersections(polygon);
            if (hasSelfIntersection)
            {
                error = "Polygon contains self intersections.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void WriteColliderPoints(List<Vector2> polygon)
        {
            if (outputEdgeCollider)
            {
                if (edgeCollider == null)
                {
                    return;
                }

                List<Vector2> closed = new List<Vector2>(polygon.Count + 1);
                closed.AddRange(polygon);
                if (polygon.Count > 0)
                {
                    closed.Add(polygon[0]);
                }

                edgeCollider.points = closed.ToArray();
            }
            else if (polygonCollider != null)
            {
                polygonCollider.pathCount = 1;
                polygonCollider.SetPath(0, polygon.ToArray());
            }
        }

        private void OverwriteSerializedPoints(List<Vector2> sanitized)
        {
            points.Clear();
            points.AddRange(sanitized);
        }

        private void RemoveTerminalDuplicate()
        {
            if (points.Count < 2)
            {
                return;
            }

            if ((points[0] - points[points.Count - 1]).sqrMagnitude < DuplicatePointEpsilon)
            {
                points.RemoveAt(points.Count - 1);
            }
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private static float SignedArea(IList<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                area += (a.x * b.y) - (b.x * a.y);
            }

            return area * 0.5f;
        }

        private static Vector2 ComputeCentroid(IList<Vector2> polygon)
        {
            float area = 0f;
            float cx = 0f;
            float cy = 0f;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                float cross = (current.x * next.y) - (next.x * current.y);
                area += cross;
                cx += (current.x + next.x) * cross;
                cy += (current.y + next.y) * cross;
            }

            area *= 0.5f;
            if (Mathf.Abs(area) < 0.00001f)
            {
                return Vector2.zero;
            }

            float factor = 1f / (6f * area);
            return new Vector2(cx * factor, cy * factor);
        }

        private static IEnumerable<Vector2> DefaultRectangle()
        {
            yield return new Vector2(-0.5f, -0.5f);
            yield return new Vector2(0.5f, -0.5f);
            yield return new Vector2(0.5f, 0.5f);
            yield return new Vector2(-0.5f, 0.5f);
        }
    }

    /// <summary>
    /// Collider utility helpers used for validation.
    /// </summary>
    public static class Collider2DUtils
    {
        /// <summary>
        /// Tests a polygon for self-intersections (excluding consecutive edges sharing a vertex).
        /// </summary>
        public static bool HasSelfIntersections(IList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            int count = polygon.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 a1 = polygon[i];
                Vector2 a2 = polygon[(i + 1) % count];

                for (int j = i + 1; j < count; j++)
                {
                    int jNext = (j + 1) % count;
                    if (i == j || (i + 1) % count == j || i == jNext)
                    {
                        continue;
                    }

                    Vector2 b1 = polygon[j];
                    Vector2 b2 = polygon[jNext];

                    if (SegmentsIntersect(a1, a2, b1, b2))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Removes collinear points from the polygon in-place using the provided epsilon for cross-product comparison.
        /// </summary>
        public static void RemoveCollinear(IList<Vector2> polygon, float epsilon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            for (int i = polygon.Count - 1; i >= 0; i--)
            {
                Vector2 prev = polygon[(i - 1 + polygon.Count) % polygon.Count];
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];

                float cross = (current.x - prev.x) * (next.y - current.y) - (current.y - prev.y) * (next.x - current.x);
                if (Mathf.Abs(cross) <= epsilon)
                {
                    polygon.RemoveAt(i);

                    if (polygon.Count < 3)
                    {
                        break;
                    }
                }
            }
        }

        private static bool SegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            float d1 = Direction(b1, b2, a1);
            float d2 = Direction(b1, b2, a2);
            float d3 = Direction(a1, a2, b1);
            float d4 = Direction(a1, a2, b2);

            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            if (Mathf.Approximately(d1, 0f) && OnSegment(b1, b2, a1))
            {
                return true;
            }

            if (Mathf.Approximately(d2, 0f) && OnSegment(b1, b2, a2))
            {
                return true;
            }

            if (Mathf.Approximately(d3, 0f) && OnSegment(a1, a2, b1))
            {
                return true;
            }

            if (Mathf.Approximately(d4, 0f) && OnSegment(a1, a2, b2))
            {
                return true;
            }

            return false;
        }

        private static float Direction(Vector2 a, Vector2 b, Vector2 c)
        {
            return (c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x);
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 c)
        {
            return c.x >= Mathf.Min(a.x, b.x) - Mathf.Epsilon && c.x <= Mathf.Max(a.x, b.x) + Mathf.Epsilon &&
                   c.y >= Mathf.Min(a.y, b.y) - Mathf.Epsilon && c.y <= Mathf.Max(a.y, b.y) + Mathf.Epsilon;
        }
    }
}
