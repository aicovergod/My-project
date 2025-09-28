// Assets/Scripts/Camera/CameraFollow2D.cs

using Core.Input;
using UnityEngine;
using World;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Player
{
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : ScenePersistentObject
    {
        public Transform target;
        public Vector2 offset = Vector2.zero;
        [Tooltip("0 = instant, higher = smoother (e.g., 0.12)")]
        public float smoothTime = 0.12f;

        [Header("Zoom")]
        [Tooltip("Orthographic size applied when the camera starts. 0 keeps the current camera size.")]
        public float defaultZoom = 25f;
        [Tooltip("Minimum orthographic size allowed when zooming in.")]
        public float minZoom = 10f;
        [Tooltip("Maximum orthographic size allowed when zooming out.")]
        public float maxZoom = 40f;
        [Tooltip("How much the target zoom changes per scroll tick or controller input.")]
        public float zoomStep = 2f;
        [Tooltip("Interpolation speed when smoothing zoom transitions. 0 applies the target instantly.")]
        public float zoomSmoothing = 12f;

#if ENABLE_INPUT_SYSTEM
        [Header("Input")]
        [Tooltip("Optional PlayerInput override used to locate the ZoomCamera action.")]
        [SerializeField] private PlayerInput playerInput;
        [Tooltip("Input action reference representing the ZoomCamera control.")]
        [SerializeField] private InputActionReference zoomActionReference;
#endif

        [Header("Pixel Snapping")]
        public bool snapToPixels = true;
        public int pixelsPerUnit = 64;

        [Header("World Bounds (optional)")]
        public bool confineToBounds = false;
        public Rect worldBounds = new Rect(-100, -100, 200, 200);

        private Vector3 velocity;
        private Camera cam;
        private float targetZoom;
#if ENABLE_INPUT_SYSTEM
        private InputAction zoomAction;
        private bool zoomActionEnabledByResolver;
#endif

        protected override void Awake()
        {
            base.Awake();
            cam = GetComponent<Camera>();
            InitialiseZoomTargets();
        }

        private void OnEnable()
        {
            SceneTransitionManager.RegisterPersistentObject(this);
#if ENABLE_INPUT_SYSTEM
            zoomAction = InputActionResolver.Resolve(playerInput, zoomActionReference, "ZoomCamera", out zoomActionEnabledByResolver);
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (zoomAction != null && zoomActionEnabledByResolver)
            {
                zoomAction.Disable();
            }

            zoomAction = null;
            zoomActionEnabledByResolver = false;
#endif
            SceneTransitionManager.UnregisterPersistentObject(this);
        }

        void LateUpdate()
        {
            UpdateZoom();

            if (!target)
            {
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj) target = playerObj.transform;
                else return;
            }

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

        /// <summary>
        ///     Configures the starting zoom so the camera respects configured limits right away.
        /// </summary>
        private void InitialiseZoomTargets()
        {
            if (cam == null)
                return;

            GetOrderedZoomBounds(out float lowerBound, out float upperBound);

            float initialSize = cam.orthographicSize;

            if (defaultZoom > 0f)
                initialSize = defaultZoom;

            targetZoom = Mathf.Clamp(initialSize, lowerBound, upperBound);
            cam.orthographicSize = targetZoom;
        }

        /// <summary>
        ///     Reads zoom input, honours minimap focus rules, and applies smoothed zoom changes.
        /// </summary>
        private void UpdateZoom()
        {
            if (cam == null)
                return;

            float scrollDelta = ReadZoomInput();

            if (!Mathf.Approximately(scrollDelta, 0f))
            {
                bool minimapBlocksZoom = false;
                var minimap = Minimap.Instance;

                if (minimap != null)
                {
                    Vector2 pointerPosition = InputActionResolver.GetPointerScreenPosition(Vector2.zero);
                    minimapBlocksZoom = minimap.ShouldBlockWorldCameraZoom(pointerPosition);
                }

                if (!minimapBlocksZoom)
                {
                    GetOrderedZoomBounds(out float lowerBound, out float upperBound);
                    float newTarget = targetZoom - scrollDelta * Mathf.Abs(zoomStep);
                    targetZoom = Mathf.Clamp(newTarget, lowerBound, upperBound);
                }
            }

            ApplyZoomSmoothing();
        }

        /// <summary>
        ///     Applies smoothing so the camera interpolates toward the target zoom gracefully.
        /// </summary>
        private void ApplyZoomSmoothing()
        {
            GetOrderedZoomBounds(out float lowerBound, out float upperBound);
            targetZoom = Mathf.Clamp(targetZoom, lowerBound, upperBound);

            if (zoomSmoothing <= 0f)
            {
                cam.orthographicSize = targetZoom;
                return;
            }

            float t = Mathf.Clamp01(zoomSmoothing * Time.deltaTime);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, t);
        }

        /// <summary>
        ///     Returns the configured zoom bounds in ascending order so clamps behave predictably even if
        ///     the inspector values were supplied in reverse.
        /// </summary>
        private void GetOrderedZoomBounds(out float lowerBound, out float upperBound)
        {
            if (minZoom <= maxZoom)
            {
                lowerBound = minZoom;
                upperBound = maxZoom;
            }
            else
            {
                lowerBound = maxZoom;
                upperBound = minZoom;
            }
        }

        /// <summary>
        ///     Reads the zoom delta from the resolved input action, falling back to the mouse scroll
        ///     value so editor builds continue to function without a PlayerInput component.
        /// </summary>
        private float ReadZoomInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (zoomAction != null)
                return zoomAction.ReadValue<float>();

            if (Mouse.current != null)
                return Mouse.current.scroll.ReadValue().y;
#endif

            return 0f;
        }
    }
}
