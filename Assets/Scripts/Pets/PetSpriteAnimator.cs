using UnityEngine;

namespace Pets
{
    /// <summary>
    /// Handles simple sprite swapping animation for pets based on movement direction.
    /// </summary>
    public class PetSpriteAnimator : MonoBehaviour
    {
        [Tooltip("SpriteRenderer used for displaying pet sprites (auto-found if null).")]
        public SpriteRenderer spriteRenderer;

        [Tooltip("Frames used when idle and facing up.")]
        public Sprite[] idleUp;
        [Tooltip("Frames used when walking and facing up.")]
        public Sprite[] walkUp;

        [Tooltip("Frames used when idle and facing down.")]
        public Sprite[] idleDown;
        [Tooltip("Frames used when walking and facing down.")]
        public Sprite[] walkDown;

        [Tooltip("Frames used when idle and facing left.")]
        public Sprite[] idleLeft;
        [Tooltip("Frames used when walking and facing left.")]
        public Sprite[] walkLeft;

        [Tooltip("Frames used when idle and facing right.")]
        public Sprite[] idleRight;
        [Tooltip("Frames used when walking and facing right.")]
        public Sprite[] walkRight;

        [Tooltip("If true, ignore Left arrays and flip the Right sprites for left-facing.")]
        public bool useFlipXForLeft = true;

        [Tooltip("If true, ignore Right arrays and flip the Left sprites for right-facing.")]
        public bool useFlipXForRight = false;

        [Tooltip("Frames per second for the sprite swapping animation.")]
        public float animationFPS = 6f;

        private int _currentDir = 0; // 0=Down,1=Left,2=Right,3=Up
        private bool _currentlyMoving = false;
        private float _animClock = 0f;
        private int _animFrame = 0;

        private void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
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

            if (spriteRenderer == null) return;

            float fps = Mathf.Max(0.01f, animationFPS);
            _animClock += Time.deltaTime * fps;
            Sprite[] set = SelectSpriteSet(_currentlyMoving, _currentDir, out int frames);

            if (frames <= 0) return;

            _animFrame = Mathf.FloorToInt(_animClock) % frames;
            spriteRenderer.sprite = set[_animFrame];
            spriteRenderer.flipX = (_currentDir == 1 && useFlipXForLeft) || (_currentDir == 2 && useFlipXForRight);
        }

        /// <summary>Force the animator to face the given direction (0=Down,1=Left,2=Right,3=Up).</summary>
        public void SetFacing(int dir)
        {
            _currentDir = Mathf.Clamp(dir, 0, 3);
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
                    case 2: set = useFlipXForRight ? walkLeft : walkRight; break;
                    case 3: set = walkUp; break;
                }
            }
            else
            {
                switch (dir)
                {
                    case 0: set = idleDown != null && idleDown.Length > 0 ? idleDown : walkDown; break;
                    case 1:
                        set = useFlipXForLeft
                            ? (idleRight != null && idleRight.Length > 0 ? idleRight : walkRight)
                            : (idleLeft != null && idleLeft.Length > 0 ? idleLeft : walkLeft);
                        break;
                    case 2:
                        set = useFlipXForRight
                            ? (idleLeft != null && idleLeft.Length > 0 ? idleLeft : walkLeft)
                            : (idleRight != null && idleRight.Length > 0 ? idleRight : walkRight);
                        break;
                    case 3: set = idleUp != null && idleUp.Length > 0 ? idleUp : walkUp; break;
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

