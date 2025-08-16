using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Handles sprite-based visuals for NPCs using either an Animator or manual sprite swapping.
    /// </summary>
    public class NpcSpriteAnimator : MonoBehaviour
    {
        public enum VisualMode { Animator, SpriteSwap }

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

        private int _currentDir = 0;
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
            if (animator == null) animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        }

        /// <summary>
        /// Update the visual state based on movement velocity.
        /// </summary>
        public void UpdateVisuals(Vector2 velocity)
        {
            _currentlyMoving = velocity.sqrMagnitude > 0.0001f;

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
    }
}

