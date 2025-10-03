// Assets/Scripts/Player/Visuals/PlayerSpriteController.cs
using System;
using UnityEngine;
using UnityEngine.Serialization;
using Util;

namespace Player.Visuals
{
    /// <summary>
    ///     Drives the player's sprite renderer and animator states, supporting optional directional overrides
    ///     and configurable mirroring flags so OSRS-style diagonal reuse behaves correctly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerSpriteController : MonoBehaviour, IPlayerSpriteController
    {
        [Header("(Optional) Direct Sprite Override")]
        [Tooltip("Directional overrides can include either legacy single sprites or multi-frame lists for idle/walk states.")]
        [SerializeField] private DirectionalSpriteOverride downOverrides;
        [SerializeField] private DirectionalSpriteOverride downRightOverrides;
        [SerializeField] private DirectionalSpriteOverride rightOverrides;
        [SerializeField] private DirectionalSpriteOverride upRightOverrides;
        [SerializeField] private DirectionalSpriteOverride upOverrides;
        [SerializeField] private DirectionalSpriteOverride upLeftOverrides;
        [SerializeField] private DirectionalSpriteOverride leftOverrides;
        [SerializeField] private DirectionalSpriteOverride downLeftOverrides;

        [Header("Sprite Animation Settings")]
        [SerializeField, Min(0.1f), Tooltip("Frames-per-second used when resolving idle frame lists.")]
        private float idleAnimationFps = 6f;
        [SerializeField, Min(0.1f), Tooltip("Frames-per-second used when resolving walking frame lists.")]
        private float walkAnimationFps = 10f;

        [Header("Mirroring")]
        [Tooltip("If true, reuse right-facing sprites for any left-facing orientation (including diagonals).")]
        [SerializeField] private bool useFlipXForLeft;
        [Tooltip("If true, reuse left-facing sprites for any right-facing orientation (including diagonals).")]
        [SerializeField] private bool useFlipXForRight;
        [Tooltip("If true, reuse Down-Right sprites for Down-Left facings by mirroring them.")]
        [SerializeField] private bool useFlipXForDownLeft = true;
        [Tooltip("If true, reuse Up-Right sprites for Up-Left facings by mirroring them.")]
        [SerializeField] private bool useFlipXForUpLeft = true;
        [Tooltip("If true, reuse Up-Left sprites for Up-Right facings by mirroring them.")]
        [SerializeField] private bool useFlipXForUpRight;
        [Tooltip("If true, reuse Down-Left sprites for Down-Right facings by mirroring them.")]
        [SerializeField] private bool useFlipXForDownRight;

        [Tooltip("When enabled, keeps the current sprite frame static while allowing the animator parameters to continue updating.")]
        [SerializeField] private bool freezeSprite;

        private Animator animator;
        private SpriteRenderer spriteRenderer;

        private Direction8 cachedLookupDirection;
        private bool cachedIsMoving;
        private MovementVisualState cachedResolvedState;
        private Sprite[] cachedResolvedFrames;
        private int cachedFrameIndex;
        private float nextFrameTime;

        /// <inheritdoc />
        public bool FreezeSprite
        {
            get => freezeSprite;
            set => freezeSprite = value;
        }

        /// <inheritdoc />
        public bool UseFlipXForLeft
        {
            get => useFlipXForLeft;
            set => useFlipXForLeft = value;
        }

        /// <inheritdoc />
        public bool UseFlipXForRight
        {
            get => useFlipXForRight;
            set => useFlipXForRight = value;
        }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <inheritdoc />
        public void ApplyMovementVisuals(Direction8 direction, bool isMoving)
        {
            if (animator != null)
            {
                animator.SetBool("IsMoving", isMoving);
                animator.SetInteger("Dir", Direction8Utility.ToAnimatorIndex(direction));
            }

            if (spriteRenderer == null)
                return;

            if (freezeSprite)
                return;

            if (!TryResolveVisualState(direction, isMoving, out Sprite[] frames, out Sprite desired, out MovementVisualState resolvedState, out Direction8 lookupDirection, out bool flip))
                return;

            if (spriteRenderer.flipX != flip)
                spriteRenderer.flipX = flip;

            if (frames != null && frames.Length > 0)
            {
                var shouldReset = cachedResolvedFrames != frames || cachedLookupDirection != lookupDirection || cachedIsMoving != isMoving || cachedResolvedState != resolvedState;

                if (shouldReset)
                {
                    cachedResolvedFrames = frames;
                    cachedLookupDirection = lookupDirection;
                    cachedIsMoving = isMoving;
                    cachedResolvedState = resolvedState;
                    cachedFrameIndex = 0;
                    nextFrameTime = Time.time + GetFrameInterval(resolvedState);
                }
                else if (frames.Length > 1 && Time.time >= nextFrameTime)
                {
                    cachedFrameIndex = (cachedFrameIndex + 1) % frames.Length;
                    nextFrameTime = Time.time + GetFrameInterval(cachedResolvedState);
                }

                var frameSprite = frames[Mathf.Clamp(cachedFrameIndex, 0, frames.Length - 1)];
                if (spriteRenderer.sprite != frameSprite)
                    spriteRenderer.sprite = frameSprite;
            }
            else if (desired != null)
            {
                cachedResolvedFrames = null;
                cachedLookupDirection = lookupDirection;
                cachedIsMoving = isMoving;
                cachedResolvedState = resolvedState;
                cachedFrameIndex = 0;
                nextFrameTime = 0f;

                if (spriteRenderer.sprite != desired)
                    spriteRenderer.sprite = desired;
            }
        }

        /// <summary>Determines whether the supplied direction should mirror its counterpart when resolving overrides.</summary>
        private bool ShouldMirrorOverride(Direction8 direction)
        {
            return direction switch
            {
                Direction8.Left => useFlipXForLeft,
                Direction8.Right => useFlipXForRight,
                Direction8.DownLeft => useFlipXForDownLeft,
                Direction8.UpLeft => useFlipXForUpLeft,
                Direction8.UpRight => useFlipXForUpRight,
                Direction8.DownRight => useFlipXForDownRight,
                _ => false
            };
        }

        /// <summary>Retrieves the idle override sprite for the supplied facing direction.</summary>
        public Sprite GetIdleSprite(Direction8 direction)
        {
            return GetOverride(direction).SingleIdle;
        }

        /// <summary>Assigns the idle override sprite for the supplied facing direction.</summary>
        public void SetIdleSprite(Direction8 direction, Sprite sprite)
        {
            ref var overrides = ref GetOverrideRef(direction);
            overrides.SingleIdle = sprite;
        }

        /// <summary>Retrieves the walking override sprite for the supplied facing direction.</summary>
        public Sprite GetWalkSprite(Direction8 direction)
        {
            return GetOverride(direction).SingleWalk;
        }

        /// <summary>Assigns the walking override sprite for the supplied facing direction.</summary>
        public void SetWalkSprite(Direction8 direction, Sprite sprite)
        {
            ref var overrides = ref GetOverrideRef(direction);
            overrides.SingleWalk = sprite;
        }

        /// <inheritdoc />
        public float IdleAnimationFps => idleAnimationFps;

        /// <inheritdoc />
        public float WalkAnimationFps => walkAnimationFps;

        /// <inheritdoc />
        public int GetFrameCount(Direction8 direction, bool moving)
        {
            var overrides = GetOverride(direction);
            var frames = moving ? overrides.WalkFrames : overrides.IdleFrames;
            if (frames != null && frames.Length > 0)
                return frames.Length;

            frames = moving ? overrides.IdleFrames : overrides.WalkFrames;
            if (frames != null && frames.Length > 0)
                return frames.Length;

            var sprite = moving ? overrides.SingleWalk : overrides.SingleIdle;
            if (sprite != null)
                return 1;

            sprite = moving ? overrides.SingleIdle : overrides.SingleWalk;
            return sprite != null ? 1 : 0;
        }

        private void OnValidate()
        {
            idleAnimationFps = Mathf.Max(0.1f, idleAnimationFps);
            walkAnimationFps = Mathf.Max(0.1f, walkAnimationFps);

            SeedLegacyFrames(ref downOverrides);
            SeedLegacyFrames(ref downRightOverrides);
            SeedLegacyFrames(ref rightOverrides);
            SeedLegacyFrames(ref upRightOverrides);
            SeedLegacyFrames(ref upOverrides);
            SeedLegacyFrames(ref upLeftOverrides);
            SeedLegacyFrames(ref leftOverrides);
            SeedLegacyFrames(ref downLeftOverrides);
        }

        private void SeedLegacyFrames(ref DirectionalSpriteOverride overrides)
        {
            overrides.SeedFrameArraysFromLegacySingles();
        }

        private float GetFrameInterval(MovementVisualState state)
        {
            var fps = state == MovementVisualState.Walk ? walkAnimationFps : idleAnimationFps;
            return fps <= 0f ? float.MaxValue : 1f / fps;
        }

        private bool TryResolveVisualState(Direction8 direction, bool moving, out Sprite[] frames, out Sprite sprite, out MovementVisualState resolvedState, out Direction8 lookupDirection, out bool flip)
        {
            foreach (var lookup in Direction8Utility.BuildSpriteFallbackOrder(direction, ShouldMirrorOverride))
            {
                var overrides = GetOverride(lookup.Direction);

                if (moving)
                {
                    if (overrides.TryGetFrames(MovementVisualState.Walk, out frames))
                    {
                        sprite = null;
                        resolvedState = MovementVisualState.Walk;
                        lookupDirection = lookup.Direction;
                        flip = lookup.FlipX;
                        return true;
                    }

                    if (overrides.TryGetFrames(MovementVisualState.Idle, out frames))
                    {
                        sprite = null;
                        resolvedState = MovementVisualState.Idle;
                        lookupDirection = lookup.Direction;
                        flip = lookup.FlipX;
                        return true;
                    }

                    if (overrides.TryGetSingle(MovementVisualState.Walk, out sprite))
                    {
                        frames = null;
                        resolvedState = MovementVisualState.Walk;
                        lookupDirection = lookup.Direction;
                        flip = lookup.FlipX;
                        return true;
                    }

                    if (overrides.TryGetSingle(MovementVisualState.Idle, out sprite))
                    {
                        frames = null;
                        resolvedState = MovementVisualState.Idle;
                        lookupDirection = lookup.Direction;
                        flip = lookup.FlipX;
                        return true;
                    }
                }
                else
                {
                    if (overrides.TryGetFrames(MovementVisualState.Idle, out frames))
                    {
                        sprite = null;
                        resolvedState = MovementVisualState.Idle;
                        lookupDirection = lookup.Direction;
                        flip = lookup.FlipX;
                        return true;
                    }

                    if (overrides.TryGetFrames(MovementVisualState.Walk, out frames))
                    {
                        sprite = null;
                        resolvedState = MovementVisualState.Walk;
                        lookupDirection = lookup.Direction;
                        flip = lookup.FlipX;
                        return true;
                    }

                    if (overrides.TryGetSingle(MovementVisualState.Idle, out sprite))
                    {
                        frames = null;
                        resolvedState = MovementVisualState.Idle;
                        lookupDirection = lookup.Direction;
                        flip = lookup.FlipX;
                        return true;
                    }

                    if (overrides.TryGetSingle(MovementVisualState.Walk, out sprite))
                    {
                        frames = null;
                        resolvedState = MovementVisualState.Walk;
                        lookupDirection = lookup.Direction;
                        flip = lookup.FlipX;
                        return true;
                    }
                }
            }

            frames = null;
            sprite = null;
            resolvedState = moving ? MovementVisualState.Walk : MovementVisualState.Idle;
            lookupDirection = direction;
            flip = false;
            return false;
        }

        private DirectionalSpriteOverride GetOverride(Direction8 direction)
        {
            return direction switch
            {
                Direction8.Down => downOverrides,
                Direction8.DownRight => downRightOverrides,
                Direction8.Right => rightOverrides,
                Direction8.UpRight => upRightOverrides,
                Direction8.Up => upOverrides,
                Direction8.UpLeft => upLeftOverrides,
                Direction8.Left => leftOverrides,
                Direction8.DownLeft => downLeftOverrides,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
            };
        }

        private ref DirectionalSpriteOverride GetOverrideRef(Direction8 direction)
        {
            switch (direction)
            {
                case Direction8.Down:
                    return ref downOverrides;
                case Direction8.DownRight:
                    return ref downRightOverrides;
                case Direction8.Right:
                    return ref rightOverrides;
                case Direction8.UpRight:
                    return ref upRightOverrides;
                case Direction8.Up:
                    return ref upOverrides;
                case Direction8.UpLeft:
                    return ref upLeftOverrides;
                case Direction8.Left:
                    return ref leftOverrides;
                case Direction8.DownLeft:
                    return ref downLeftOverrides;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        private enum MovementVisualState
        {
            Idle,
            Walk
        }

        [System.Serializable]
        private struct DirectionalSpriteOverride
        {
            [FormerlySerializedAs("idleDown"), FormerlySerializedAs("idleDownRight"), FormerlySerializedAs("idleRight"), FormerlySerializedAs("idleUpRight"), FormerlySerializedAs("idleUp"), FormerlySerializedAs("idleUpLeft"), FormerlySerializedAs("idleLeft"), FormerlySerializedAs("idleDownLeft")]
            [SerializeField]
            private Sprite singleIdle;

            [FormerlySerializedAs("walkDown"), FormerlySerializedAs("walkDownRight"), FormerlySerializedAs("walkRight"), FormerlySerializedAs("walkUpRight"), FormerlySerializedAs("walkUp"), FormerlySerializedAs("walkUpLeft"), FormerlySerializedAs("walkLeft"), FormerlySerializedAs("walkDownLeft")]
            [SerializeField]
            private Sprite singleWalk;

            [SerializeField]
            private Sprite[] idleFrames;

            [SerializeField]
            private Sprite[] walkFrames;

            public Sprite SingleIdle
            {
                readonly get => singleIdle;
                set
                {
                    singleIdle = value;

                    if (idleFrames == null || idleFrames.Length == 0)
                    {
                        if (value != null)
                            idleFrames = new[] { value };
                    }
                    else if (idleFrames.Length == 1)
                    {
                        idleFrames[0] = value;
                    }
                }
            }

            public Sprite SingleWalk
            {
                readonly get => singleWalk;
                set
                {
                    singleWalk = value;

                    if (walkFrames == null || walkFrames.Length == 0)
                    {
                        if (value != null)
                            walkFrames = new[] { value };
                    }
                    else if (walkFrames.Length == 1)
                    {
                        walkFrames[0] = value;
                    }
                }
            }

            public Sprite[] IdleFrames => idleFrames;

            public Sprite[] WalkFrames => walkFrames;

            public void SeedFrameArraysFromLegacySingles()
            {
                if ((idleFrames == null || idleFrames.Length == 0) && singleIdle != null)
                    idleFrames = new[] { singleIdle };

                if ((walkFrames == null || walkFrames.Length == 0) && singleWalk != null)
                    walkFrames = new[] { singleWalk };
            }

            public bool TryGetFrames(MovementVisualState state, out Sprite[] frames)
            {
                frames = state == MovementVisualState.Walk ? walkFrames : idleFrames;
                if (frames != null && frames.Length > 0)
                    return true;

                frames = null;
                return false;
            }

            public bool TryGetSingle(MovementVisualState state, out Sprite sprite)
            {
                sprite = state == MovementVisualState.Walk ? singleWalk : singleIdle;
                return sprite != null;
            }
        }
    }

    /// <summary>
    ///     Public contract exposed by <see cref="PlayerSpriteController"/> so other systems only depend on sprite-specific behaviour.
    /// </summary>
    public interface IPlayerSpriteController
    {
        /// <summary>Gets or sets whether sprite swapping is temporarily frozen.</summary>
        bool FreezeSprite { get; set; }

        /// <summary>Gets or sets whether right-facing sprites should be mirrored for left facings.</summary>
        bool UseFlipXForLeft { get; set; }

        /// <summary>Gets or sets whether left-facing sprites should be mirrored for right facings.</summary>
        bool UseFlipXForRight { get; set; }

        /// <summary>Applies the supplied movement visuals to the animator and sprite renderer.</summary>
        void ApplyMovementVisuals(Direction8 direction, bool isMoving);

        /// <summary>Gets the configured frames-per-second for idle animations.</summary>
        float IdleAnimationFps { get; }

        /// <summary>Gets the configured frames-per-second for walking animations.</summary>
        float WalkAnimationFps { get; }

        /// <summary>Retrieves the configured frame count for the supplied direction/state, ignoring fallback logic.</summary>
        int GetFrameCount(Direction8 direction, bool isMoving);
    }
}
