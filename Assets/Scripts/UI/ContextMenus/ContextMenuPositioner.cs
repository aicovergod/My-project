using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI.ContextMenus
{
    /// <summary>
    /// Shared helper that positions context menus relative to the pointer while clamping them
    /// inside the visible screen bounds. Centralises the world-corner calculation that multiple
    /// menus previously duplicated.
    /// </summary>
    public static class ContextMenuPositioner
    {
        private static readonly Vector3[] SharedWorldCorners = new Vector3[4];

        /// <summary>
        /// Positions the supplied menu rect using the provided screen-space pointer coordinate.
        /// </summary>
        /// <param name="menuRect">RectTransform that represents the menu root.</param>
        /// <param name="canvas">Canvas containing the menu.</param>
        /// <param name="canvasCamera">Camera used for non-overlay render modes.</param>
        /// <param name="pointerScreenPosition">Screen-space pointer position used for anchoring.</param>
        public static void PositionMenu(
            RectTransform menuRect,
            Canvas canvas,
            Camera canvasCamera,
            Vector2 pointerScreenPosition)
        {
            PositionMenuInternal(menuRect, canvas, canvasCamera, pointerScreenPosition, SharedWorldCorners);
        }

        /// <summary>
        /// Positions the menu rect using a callback that returns the desired pointer position.
        /// Intended for menus that cache their pointer coordinate internally and simply need to
        /// expose a getter.
        /// </summary>
        /// <param name="menuRect">RectTransform that represents the menu root.</param>
        /// <param name="canvas">Canvas containing the menu.</param>
        /// <param name="canvasCamera">Camera used for non-overlay render modes.</param>
        /// <param name="pointerProvider">Callback returning the screen-space pointer coordinate.</param>
        public static void PositionMenu(
            RectTransform menuRect,
            Canvas canvas,
            Camera canvasCamera,
            Func<Vector2> pointerProvider)
        {
            if (pointerProvider == null)
                return;

            PositionMenu(menuRect, canvas, canvasCamera, pointerProvider());
        }

        /// <summary>
        /// Positions the menu rect using a pointer callback and a caller-supplied world corner buffer.
        /// Use this overload when the menu caches its pointer position but also wants to reuse an
        /// existing world-corner array for efficiency.
        /// </summary>
        /// <param name="menuRect">RectTransform that represents the menu root.</param>
        /// <param name="canvas">Canvas containing the menu.</param>
        /// <param name="canvasCamera">Camera used for non-overlay render modes.</param>
        /// <param name="pointerProvider">Callback returning the screen-space pointer coordinate.</param>
        /// <param name="worldCornerBuffer">Reusable world-corner buffer (must contain four entries).</param>
        public static void PositionMenu(
            RectTransform menuRect,
            Canvas canvas,
            Camera canvasCamera,
            Func<Vector2> pointerProvider,
            Vector3[] worldCornerBuffer)
        {
            if (pointerProvider == null)
                return;

            PositionMenu(menuRect, canvas, canvasCamera, pointerProvider(), worldCornerBuffer);
        }

        /// <summary>
        /// Positions the menu rect using the provided pointer coordinate and a caller-supplied
        /// corner buffer. Passing a buffer allows menus to reuse an existing array to avoid
        /// allocations when they reposition frequently.
        /// </summary>
        /// <param name="menuRect">RectTransform that represents the menu root.</param>
        /// <param name="canvas">Canvas containing the menu.</param>
        /// <param name="canvasCamera">Camera used for non-overlay render modes.</param>
        /// <param name="pointerScreenPosition">Screen-space pointer position used for anchoring.</param>
        /// <param name="worldCornerBuffer">Reusable world-corner buffer (must contain four entries).</param>
        public static void PositionMenu(
            RectTransform menuRect,
            Canvas canvas,
            Camera canvasCamera,
            Vector2 pointerScreenPosition,
            Vector3[] worldCornerBuffer)
        {
            PositionMenuInternal(menuRect, canvas, canvasCamera, pointerScreenPosition, worldCornerBuffer);
        }

        private static void PositionMenuInternal(
            RectTransform menuRect,
            Canvas canvas,
            Camera canvasCamera,
            Vector2 pointerScreenPosition,
            Vector3[] worldCornerBuffer)
        {
            if (menuRect == null || canvas == null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(menuRect);

            Vector3[] corners = EnsureCornerBuffer(worldCornerBuffer);
            menuRect.GetWorldCorners(corners);

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 screenCorner = RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[i]);
                if (screenCorner.x < minX)
                    minX = screenCorner.x;
                if (screenCorner.x > maxX)
                    maxX = screenCorner.x;
                if (screenCorner.y < minY)
                    minY = screenCorner.y;
                if (screenCorner.y > maxY)
                    maxY = screenCorner.y;
            }

            float width = Mathf.Max(0f, maxX - minX);
            float height = Mathf.Max(0f, maxY - minY);

            Vector2 clampedScreenPosition = pointerScreenPosition;
            float maxAllowedX = Mathf.Max(0f, Screen.width - width);
            clampedScreenPosition.x = Mathf.Clamp(clampedScreenPosition.x, 0f, maxAllowedX);
            clampedScreenPosition.y = Mathf.Clamp(clampedScreenPosition.y, height, Screen.height);

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                menuRect.position = new Vector3(clampedScreenPosition.x, clampedScreenPosition.y, menuRect.position.z);
                return;
            }

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(menuRect, clampedScreenPosition, canvasCamera, out Vector3 worldPosition))
            {
                menuRect.position = worldPosition;
            }
            else
            {
                menuRect.position = new Vector3(clampedScreenPosition.x, clampedScreenPosition.y, menuRect.position.z);
            }
        }

        private static Vector3[] EnsureCornerBuffer(Vector3[] buffer)
        {
            if (buffer != null && buffer.Length >= 4)
                return buffer;

            if (SharedWorldCorners != null && SharedWorldCorners.Length >= 4)
                return SharedWorldCorners;

            return new Vector3[4];
        }
    }
}
