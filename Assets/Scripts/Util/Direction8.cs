using System.Collections.Generic;
using UnityEngine;

namespace Util
{
    /// <summary>
    ///     Enumerates the eight principal facing directions in clockwise order starting from down.
    ///     This keeps player, NPC, pet, and combat systems aligned so orientation logic can be shared.
    /// </summary>
    public enum Direction8
    {
        Down = 0,
        DownRight = 1,
        Right = 2,
        UpRight = 3,
        Up = 4,
        UpLeft = 5,
        Left = 6,
        DownLeft = 7
    }

    /// <summary>
    ///     Helper utilities for working with <see cref="Direction8"/> values. Centralising these conversions ensures
    ///     every system uses a consistent clockwise mapping, sprite mirroring, and animator index calculation.
    /// </summary>
    public static class Direction8Utility
    {
        private const float DiagonalComponent = 0.70710678f; // sqrt(1/2)

        /// <summary>Precomputed unit vectors (clockwise, starting at down) for each enum entry.</summary>
        private static readonly Vector2[] DirectionVectors =
        {
            Vector2.down,
            new Vector2(DiagonalComponent, -DiagonalComponent),
            Vector2.right,
            new Vector2(DiagonalComponent, DiagonalComponent),
            Vector2.up,
            new Vector2(-DiagonalComponent, DiagonalComponent),
            Vector2.left,
            new Vector2(-DiagonalComponent, -DiagonalComponent)
        };

        /// <summary>
        ///     Converts a vector into one of the eight compass directions. Optional flags determine whether diagonals are
        ///     permitted and which direction should be returned when the vector magnitude is too small to classify.
        /// </summary>
        /// <param name="direction">Vector to evaluate. Zero vectors will fall back to <paramref name="fallback"/>.</param>
        /// <param name="allowDiagonals">When false the result is clamped to the four cardinal directions.</param>
        /// <param name="fallback">Direction returned if the vector has near-zero magnitude.</param>
        public static Direction8 FromVector(Vector2 direction, bool allowDiagonals = true, Direction8 fallback = Direction8.Down)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon)
                return fallback;

            Vector2 normalised = direction.normalized;
            if (!allowDiagonals)
            {
                float absX = Mathf.Abs(normalised.x);
                float absY = Mathf.Abs(normalised.y);
                if (absX > absY)
                    return normalised.x >= 0f ? Direction8.Right : Direction8.Left;
                if (absY > absX)
                    return normalised.y >= 0f ? Direction8.Up : Direction8.Down;
                return normalised.y >= 0f ? Direction8.Up : Direction8.Down;
            }

