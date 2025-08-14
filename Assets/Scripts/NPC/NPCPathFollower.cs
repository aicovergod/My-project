using System.Collections;
using UnityEngine;

namespace NPC
{
    /// <summary>
    /// NPC path movement with optional Animator driving OR sprite swapping (frame animation).
    /// Primary workflow: assign a WaypointPath. Also supports legacy manual waypoints.
    /// Loop/PingPong/Once, per-point waits/speeds, start-at-nearest, snapshot-at-start.
    /// Visuals:
    ///  - Animator mode: sets Dir(int:0=Down,1=Left,2=Right,3=Up) + IsMoving(bool)
    ///  - SpriteSwap mode: swaps SpriteRenderer sprites per direction with idle/walk arrays
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class NpcPathFollower : MonoBehaviour
    {
        public enum LoopType { Loop, PingPong, Once }
        public enum VisualMode { Animator, SpriteSwap }

        [Header("Path Reference (preferred)")]
        [Tooltip("Reusable scene path. If set, used instead of the legacy Waypoints array.")]
        public WaypointPath path;

        [Header("Legacy Waypoints (optional)")]
        [Tooltip("Used only if Path is null. If empty, will try to read from a child named 'Path'.")]
        public Transform[] waypoints;

        [Header("Traversal")]
        public LoopType loopType = LoopType.Loop;
        public bool startAtNearest = false;
        [Tooltip("Snapshot world positions at Start (recommended if points might move/are parented).")]
        public bool snapshotAtStart = false;

        [Header("Movement")]
        [Tooltip("Base units per second. If your tiles are 1 unit (PPU=64), this is tiles/sec.")]
        public float moveSpeed = 2.0f;
        [Tooltip("Consider we 'arrived' when within this distance.")]
        public float arriveDistance = 0.05f;
        [Tooltip("If true, immediately place the NPC at the starting point on Start().")]
        public bool snapToFirstOnStart = false;

        [Header("Visuals")]
        public VisualMode visualMode = VisualMode.Animator;

        [Tooltip("Animator with parameters Dir(int 0=Down,1=Left,2=Right,3=Up) and IsMoving(bool). Used in Animator mode.")]
        public Animator animator;
        public string dirParam = "Dir";
        public string isMovingParam = "IsMoving";

        [Tooltip("SpriteRenderer used in SpriteSwap mode (auto-found if null).")]
        public SpriteRenderer spriteRenderer;

        [Header("SpriteSwap Sets (used only in SpriteSwap mode)")]
        [Tooltip("Frames used when idle (Down). Leave empty to fall back to first frame of WalkDown.")]
        public Sprite[] idleDown;
        public Sprite[] idleLeft;
        public Sprite[] idleRight;
        public Sprite[] idleUp;

        [Tooltip("Frames used when moving (Down/Left/Right/Up).")]
        public Sprite[] walkDown;
        public Sprite[] walkLeft;
        public Sprite[] walkRight;
        public Sprite[] walkUp;

        [Tooltip("If true, ignore Left arrays and flip the Right sprites for left-facing.")]
        public bool useFlipXForLeft = true;

        [Tooltip("Frames per second for SpriteSwap animation.")]
        public float animationFPS = 6f;

        // Internal state
        private Rigidbody2D _rb;
        private int _index;
        private int _step = 1; // for PingPong
        private bool _waiting;
        private Vector2 _lastPos;

        // Snapshot storage
        private Vector2[] _snapshotPoints;
        private bool _usingSnapshot;

        // Legacy auto-discovered container
        private Transform _legacyPathContainer;

        // Visual state
        private int _currentDir = 0; // 0=Down,1=Left,2=Right,3=Up
        private bool _currentlyMoving = false;
        private float _animClock = 0f;
        private int _animFrame = 0;

        private void Reset()
        {
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null) _rb.bodyType = RigidbodyType2D.Kinematic;

            if (animator == null) animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();

            // Legacy auto-discovery (only used if no WaypointPath and no manual array)
            if (path == null && (waypoints == null || waypoints.Length == 0))
            {
                var child = transform.Find("Path");
                if (child != null)
                {
                    _legacyPathContainer = child;
                    int count = child.childCount;
                    waypoints = new Transform[count];
                    for (int i = 0; i < count; i++)
                        waypoints[i] = child.GetChild(i);
                }
            }
        }

