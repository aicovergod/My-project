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
    }
}

