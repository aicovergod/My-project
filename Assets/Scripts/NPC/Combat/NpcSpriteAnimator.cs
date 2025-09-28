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
        public string attackTrigger = "Attack";

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

        [Tooltip("Frames used when attacking (Down/Left/Right/Up).")]
        public Sprite[] attackDown;
        public Sprite[] attackLeft;
        public Sprite[] attackRight;
        public Sprite[] attackUp;

        [Tooltip("If true, ignore Left arrays and flip the Right sprites for left-facing.")]
        public bool useFlipXForLeft = true;

        [Tooltip("If true, ignore Right arrays and flip the Left sprites for right-facing.")]
        public bool useFlipXForRight = false;

        [Tooltip("If true, ignore Left attack arrays and flip the Right sprites for left-facing attacks.")]
        public bool useFlipXForLeftAttack = true;

        [Tooltip("If true, ignore Right attack arrays and flip the Left sprites for right-facing attacks.")]
        public bool useFlipXForRightAttack = false;

        [Tooltip("Frames per second for SpriteSwap animation.")]
        public float animationFPS = 6f;

        private int _currentDir = 0;
        private bool _currentlyMoving = false;
        private float _animClock = 0f;
        private int _animFrame = 0;
        private bool _overridePlaying = false;

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
            if (_overridePlaying) return;

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

            if (useFlipXForRight && _currentDir == 2)
            {
                bool prevFlipLeft = useFlipXForLeft;
                useFlipXForLeft = false;
                Sprite[] leftSet = SelectSpriteSet(_currentlyMoving, 1, out frames);
                useFlipXForLeft = prevFlipLeft;
                if (frames > 0)
                {
                    _animFrame = Mathf.FloorToInt(_animClock) % frames;
                    spriteRenderer.sprite = leftSet[_animFrame];
                    spriteRenderer.flipX = true;
                    return;
                }
            }

            spriteRenderer.sprite = set[_animFrame];
        }

        /// <summary>Force the animator to face the given direction (0=Down,1=Left,2=Right,3=Up).</summary>
        public void SetFacing(int dir)
        {
            _currentDir = Mathf.Clamp(dir, 0, 3);
        }

        public bool HasAttackAnimation(int dir)
        {
            Sprite[] set = SelectAttackSpriteSet(dir, out int frames);
            return frames > 0;
        }

        public System.Collections.IEnumerator PlayAttackAnimation(int dir)
        {
            if (visualMode == VisualMode.Animator)
            {
                if (animator != null)
                {
                    animator.SetInteger(dirParam, Mathf.Clamp(dir, 0, 3));
                    animator.SetTrigger(attackTrigger);
                }
                yield break;
            }

            Sprite[] set = SelectAttackSpriteSet(dir, out int frames);
            if (frames == 0 || spriteRenderer == null)
                yield break;

            _overridePlaying = true;
            _currentDir = Mathf.Clamp(dir, 0, 3);
            float fps = Mathf.Max(0.01f, animationFPS);
            for (int i = 0; i < frames; i++)
            {
                spriteRenderer.sprite = set[i];
                spriteRenderer.flipX = (_currentDir == 1 && useFlipXForLeftAttack) || (_currentDir == 2 && useFlipXForRightAttack);
                yield return new WaitForSeconds(1f / fps);
            }
            _overridePlaying = false;
            _animClock = 0f;
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
                    case 0: set = idleDown != null && idleDown.Length > 0 ? idleDown : (walkDown ?? idleDown); break;
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
                    if (useFlipXForRight && walkLeft != null && walkLeft.Length > 0) { frames = walkLeft.Length; return walkLeft; }
                    if (walkRight != null && walkRight.Length > 0) { frames = walkRight.Length; return walkRight; }
                    if (walkDown != null && walkDown.Length > 0) { frames = walkDown.Length; return walkDown; }
                }
            }

            frames = set != null ? set.Length : 0;
            return set ?? System.Array.Empty<Sprite>();
        }

        private Sprite[] SelectAttackSpriteSet(int dir, out int frames)
        {
            Sprite[] set = null;
            switch (dir)
            {
                case 0: set = attackDown; break;
                case 1: set = useFlipXForLeftAttack ? attackRight : attackLeft; break;
                case 2: set = useFlipXForRightAttack ? attackLeft : attackRight; break;
                case 3: set = attackUp; break;
            }

            frames = set != null ? set.Length : 0;
            return set ?? System.Array.Empty<Sprite>();
        }
    }
}