        private void Start()
        {
            // If we have a legacy child path, snapshot by default (prevents moving-target bug).
            if (_legacyPathContainer != null) snapshotAtStart = true;

            BuildSnapshotIfNeeded();

            // Starting index
            if (GetPointCount() > 0)
            {
                _index = startAtNearest ? GetNearestIndex(GetPosition2D()) : 0;

                if (snapToFirstOnStart)
                {
                    Vector2 p = GetPoint(_index);
                    if (_rb) _rb.position = p;
                    else transform.position = p;
                }
            }

            _lastPos = GetPosition2D();
            UpdateVisuals(Vector2.zero); // init as idle
        }

        private void FixedUpdate()
        {
            if (_waiting || GetPointCount() == 0)
            {
                UpdateVisuals(Vector2.zero);
                return;
            }

            Vector2 current = GetPosition2D();
            Vector2 target = GetPoint(_index);
            Vector2 toTarget = target - current;
            float dist = toTarget.magnitude;

            if (dist <= arriveDistance)
            {
                float wait = GetWaitFor(_index);
                if (wait > 0f)
                {
                    if (!_waiting) StartCoroutine(WaitThenAdvance(wait));
                }
                else
                {
                    AdvanceIndex();
                }
                UpdateVisuals(Vector2.zero);
                return;
            }

            // Move
            Vector2 dir = (dist > 0.0001f) ? (toTarget / dist) : Vector2.zero;
            float speed = GetSpeedMultiplierFor(_index) * Mathf.Max(0f, moveSpeed);
            Vector2 next = current + dir * speed * Time.fixedDeltaTime;

            if (_rb != null) _rb.MovePosition(next);
            else transform.position = next;

            Vector2 velocity = (next - _lastPos) / Time.fixedDeltaTime;
            UpdateVisuals(velocity);
            _lastPos = next;
        }

        private IEnumerator WaitThenAdvance(float seconds)
        {
            _waiting = true;
            UpdateVisuals(Vector2.zero);
            yield return new WaitForSeconds(seconds);
            AdvanceIndex();
            _waiting = false;
        }

        private void AdvanceIndex()
        {
            int n = GetPointCount();
            if (n == 0) return;

            bool isLoop = (loopType == LoopType.Loop) || (path != null && path.closedLoop && loopType != LoopType.Once);

            if (loopType == LoopType.Once && _index >= n - 1)
                return;

            if (loopType == LoopType.PingPong)
            {
                if (n == 1) return;
                _index += _step;
                if (_index >= n) { _index = n - 2; _step = -1; }
                else if (_index < 0) { _index = 1; _step = 1; }
                return;
            }

            _index = (_index + 1);
            if (isLoop) _index %= n;
            else _index = Mathf.Min(_index, n - 1);
        }

        // ------- Path helpers -------
        private int GetPointCount()
        {
            if (path != null) return path.Count;
            if (_usingSnapshot && _snapshotPoints != null) return _snapshotPoints.Length;
            if (waypoints != null) return waypoints.Length;
            return 0;
        }

        private Vector2 GetPoint(int i)
        {
            if (path != null) return path.GetPoint(i);
            if (_usingSnapshot && _snapshotPoints != null && i >= 0 && i < _snapshotPoints.Length) return _snapshotPoints[i];
            if (waypoints != null && i >= 0 && i < waypoints.Length && waypoints[i] != null) return waypoints[i].position;
            return GetPosition2D();
        }

        private float GetWaitFor(int i)
        {
            if (path != null) return path.GetWait(i);
            return 0f;
        }

        private float GetSpeedMultiplierFor(int i)
        {
            if (path != null) return path.GetSpeedMultiplier(i);
            return 1f;
        }

