using UnityEngine;
using Util;

namespace Pets
{
    /// <summary>
    /// Handles simple sprite swapping animation for pets based on movement direction.
    /// </summary>
    public class PetSpriteAnimator : MonoBehaviour
    {
        [Tooltip("SpriteRenderer used for displaying pet sprites (auto-found if null).")]
        public SpriteRenderer spriteRenderer;

        [Tooltip("Frames used when idle (Down/Diagonals/Cardinals).")]
        public Sprite[] idleDown;
        public Sprite[] idleDownRight;
        public Sprite[] idleRight;
        public Sprite[] idleUpRight;
        public Sprite[] idleUp;
        public Sprite[] idleUpLeft;
        public Sprite[] idleLeft;
        public Sprite[] idleDownLeft;

        [Tooltip("Frames used when walking (Down/Diagonals/Cardinals).")]
        public Sprite[] walkDown;
        public Sprite[] walkDownRight;
        public Sprite[] walkRight;
        public Sprite[] walkUpRight;
        public Sprite[] walkUp;
        public Sprite[] walkUpLeft;
        public Sprite[] walkLeft;
        public Sprite[] walkDownLeft;

        [Tooltip("Frames used when attacking (Down/Diagonals/Cardinals).")]
        public Sprite[] hitDown;
        public Sprite[] hitDownRight;
        public Sprite[] hitRight;
        public Sprite[] hitUpRight;
        public Sprite[] hitUp;
        public Sprite[] hitUpLeft;
        public Sprite[] hitLeft;
        public Sprite[] hitDownLeft;

        [Tooltip("If true, ignore Left arrays and flip the Right sprites for left-facing.")]
        public bool useFlipXForLeft = true;

        [Tooltip("If true, ignore Right arrays and flip the Left sprites for right-facing.")]
        public bool useFlipXForRight = false;

        [Tooltip("If true, reuse Down-Right sprites for Down-Left facings by mirroring them.")]
        public bool useFlipXForDownLeft = true;

        [Tooltip("If true, reuse Up-Right sprites for Up-Left facings by mirroring them.")]
        public bool useFlipXForUpLeft = true;

        [Tooltip("If true, reuse Up-Left sprites for Up-Right facings by mirroring them.")]
        public bool useFlipXForUpRight = false;

        [Tooltip("If true, reuse Down-Left sprites for Down-Right facings by mirroring them.")]
        public bool useFlipXForDownRight = false;

        [Tooltip("Frames per second for the sprite swapping animation.")]
        public float animationFPS = 6f;

        [Tooltip("Override frames per second while idle. Values <= 0 fall back to Animation FPS.")]
        public float idleAnimationFPS = 0f;

        [Tooltip("Override frames per second while walking. Values <= 0 fall back to Animation FPS.")]
        public float walkAnimationFPS = 0f;

        [Tooltip("Override frames per second while playing hit animations. Values <= 0 fall back to Animation FPS.")]
        public float hitAnimationFPS = 0f;

        private Direction8 _currentDir = Direction8.Down;
        private bool _currentlyMoving = false;
        private float _animClock = 0f;
        private int _animFrame = 0;
        private bool _overridePlaying = false;

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
            if (_overridePlaying)
                return;

            _currentlyMoving = velocity.sqrMagnitude > 0.0001f;

            if (_currentlyMoving)
            {
                _currentDir = Direction8Utility.FromVector(velocity, allowDiagonals: true, fallback: _currentDir);
            }

            if (spriteRenderer == null)
                return;

            float fps = ResolveAnimationFps(_currentlyMoving ? walkAnimationFPS : idleAnimationFPS);
            _animClock += Time.deltaTime * fps;
            Sprite[] set = SelectSpriteSet(_currentlyMoving, _currentDir, out int frames, out bool flip);

            if (frames <= 0)
                return;

            _animFrame = Mathf.FloorToInt(_animClock) % frames;
            spriteRenderer.sprite = set[_animFrame];
            spriteRenderer.flipX = flip;
        }

        /// <summary>Force the animator to face the given direction.</summary>
        public void SetFacing(Direction8 dir)
        {
            _currentDir = dir;
        }

        public bool HasHitAnimation(Direction8 dir)
        {
            Sprite[] set = SelectHitSpriteSet(dir, out int frames, out _);
            return frames > 0;
        }

        public System.Collections.IEnumerator PlayHitAnimation(Direction8 dir)
        {
            Sprite[] set = SelectHitSpriteSet(dir, out int frames, out bool flip);
            if (frames == 0 || spriteRenderer == null)
                yield break;

            _overridePlaying = true;
            _currentDir = dir;
            float fps = ResolveAnimationFps(hitAnimationFPS);
            for (int i = 0; i < frames; i++)
            {
                spriteRenderer.sprite = set[i];
                spriteRenderer.flipX = flip;
                yield return new WaitForSeconds(1f / fps);
            }
            _overridePlaying = false;
            _animClock = 0f;
        }

        /// <summary>
        ///     Resolves the hit animation frames for a given direction using mirroring and cardinal fallbacks so merged
        ///     attacks always find a frame set.
        /// </summary>
        private Sprite[] SelectHitSpriteSet(Direction8 dir, out int frames, out bool flip)
        {
            foreach (var lookup in Direction8Utility.BuildSpriteFallbackOrder(dir, ShouldMirrorHit))
            {
                Sprite[] set = GetHitSprites(lookup.Direction);
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
        ///     Resolves the active idle/walk frames for the supplied direction, respecting mirroring preferences and
        ///     gracefully falling back to cardinal facings.
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

        /// <summary>Returns either the idle or walk frames for the given direction based on current movement state.</summary>
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

        /// <summary>Retrieves the configured hit animation frames for the supplied direction.</summary>
        private Sprite[] GetHitSprites(Direction8 dir)
        {
            switch (dir)
            {
                case Direction8.Down:
                    return hitDown;
                case Direction8.DownRight:
                    return hitDownRight;
                case Direction8.Right:
                    return hitRight;
                case Direction8.UpRight:
                    return hitUpRight;
                case Direction8.Up:
                    return hitUp;
                case Direction8.UpLeft:
                    return hitUpLeft;
                case Direction8.Left:
                    return hitLeft;
                case Direction8.DownLeft:
                    return hitDownLeft;
                default:
                    return null;
            }
        }

        /// <summary>Returns true when the movement animation should reuse its mirrored counterpart via horizontal flipping.</summary>
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

        /// <summary>Mirroring preference for hit animations (shares settings with movement mirroring).</summary>
        private bool ShouldMirrorHit(Direction8 dir)
        {
            return ShouldMirrorMovement(dir);
        }

        /// <summary>Resolves the effective FPS taking per-state overrides into account.</summary>
        private float ResolveAnimationFps(float overrideFps)
        {
            float baseFps = animationFPS > 0f ? animationFPS : 6f;
            float resolved = overrideFps > 0f ? overrideFps : baseFps;
            return Mathf.Max(0.01f, resolved);
        }
    }
}

