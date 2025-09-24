using UnityEngine;

namespace Skills.Common.UI
{
    /// <summary>
    /// Provides a shared rainbow gradient for skilling HUD progress bars so each bar smoothly
    /// transitions through the traditional red-to-violet RuneScape spectrum as the player advances.
    /// </summary>
    public static class SkillingProgressColorGradient
    {
        /// <summary>
        /// Cached rainbow colours ordered from empty (red) to full (violet). The list mirrors the
        /// classic OSRS skilling feedback palette while ensuring each segment blends smoothly.
        /// </summary>
        private static readonly Color[] RainbowStops =
        {
            new Color(1f, 0f, 0f),        // Red
            new Color(1f, 0.49803922f, 0f), // Orange (#FF7F00)
            new Color(1f, 1f, 0f),        // Yellow
            new Color(0f, 1f, 0f),        // Green
            new Color(0f, 0f, 1f),        // Blue
            new Color(0.29411766f, 0f, 0.50980395f), // Indigo (#4B0082)
            new Color(0.56078434f, 0f, 1f) // Violet (#8F00FF)
        };

        /// <summary>
        /// Evaluates the rainbow gradient for the provided normalised progress value.
        /// </summary>
        /// <param name="normalizedProgress">Progress between 0 (start) and 1 (complete).</param>
        /// <returns>The colour that corresponds to the requested point along the rainbow.</returns>
        public static Color Evaluate(float normalizedProgress)
        {
            if (RainbowStops.Length == 0)
                return Color.white;

            float clamped = Mathf.Clamp01(normalizedProgress);
            float scaled = clamped * (RainbowStops.Length - 1);
            int index = Mathf.FloorToInt(scaled);

            if (index >= RainbowStops.Length - 1)
                return RainbowStops[RainbowStops.Length - 1];

            float localT = scaled - index;
            return Color.Lerp(RainbowStops[index], RainbowStops[index + 1], localT);
        }
    }
}