        private int GetNearestIndex(Vector2 from)
        {
            int n = GetPointCount();
            if (n == 0) return 0;
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = GetPoint(i);
                float d = (p - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private Vector2 GetPosition2D()
        {
            if (_rb != null) return _rb.position;
            return (Vector2)transform.position;
        }

        private void BuildSnapshotIfNeeded()
        {
            if (!snapshotAtStart) { _usingSnapshot = false; _snapshotPoints = null; return; }

            int n = (path != null) ? path.Count : (waypoints != null ? waypoints.Length : 0);
            if (n <= 0) { _usingSnapshot = false; _snapshotPoints = null; return; }

            _usingSnapshot = true;
            _snapshotPoints = new Vector2[n];

            if (path != null)
            {
                for (int i = 0; i < n; i++) _snapshotPoints[i] = path.GetPoint(i);
            }
            else
            {
                for (int i = 0; i < n; i++)
                    _snapshotPoints[i] = waypoints[i] ? (Vector2)waypoints[i].position : GetPosition2D();
            }
        }

        // ------- Visuals -------
        private void UpdateVisuals(Vector2 velocity)
        {
            _currentlyMoving = velocity.sqrMagnitude > 0.0001f;

            // Determine facing dir from dominant axis (Down/Left/Right/Up)
            if (_currentlyMoving)
            {
                if (Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y))
                    _currentDir = velocity.x < 0f ? 1 : 2; // Left : Right
                else
                    _currentDir = velocity.y < 0f ? 0 : 3; // Down : Up
            }

            if (visualMode == VisualMode.Animator)
            {
                if (animator == null) return;
                animator.SetBool(isMovingParam, _currentlyMoving);
                if (_currentlyMoving)
                    animator.SetInteger(dirParam, _currentDir);
                return;
            }

            // SpriteSwap mode
            if (spriteRenderer == null) return;

            // Advance animation clock if moving, else idle clock
            float fps = Mathf.Max(0.01f, animationFPS);
            _animClock += Time.fixedDeltaTime * fps;
            int frames;
            Sprite[] set = SelectSpriteSet(_currentlyMoving, _currentDir, out frames);

            if (frames <= 0) return;

            _animFrame = Mathf.FloorToInt(_animClock) % frames;
            spriteRenderer.flipX = false; // reset; may set below

            // Handle flip-left using right set if requested
            if (useFlipXForLeft && _currentDir == 1) // Left
            {
                Sprite[] rightSet = SelectSpriteSet(_currentlyMoving, 2, out frames); // use right
                if (frames > 0)
                {
                    _animFrame = Mathf.FloorToInt(_animClock) % frames;
                    spriteRenderer.sprite = rightSet[_animFrame];
                    spriteRenderer.flipX = true;
                    return;
                }
            }

            spriteRenderer.sprite = set[_animFrame];
        }

        private Sprite[] SelectSpriteSet(bool moving, int dir, out int frames)
        {
            Sprite[] set = null;

            if (moving)
            {
                switch (dir)
                {
                    case 0: set = walkDown; break;
                    case 1: set = useFlipXForLeft ? walkRight : walkLeft; break;
                    case 2: set = walkRight; break;
                    case 3: set = walkUp; break;
                }
            }
            else
            {
                switch (dir)
                {
                    case 0: set = idleDown != null && idleDown.Length > 0 ? idleDown : (walkDown ?? idleDown); break;
                    case 1: set = useFlipXForLeft
                            ? (idleRight != null && idleRight.Length > 0 ? idleRight : walkRight)
                            : (idleLeft != null && idleLeft.Length > 0 ? idleLeft : walkLeft);
                        break;
                    case 2: set = idleRight != null && idleRight.Length > 0 ? idleRight : (walkRight ?? idleRight); break;
                    case 3: set = idleUp != null && idleUp.Length > 0 ? idleUp : (walkUp ?? idleUp); break;
                }
            }

            if (set == null || set.Length == 0)
            {
                // Fallback: try a single frame from down/right
                if (!moving)
                {
                    if (idleDown != null && idleDown.Length > 0) { frames = idleDown.Length; return idleDown; }
                    if (walkDown != null && walkDown.Length > 0) { frames = walkDown.Length; return walkDown; }
                }
                else
                {
                    if (walkRight != null && walkRight.Length > 0) { frames = walkRight.Length; return walkRight; }
                    if (walkDown != null && walkDown.Length > 0) { frames = walkDown.Length; return walkDown; }
                }
            }

            frames = set != null ? set.Length : 0;
            return set ?? System.Array.Empty<Sprite>();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Visualize whichever source we're using
            int n = GetPointCount();
            if (n <= 0) return;

            for (int i = 0; i < n; i++)
            {
                Vector3 a = GetPoint(i);
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(a, 0.07f);

                int j = i + 1;
                bool isLoop = (loopType == LoopType.Loop) || (path != null && path.closedLoop && loopType != LoopType.Once);
                if (j < n)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(a, GetPoint(j));
                }
                else if (isLoop && n > 1)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(a, GetPoint(0));
                }
            }
        }
#endif
    }
}
