using Combat;
using UnityEngine;
using Util;

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

        [Tooltip("Animator with parameters Dir(int 0-7 following Direction8 order) and IsMoving(bool). Used in Animator mode.")]
        public Animator animator;
        public string dirParam = "Dir";
        public string isMovingParam = "IsMoving";
        public string attackTrigger = "Attack";

        [Tooltip("SpriteRenderer used in SpriteSwap mode (auto-found if null).")]
        public SpriteRenderer spriteRenderer;

        [Header("SpriteSwap Sets (used only in SpriteSwap mode)")]
        [Tooltip("Frames used when idle (Down/Diagonals/Cardinals). Leave empty to fall back to matching walk frames.")]
        public Sprite[] idleDown;
        public Sprite[] idleDownRight;
        public Sprite[] idleRight;
        public Sprite[] idleUpRight;
        public Sprite[] idleUp;
        public Sprite[] idleUpLeft;
        public Sprite[] idleLeft;
        public Sprite[] idleDownLeft;

        [Tooltip("Frames used when moving (Down/Diagonals/Cardinals).")]
        public Sprite[] walkDown;
        public Sprite[] walkDownRight;
        public Sprite[] walkRight;
        public Sprite[] walkUpRight;
        public Sprite[] walkUp;
        public Sprite[] walkUpLeft;
        public Sprite[] walkLeft;
        public Sprite[] walkDownLeft;

        [Tooltip("Frames used when attacking (Down/Diagonals/Cardinals).")]
        public Sprite[] attackDown;
        public Sprite[] attackDownRight;
        public Sprite[] attackRight;
        public Sprite[] attackUpRight;
        public Sprite[] attackUp;
        public Sprite[] attackUpLeft;
        public Sprite[] attackLeft;
        public Sprite[] attackDownLeft;

        [Tooltip("If true, ignore Left arrays and flip the Right sprites for left-facing.")]
        public bool useFlipXForLeft = true;

        [Tooltip("If true, ignore Right arrays and flip the Left sprites for right-facing.")]
        public bool useFlipXForRight = false;

        [Tooltip("If true, reuse Down-Left sprites for Down-Right facings by mirroring them.")]
        public bool useFlipXForDownRight = false;

        [Tooltip("If true, reuse Up-Left sprites for Up-Right facings by mirroring them.")]
        public bool useFlipXForUpRight = false;

        [Tooltip("If true, reuse Up-Right sprites for Up-Left facings by mirroring them.")]
        public bool useFlipXForUpLeft = true;

        [Tooltip("If true, reuse Down-Right sprites for Down-Left facings by mirroring them.")]
        public bool useFlipXForDownLeft = true;

        [Tooltip("If true, ignore Left attack arrays and flip the Right sprites for left-facing attacks.")]
        public bool useFlipXForLeftAttack = true;

        [Tooltip("If true, ignore Right attack arrays and flip the Left sprites for right-facing attacks.")]
        public bool useFlipXForRightAttack = false;

        [Tooltip("If true, reuse Down-Left attack sprites for Down-Right facings by mirroring them.")]
        public bool useFlipXForDownRightAttack = false;

        [Tooltip("If true, reuse Up-Left attack sprites for Up-Right facings by mirroring them.")]
        public bool useFlipXForUpRightAttack = false;

        [Tooltip("If true, reuse Up-Right attack sprites for Up-Left facings by mirroring them.")]
        public bool useFlipXForUpLeftAttack = true;

        [Tooltip("If true, reuse Down-Right attack sprites for Down-Left facings by mirroring them.")]
        public bool useFlipXForDownLeftAttack = true;

        [Tooltip("Frames per second for SpriteSwap animation.")]
        public float animationFPS = 6f;

        [Header("Attack Timing")]
        [Tooltip("Minimum duration a single-frame attack pose should remain visible.")]
        [SerializeField]
        private float singleFrameAttackHoldSeconds = CombatMath.TICK_SECONDS;

        private Direction8 _currentDir = Direction8.Down;
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
            if (_overridePlaying)
                return;

            _currentlyMoving = velocity.sqrMagnitude > 0.0001f;

            if (_currentlyMoving)
            {
                _currentDir = Direction8Utility.FromVector(velocity, allowDiagonals: true, fallback: _currentDir);
            }

            if (visualMode == VisualMode.Animator)
            {
                if (animator == null)
                    return;
                animator.SetBool(isMovingParam, _currentlyMoving);
                animator.SetInteger(dirParam, Direction8Utility.ToAnimatorIndex8(_currentDir));
                return;
            }

            if (spriteRenderer == null)
                return;

            float fps = Mathf.Max(0.01f, animationFPS);
            _animClock += Time.fixedDeltaTime * fps;
            Sprite[] set = SelectSpriteSet(_currentlyMoving, _currentDir, out int frames, out bool flip);

            if (frames <= 0)
                return;

            _animFrame = Mathf.FloorToInt(_animClock) % frames;
            spriteRenderer.flipX = flip;
            spriteRenderer.sprite = set[_animFrame];
        }

        /// <summary>Force the animator to face the given direction.</summary>
        public void SetFacing(Direction8 dir)
        {
            _currentDir = dir;
        }

        public bool HasAttackAnimation(Direction8 dir)
        {
            Sprite[] set = SelectAttackSpriteSet(dir, out int frames, out _);
            return frames > 0;
        }

        public System.Collections.IEnumerator PlayAttackAnimation(Direction8 dir)
        {
            if (visualMode == VisualMode.Animator)
            {
                if (animator != null)
                {
                    animator.SetInteger(dirParam, Direction8Utility.ToAnimatorIndex8(dir));
                    animator.SetTrigger(attackTrigger);
                }
                yield break;
            }

            Sprite[] set = SelectAttackSpriteSet(dir, out int frames, out bool flip);
            if (frames == 0 || spriteRenderer == null)
                yield break;

            _overridePlaying = true;
            _currentDir = dir;
            float fps = Mathf.Max(0.01f, animationFPS);
            float baseFrameDuration = 1f / fps;
            float frameDuration = frames == 1
                ? Mathf.Max(singleFrameAttackHoldSeconds, baseFrameDuration)
                : baseFrameDuration;

            for (int i = 0; i < frames; i++)
            {
                spriteRenderer.sprite = set[i];
                spriteRenderer.flipX = flip;
                yield return new WaitForSeconds(frameDuration);
            }
            _overridePlaying = false;
            _animClock = 0f;
        }

        /// <summary>
        ///     Resolves the appropriate idle or walk sprite set for the requested direction, mirroring as configured
        ///     and falling back to cardinal facings when bespoke diagonal art is unavailable.
        /// </summary>
        private Sprite[] SelectSpriteSet(bool moving, Direction8 dir, out int frames, out bool flip)
        {
            foreach (var lookup in Direction8Utility.BuildSpriteFallbackOrder(dir, ShouldMirrorMovement))
            {
                Sprite[] set = GetMovementSprites(lookup.Direction, moving);
                if (set != null && set.Length > 0)
                {
                    frames = set.Length;
                    flip = lookup.FlipX;
                    return set;
                }
            }

            frames = 0;
            flip = false;
            return System.Array.Empty<Sprite>();
        }

        /// <summary>
        ///     Locates the correct attack animation frames for the supplied direction, using mirroring and down-facing
        ///     defaults where needed so attacks never fail to render.
        /// </summary>
        private Sprite[] SelectAttackSpriteSet(Direction8 dir, out int frames, out bool flip)
        {
            foreach (var lookup in Direction8Utility.BuildSpriteFallbackOrder(dir, ShouldMirrorAttack))
            {
                Sprite[] set = GetAttackSprites(lookup.Direction);
                if (set != null && set.Length > 0)
                {
                    frames = set.Length;
                    flip = lookup.FlipX;
                    return set;
                }
            }

            frames = 0;
            flip = false;
            return System.Array.Empty<Sprite>();
        }

        /// <summary>Returns either the idle or walk frames for the specified direction, depending on movement state.</summary>
        private Sprite[] GetMovementSprites(Direction8 dir, bool moving)
        {
            Sprite[] idle = null;
            Sprite[] walk = null;
            switch (dir)
            {
                case Direction8.Down:
                    idle = idleDown;
                    walk = walkDown;
                    break;
                case Direction8.DownRight:
                    idle = idleDownRight;
                    walk = walkDownRight;
                    break;
                case Direction8.Right:
                    idle = idleRight;
                    walk = walkRight;
                    break;
                case Direction8.UpRight:
                    idle = idleUpRight;
                    walk = walkUpRight;
                    break;
                case Direction8.Up:
                    idle = idleUp;
                    walk = walkUp;
                    break;
                case Direction8.UpLeft:
                    idle = idleUpLeft;
                    walk = walkUpLeft;
                    break;
                case Direction8.Left:
                    idle = idleLeft;
                    walk = walkLeft;
                    break;
                case Direction8.DownLeft:
                    idle = idleDownLeft;
                    walk = walkDownLeft;
                    break;
            }

            if (moving)
            {
                if (walk != null && walk.Length > 0)
                    return walk;
                if (idle != null && idle.Length > 0)
                    return idle;
            }
            else
            {
                if (idle != null && idle.Length > 0)
                    return idle;
                if (walk != null && walk.Length > 0)
                    return walk;
            }

            return null;
        }

        /// <summary>Retrieves the configured attack frames for the supplied direction.</summary>
        private Sprite[] GetAttackSprites(Direction8 dir)
        {
            switch (dir)
            {
                case Direction8.Down:
                    return attackDown;
                case Direction8.DownRight:
                    return attackDownRight;
                case Direction8.Right:
                    return attackRight;
                case Direction8.UpRight:
                    return attackUpRight;
                case Direction8.Up:
                    return attackUp;
                case Direction8.UpLeft:
                    return attackUpLeft;
                case Direction8.Left:
                    return attackLeft;
                case Direction8.DownLeft:
                    return attackDownLeft;
                default:
                    return null;
            }
        }

        /// <summary>Determines whether the movement animation for the given direction should be mirrored from its counterpart.</summary>
        private bool ShouldMirrorMovement(Direction8 dir)
        {
            switch (dir)
            {
                case Direction8.Left:
                    return useFlipXForLeft;
                case Direction8.Right:
                    return useFlipXForRight;
                case Direction8.DownLeft:
                    return useFlipXForDownLeft;
                case Direction8.UpLeft:
                    return useFlipXForUpLeft;
                case Direction8.UpRight:
                    return useFlipXForUpRight;
                case Direction8.DownRight:
                    return useFlipXForDownRight;
                default:
                    return false;
            }
        }

        /// <summary>Determines whether the attack animation for the given direction should reuse the mirrored counterpart.</summary>
        private bool ShouldMirrorAttack(Direction8 dir)
        {
            switch (dir)
            {
                case Direction8.Left:
                    return useFlipXForLeftAttack;
                case Direction8.Right:
                    return useFlipXForRightAttack;
                case Direction8.DownLeft:
                    return useFlipXForDownLeftAttack;
                case Direction8.UpLeft:
                    return useFlipXForUpLeftAttack;
                case Direction8.UpRight:
                    return useFlipXForUpRightAttack;
                case Direction8.DownRight:
                    return useFlipXForDownRightAttack;
                default:
                    return false;
            }
        }
    }
}