            float angle = Mathf.Atan2(normalised.x, -normalised.y) * Mathf.Rad2Deg;
            if (angle < 0f)
                angle += 360f;
            int index = Mathf.RoundToInt(angle / 45f) % 8;
            return (Direction8)index;
        }

        /// <summary>
        ///     Lightweight descriptor returned when building sprite fallback orders. It packages together the
        ///     source direction to probe for art and whether that lookup expects a horizontal flip when rendered.
        /// </summary>
        public readonly struct SpriteLookup
        {
            public SpriteLookup(Direction8 direction, bool flipX)
            {
                Direction = direction;
                FlipX = flipX;
            }

            /// <summary>The direction whose sprite data should be queried.</summary>
            public Direction8 Direction { get; }

            /// <summary>True when the resolved sprite should be mirrored horizontally.</summary>
            public bool FlipX { get; }
        }

        /// <summary>
        ///     Converts a vector into a direction while first enforcing a deadzone. Useful for analog sticks where small noise
        ///     should be ignored before computing the facing value.
        /// </summary>
        public static Direction8 FromVector(Vector2 direction, float deadzone, bool allowDiagonals, Direction8 fallback = Direction8.Down)
        {
            if (direction.magnitude <= deadzone)
                return fallback;
            return FromVector(direction, allowDiagonals, fallback);
        }

        /// <summary>Returns the normalised vector corresponding to the supplied direction.</summary>
        public static Vector2 ToVector(Direction8 direction)
        {
            return DirectionVectors[(int)direction];
        }

        /// <summary>
        ///     Returns a cardinal-only unit vector for the supplied direction. Diagonals are mapped to whichever axis is
        ///     dominant (matching the legacy facing logic used before the eight-way refactor).
        /// </summary>
        public static Vector2 ToCardinalVector(Direction8 direction)
        {
            return ToVector(SnapToFourWay(direction));
        }

        /// <summary>Snaps any direction to the closest of the four cardinal points.</summary>
        public static Direction8 SnapToFourWay(Direction8 direction)
        {
            Vector2 vec = DirectionVectors[(int)direction];
            float absX = Mathf.Abs(vec.x);
            float absY = Mathf.Abs(vec.y);

            if (absX > absY)
                return vec.x >= 0f ? Direction8.Right : Direction8.Left;
            if (absY > absX)
                return vec.y >= 0f ? Direction8.Up : Direction8.Down;
            return vec.y >= 0f ? Direction8.Up : Direction8.Down;
        }

        /// <summary>
        ///     Converts to the 0=Down, 1=Left, 2=Right, 3=Up index set used by legacy animator controllers.
        ///     Diagonal facings are mapped using their dominant axis so existing 4-way assets continue to work.
        /// </summary>
        public static int ToAnimatorIndex(Direction8 direction)
        {
            switch (SnapToFourWay(direction))
            {
                case Direction8.Left:
                    return 1;
                case Direction8.Right:
                    return 2;
                case Direction8.Up:
                    return 3;
                default:
                    return 0;
            }
        }

        /// <summary>Returns true if the direction is one of the diagonal variants.</summary>
        public static bool IsDiagonal(Direction8 direction)
        {
            return direction == Direction8.DownRight || direction == Direction8.UpRight ||
                   direction == Direction8.UpLeft || direction == Direction8.DownLeft;
        }

        /// <summary>Returns true if the direction points towards the right-hand side of the screen.</summary>
        public static bool IsFacingRight(Direction8 direction)
        {
            return direction == Direction8.Right || direction == Direction8.UpRight || direction == Direction8.DownRight;
        }

        /// <summary>Returns true if the direction points towards the left-hand side of the screen.</summary>
        public static bool IsFacingLeft(Direction8 direction)
        {
            return direction == Direction8.Left || direction == Direction8.UpLeft || direction == Direction8.DownLeft;
        }

        /// <summary>Returns true if the direction has an upward component.</summary>
        public static bool IsFacingUp(Direction8 direction)
        {
            return direction == Direction8.Up || direction == Direction8.UpLeft || direction == Direction8.UpRight;
        }

        /// <summary>Returns true if the direction has a downward component.</summary>
        public static bool IsFacingDown(Direction8 direction)
        {
            return direction == Direction8.Down || direction == Direction8.DownLeft || direction == Direction8.DownRight;
        }

        /// <summary>Mirrors a direction across the vertical axis (left-right swap).</summary>
        public static Direction8 MirrorHorizontally(Direction8 direction)
        {
            return Mirror(direction, mirrorX: true, mirrorY: false);
        }

        /// <summary>Mirrors a direction across the horizontal axis (up-down swap).</summary>
        public static Direction8 MirrorVertically(Direction8 direction)
        {
            return Mirror(direction, mirrorX: false, mirrorY: true);
        }

        /// <summary>Mirrors a direction across optional axes.</summary>
        public static Direction8 Mirror(Direction8 direction, bool mirrorX, bool mirrorY)
        {
            if (!mirrorX && !mirrorY)
                return direction;

            Vector2 vec = DirectionVectors[(int)direction];
            if (mirrorX)
                vec.x = -vec.x;
            if (mirrorY)
                vec.y = -vec.y;

            return FromVector(vec, allowDiagonals: true, fallback: direction);
        }

        /// <summary>
        ///     Extracts the horizontal and vertical cardinal components that make up a diagonal direction. When the
        ///     supplied direction is already cardinal the method simply returns that direction for both outputs.
        /// </summary>
        /// <param name="direction">Direction to decompose.</param>
        /// <param name="vertical">Receives the vertical component (Up/Down).</param>
        /// <param name="horizontal">Receives the horizontal component (Left/Right).</param>
        public static void GetDiagonalComponents(Direction8 direction, out Direction8 vertical, out Direction8 horizontal)
        {
            switch (direction)
            {
                case Direction8.UpRight:
                    vertical = Direction8.Up;
                    horizontal = Direction8.Right;
                    break;
                case Direction8.UpLeft:
                    vertical = Direction8.Up;
                    horizontal = Direction8.Left;
                    break;
                case Direction8.DownRight:
                    vertical = Direction8.Down;
                    horizontal = Direction8.Right;
                    break;
                case Direction8.DownLeft:
                    vertical = Direction8.Down;
                    horizontal = Direction8.Left;
                    break;
                default:
                    vertical = SnapToFourWay(direction);
                    horizontal = vertical;
                    break;
            }
        }

        /// <summary>
        ///     Enumerates a best-effort lookup order for sprite assets that can represent the supplied direction. Diagonal
        ///     facings fall back to their horizontal and vertical components before defaulting to down-facing art. The
        ///     <paramref name="shouldMirror"/> callback is used to decide whether a lookup should pull from the mirrored
        ///     counterpart and apply a horizontal flip.
        /// </summary>
        /// <param name="direction">Desired facing direction.</param>
        /// <param name="shouldMirror">Callback returning true when the specified direction prefers mirrored art.</param>
        /// <returns>Sequence of sprite lookups ordered by preference.</returns>
        public static IEnumerable<SpriteLookup> BuildSpriteFallbackOrder(Direction8 direction, System.Func<Direction8, bool> shouldMirror)
        {
            yield return new SpriteLookup(direction, false);

            Direction8 mirror = MirrorHorizontally(direction);
            bool mirrorAdded = false;
            if (mirror != direction && shouldMirror(direction))
            {
                yield return new SpriteLookup(mirror, true);
                mirrorAdded = true;
            }

            if (IsDiagonal(direction))
            {
                GetDiagonalComponents(direction, out var vertical, out var horizontal);

                yield return new SpriteLookup(horizontal, false);
                if (shouldMirror(horizontal))
                    yield return new SpriteLookup(MirrorHorizontally(horizontal), true);

                yield return new SpriteLookup(vertical, false);
                if (shouldMirror(vertical))
                    yield return new SpriteLookup(MirrorHorizontally(vertical), true);

                if (!mirrorAdded && mirror != direction && shouldMirror(mirror))
                    yield return new SpriteLookup(mirror, true);
            }
            else if (!mirrorAdded && mirror != direction && shouldMirror(mirror))
            {
                // If the mirrored direction stores the authoritative art, probe it before falling back to Down.
                yield return new SpriteLookup(mirror, true);
            }

            if (direction != Direction8.Down)
                yield return new SpriteLookup(Direction8.Down, false);
        }

        /// <summary>Maps the direction to its eight-way animator index (Down=0 ... DownLeft=7).</summary>
        public static int ToAnimatorIndex8(Direction8 direction)
        {
            return (int)direction;
        }
    }
}

