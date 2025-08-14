// Assets/Scripts/Camera/CameraFollow2D.cs

using UnityEngine;

namespace Player
{
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : MonoBehaviour
    {
        public Transform target;
        public Vector2 offset = Vector2.zero;
        [Tooltip("0 = instant, higher = smoother (e.g., 0.12)")]
        public float smoothTime = 0.12f;

        [Header("Pixel Snapping")]
        public bool snapToPixels = true;
        public int pixelsPerUnit = 64;

        [Header("World Bounds (optional)")]
        public bool confineToBounds = false;
        public Rect worldBounds = new Rect(-100, -100, 200, 200);

        private Vector3 velocity;
        private Camera cam;

        void Awake() { cam = GetComponent<Camera>(); }

        void LateUpdate()
        {
            if (!target) return;

            // desired position (keep current Z)
            Vector3 desired = new Vector3(
                target.position.x + offset.x,
                target.position.y + offset.y,
                transform.position.z
            );

            // smooth follow
            Vector3 pos = (smoothTime <= 0f)
                ? desired
                : Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);

            // confine to world bounds (so camera never shows outside the map)
            if (confineToBounds)
            {
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;

                float minX = worldBounds.xMin + halfW;
                float maxX = worldBounds.xMax - halfW;
                float minY = worldBounds.yMin + halfH;
                float maxY = worldBounds.yMax - halfH;

                pos.x = Mathf.Clamp(pos.x, minX, maxX);
                pos.y = Mathf.Clamp(pos.y, minY, maxY);
            }

            // snap to pixel grid for razor-sharp sprites
            if (snapToPixels && pixelsPerUnit > 0)
            {
                float unitsPerPixel = 1f / pixelsPerUnit;
                pos.x = Mathf.Round(pos.x / unitsPerPixel) * unitsPerPixel;
                pos.y = Mathf.Round(pos.y / unitsPerPixel) * unitsPerPixel;
            }

            transform.position = pos;
        }
    }
}
