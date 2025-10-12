using UnityEngine;
using UnityEngine.UI;

namespace UI.Utilities
{
    /// <summary>
    /// Provides helpers for consistently applying highlight colours to selectable UI buttons.
    /// </summary>
    public static class ButtonHighlightUtility
    {
        /// <summary>
        /// Applies the provided colour palette to the target button based on the current selection state.
        /// </summary>
        /// <param name="button">Button whose <see cref="ColorBlock"/> should be updated.</param>
        /// <param name="selected">If <c>true</c>, the selected colour is applied; otherwise the default colour is used.</param>
        /// <param name="selectedColor">Colour used when <paramref name="selected"/> is <c>true</c>.</param>
        /// <param name="defaultColor">Colour used when <paramref name="selected"/> is <c>false</c>.</param>
        public static void ApplySelectedColor(Button button, bool selected, Color selectedColor, Color defaultColor)
        {
            if (button == null)
                return;

            // Retrieve the colour block, swap the primary states to the requested colour, and reapply the block.
            var colors = button.colors;
            var targetColor = selected ? selectedColor : defaultColor;
            colors.normalColor = targetColor;
            colors.highlightedColor = targetColor;
            colors.selectedColor = targetColor;
            colors.pressedColor = targetColor;
            button.colors = colors;
        }
    }
}
