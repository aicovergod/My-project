// Assets/Scripts/Player/Visuals/PlayerSpriteController.cs
using UnityEngine;
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
        [Tooltip("If assigned, these sprites will be applied directly each frame based on Dir/IsMoving. Leave null to rely on Animator clips.")]
        [SerializeField] private Sprite idleDown;
        [SerializeField] private Sprite idleDownRight;
        [SerializeField] private Sprite idleRight;
        [SerializeField] private Sprite idleUpRight;
        [SerializeField] private Sprite idleUp;
        [SerializeField] private Sprite idleUpLeft;
        [SerializeField] private Sprite idleLeft;
        [SerializeField] private Sprite idleDownLeft;

        [SerializeField] private Sprite walkDown;
        [SerializeField] private Sprite walkDownRight;
        [SerializeField] private Sprite walkRight;
        [SerializeField] private Sprite walkUpRight;
        [SerializeField] private Sprite walkUp;
        [SerializeField] private Sprite walkUpLeft;
        [SerializeField] private Sprite walkLeft;
        [SerializeField] private Sprite walkDownLeft;

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

            if (!TryResolveOverrideSprite(direction, isMoving, out Sprite desired, out bool flip))
                return;

            if (spriteRenderer.flipX != flip)
                spriteRenderer.flipX = flip;

            if (spriteRenderer.sprite != desired)
                spriteRenderer.sprite = desired;
        }

        /// <summary>
        ///     Attempts to resolve an override sprite for the supplied facing direction, including mirroring fallbacks.
        /// </summary>
        private bool TryResolveOverrideSprite(Direction8 direction, bool moving, out Sprite sprite, out bool flip)
        {
            foreach (var lookup in Direction8Utility.BuildSpriteFallbackOrder(direction, ShouldMirrorOverride))
            {
                if (TryGetSpriteForDirection(lookup.Direction, moving, out sprite))
                {
                    flip = lookup.FlipX;
                    return true;
                }
            }

            sprite = null;
            flip = false;
            return false;
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

        /// <summary>
        ///     Retrieves the idle and walking sprites for a given direction, preferring the matching state but falling back when required.
        /// </summary>
        private bool TryGetSpriteForDirection(Direction8 direction, bool moving, out Sprite sprite)
        {
            GetOverrideSprites(direction, out Sprite idleSprite, out Sprite walkSprite);
            if (moving)
            {
                if (walkSprite != null)
                {
                    sprite = walkSprite;
                    return true;
                }

                if (idleSprite != null)
                {
                    sprite = idleSprite;
                    return true;
                }
            }
            else
            {
                if (idleSprite != null)
                {
                    sprite = idleSprite;
                    return true;
                }

                if (walkSprite != null)
                {
                    sprite = walkSprite;
                    return true;
                }
            }

            sprite = null;
            return false;
        }

        /// <summary>Populates the idle and walk sprite references for a supplied direction.</summary>
        private void GetOverrideSprites(Direction8 direction, out Sprite idleSprite, out Sprite walkSprite)
        {
            idleSprite = null;
            walkSprite = null;

            switch (direction)
            {
                case Direction8.Down:
                    idleSprite = idleDown;
                    walkSprite = walkDown;
                    break;
                case Direction8.DownRight:
                    idleSprite = idleDownRight;
                    walkSprite = walkDownRight;
                    break;
                case Direction8.Right:
                    idleSprite = idleRight;
                    walkSprite = walkRight;
                    break;
                case Direction8.UpRight:
                    idleSprite = idleUpRight;
                    walkSprite = walkUpRight;
                    break;
                case Direction8.Up:
                    idleSprite = idleUp;
                    walkSprite = walkUp;
                    break;
                case Direction8.UpLeft:
                    idleSprite = idleUpLeft;
                    walkSprite = walkUpLeft;
                    break;
                case Direction8.Left:
                    idleSprite = idleLeft;
                    walkSprite = walkLeft;
                    break;
                case Direction8.DownLeft:
                    idleSprite = idleDownLeft;
                    walkSprite = walkDownLeft;
                    break;
            }
        }

        /// <summary>Retrieves the idle override sprite for the supplied facing direction.</summary>
        public Sprite GetIdleSprite(Direction8 direction)
        {
            return direction switch
            {
                Direction8.Down => idleDown,
                Direction8.DownRight => idleDownRight,
                Direction8.Right => idleRight,
                Direction8.UpRight => idleUpRight,
                Direction8.Up => idleUp,
                Direction8.UpLeft => idleUpLeft,
                Direction8.Left => idleLeft,
                Direction8.DownLeft => idleDownLeft,
                _ => null
            };
        }

        /// <summary>Assigns the idle override sprite for the supplied facing direction.</summary>
        public void SetIdleSprite(Direction8 direction, Sprite sprite)
        {
            switch (direction)
            {
                case Direction8.Down:
                    idleDown = sprite;
                    break;
                case Direction8.DownRight:
                    idleDownRight = sprite;
                    break;
                case Direction8.Right:
                    idleRight = sprite;
                    break;
                case Direction8.UpRight:
                    idleUpRight = sprite;
                    break;
                case Direction8.Up:
                    idleUp = sprite;
                    break;
                case Direction8.UpLeft:
                    idleUpLeft = sprite;
                    break;
                case Direction8.Left:
                    idleLeft = sprite;
                    break;
                case Direction8.DownLeft:
                    idleDownLeft = sprite;
                    break;
            }
        }

        /// <summary>Retrieves the walking override sprite for the supplied facing direction.</summary>
        public Sprite GetWalkSprite(Direction8 direction)
        {
            return direction switch
            {
                Direction8.Down => walkDown,
                Direction8.DownRight => walkDownRight,
                Direction8.Right => walkRight,
                Direction8.UpRight => walkUpRight,
                Direction8.Up => walkUp,
                Direction8.UpLeft => walkUpLeft,
                Direction8.Left => walkLeft,
                Direction8.DownLeft => walkDownLeft,
                _ => null
            };
        }

        /// <summary>Assigns the walking override sprite for the supplied facing direction.</summary>
        public void SetWalkSprite(Direction8 direction, Sprite sprite)
        {
            switch (direction)
            {
                case Direction8.Down:
                    walkDown = sprite;
                    break;
                case Direction8.DownRight:
                    walkDownRight = sprite;
                    break;
                case Direction8.Right:
                    walkRight = sprite;
                    break;
                case Direction8.UpRight:
                    walkUpRight = sprite;
                    break;
                case Direction8.Up:
                    walkUp = sprite;
                    break;
                case Direction8.UpLeft:
                    walkUpLeft = sprite;
                    break;
                case Direction8.Left:
                    walkLeft = sprite;
                    break;
                case Direction8.DownLeft:
                    walkDownLeft = sprite;
                    break;
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
    }
}
