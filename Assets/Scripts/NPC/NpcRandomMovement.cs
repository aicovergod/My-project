using UnityEngine;

namespace NPC
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class NpcRandomMovement : MonoBehaviour
    {
        [Header("Movement Area")]
        [Tooltip("Width and height of the wandering area centered on the start position.")]
        public Vector2 areaSize = new Vector2(5f, 5f);

        [Header("Movement")]
        public float moveSpeed = 2f;
        [Tooltip("Consider we have arrived when within this distance to the target.")]
        public float arriveDistance = 0.05f;
        [Tooltip("Minimum idle time before choosing a new target.")]
        public float minIdleTime = 0.5f;
        [Tooltip("Maximum idle time before choosing a new target.")]
        public float maxIdleTime = 2f;

        [Header("Visuals")]
        public NpcPathFollower.VisualMode visualMode = NpcPathFollower.VisualMode.Animator;
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

        private Rigidbody2D _rb;
        private Vector2 _origin;
        private Vector2 _target;
        private bool _waiting;
        private float _waitTimer;
        private Vector2 _lastPos;
        private bool _currentlyMoving;
        private int _currentDir;
        private float _animClock;
        private int _animFrame;

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
        }

        private void Start()
        {
            _origin = _rb != null ? _rb.position : (Vector2)transform.position;
            _lastPos = _origin;
            BeginIdle();
        }

        private void BeginIdle()
        {
            _waiting = true;
            _waitTimer = Random.Range(minIdleTime, maxIdleTime);
            UpdateVisuals(Vector2.zero);
        }

        private void ChooseNewTarget()
        {
            Vector2 half = areaSize * 0.5f;
            Vector2 randomOffset = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
            _target = _origin + randomOffset;
            _waiting = false;
        }

        private void FixedUpdate()
        {
            if (_waiting)
            {
                _waitTimer -= Time.fixedDeltaTime;
                if (_waitTimer <= 0f)
                    ChooseNewTarget();
                UpdateVisuals(Vector2.zero);
                return;
            }

            Vector2 current = _rb != null ? _rb.position : (Vector2)transform.position;
            Vector2 next = Vector2.MoveTowards(current, _target, moveSpeed * Time.fixedDeltaTime);
            Vector2 velocity = (next - _lastPos) / Time.fixedDeltaTime;

            if (_rb != null) _rb.MovePosition(next);
            else transform.position = next;

            if (Vector2.Distance(next, _target) <= arriveDistance)
                BeginIdle();

            UpdateVisuals(velocity);
            _lastPos = next;
        }

        private void UpdateVisuals(Vector2 velocity)
        {
            _currentlyMoving = velocity.sqrMagnitude > 0.0001f;

            if (_currentlyMoving)
            {
                if (Mathf.Abs(velocity.x) > Mathf.Abs(velocity.y))
                    _currentDir = velocity.x < 0f ? 1 : 2; // Left : Right
                else
                    _currentDir = velocity.y < 0f ? 0 : 3; // Down : Up
            }

            if (visualMode == NpcPathFollower.VisualMode.Animator)
            {
                if (animator == null) return;
                animator.SetBool(isMovingParam, _currentlyMoving);
                if (_currentlyMoving)
                    animator.SetInteger(dirParam, _currentDir);
                return;
            }

            if (spriteRenderer == null) return;

            float fps = Mathf.Max(0.01f, animationFPS);
            _animClock += Time.fixedDeltaTime * fps;
            int frames;
            Sprite[] set = SelectSpriteSet(_currentlyMoving, _currentDir, out frames);

            if (frames <= 0) return;

            _animFrame = Mathf.FloorToInt(_animClock) % frames;
            spriteRenderer.flipX = false;

            if (useFlipXForLeft && _currentDir == 1)
            {
                Sprite[] rightSet = SelectSpriteSet(_currentlyMoving, 2, out frames);
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
                    case 1:
                        set = useFlipXForLeft
                            ? (idleRight != null && idleRight.Length > 0 ? idleRight : walkRight)
                            : (idleLeft != null && idleLeft.Length > 0 ? idleLeft : walkLeft);
                        break;
                    case 2: set = idleRight != null && idleRight.Length > 0 ? idleRight : (walkRight ?? idleRight); break;
                    case 3: set = idleUp != null && idleUp.Length > 0 ? idleUp : (walkUp ?? idleUp); break;
                }
            }

            if (set == null || set.Length == 0)
            {
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
            Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
            Vector3 center = Application.isPlaying ? (Vector3)_origin : transform.position;
            Gizmos.DrawWireCube(center, new Vector3(areaSize.x, areaSize.y, 0f));
        }
#endif
    }
}

