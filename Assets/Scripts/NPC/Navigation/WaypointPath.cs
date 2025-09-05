using System.Collections.Generic;
using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Scene component that defines an ordered list of waypoints for NPCs.
    /// By default it uses this GameObject's children (in hierarchy order) as points,
    /// but you can switch to a manual list if you prefer.
    /// Supports per-waypoint waits and optional per-waypoint speed multipliers.
    /// </summary>
    [ExecuteAlways]
    public class WaypointPath : MonoBehaviour
    {
        [Header("Points Source")]
        [Tooltip("If true, children of this GameObject (in hierarchy order) are used as waypoints.")]
        public bool useChildrenAsWaypoints = true;

        [Tooltip("If not using children, define waypoints here (world Transforms).")]
        public List<Transform> waypoints = new List<Transform>();

        [Header("Traversal")]
        [Tooltip("If true, the path is visually/semantically a loop (first connects to last).")]
        public bool closedLoop = true;

        [Header("Per-Point Settings (optional)")]
        [Tooltip("Per-waypoint wait times (seconds). If shorter than point count, remaining default to 0.")]
        public List<float> waitTimes = new List<float>();

        [Tooltip("Per-waypoint speed multiplier (1 = base speed). If shorter than point count, remaining default to 1.")]
        public List<float> speedMultipliers = new List<float>();

        /// <summary>Number of points currently defined by this path.</summary>
        public int Count
        {
            get
            {
                if (useChildrenAsWaypoints) return transform.childCount;
                return waypoints?.Count ?? 0;
            }
        }

        /// <summary>Returns world position of point i.</summary>
        public Vector2 GetPoint(int i)
        {
            if (Count == 0) return (Vector2)transform.position;

            if (useChildrenAsWaypoints)
            {
                i = Mathf.Clamp(i, 0, transform.childCount - 1);
                return transform.GetChild(i).position;
            }

            i = Mathf.Clamp(i, 0, waypoints.Count - 1);
            Transform t = waypoints[i];
            return t ? (Vector2)t.position : (Vector2)transform.position;
        }

        /// <summary>Returns optional wait at point i.</summary>
        public float GetWait(int i)
        {
            if (waitTimes == null || waitTimes.Count == 0) return 0f;
            if (i < 0 || i >= Count) return 0f;
            if (i >= waitTimes.Count) return 0f;
            return Mathf.Max(0f, waitTimes[i]);
        }

        /// <summary>Returns optional speed multiplier for point i.</summary>
        public float GetSpeedMultiplier(int i)
        {
            if (speedMultipliers == null || speedMultipliers.Count == 0) return 1f;
            if (i < 0 || i >= Count) return 1f;
            if (i >= speedMultipliers.Count) return 1f;
            return Mathf.Max(0.0001f, speedMultipliers[i]);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            int n = Count;
            if (n <= 0) return;

            // Draw lines
            Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
            for (int i = 0; i < n - 1; i++)
            {
                Gizmos.DrawLine(GetPoint(i), GetPoint(i + 1));
            }
            if (closedLoop && n > 1)
            {
                Gizmos.DrawLine(GetPoint(n - 1), GetPoint(0));
            }

            // Draw points
            for (int i = 0; i < n; i++)
            {
                Vector3 p = GetPoint(i);
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(p, 0.07f);
            }
        }
#endif
    }
}
