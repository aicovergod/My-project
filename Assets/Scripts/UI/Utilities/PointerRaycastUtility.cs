using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UI.Utilities
{
    /// <summary>
    /// Provides helper methods for querying the active <see cref="EventSystem"/> and determining whether
    /// the current pointer location is blocked by UI elements. Physics raycasters are filtered out so
    /// world colliders rendered through GraphicRaycasters do not incorrectly consume UI interaction.
    /// </summary>
    public static class PointerRaycastUtility
    {
        /// <summary>
        /// Cached buffer used to store raycast results and avoid per-call allocations.
        /// </summary>
        private static readonly List<RaycastResult> RaycastResults = new List<RaycastResult>(8);

        /// <summary>
        /// Determines whether the supplied screen position is currently hovering a UI element that should
        /// block world interaction. Physics and Physics2D raycasters are ignored so world-space colliders
        /// rendered via the UI system do not count as UI blockers.
        /// </summary>
        public static bool IsPointerOverBlockingUI(Vector2 screenPosition)
        {
            return TryGetFilteredUiRaycasts(screenPosition, null);
        }

        /// <summary>
        /// Performs a UI raycast using the active event system, filtering out hits from physics raycasters
        /// and optionally returning the filtered results.
        /// </summary>
        /// <param name="screenPosition">Screen-space pointer position to query.</param>
        /// <param name="filteredResults">
        /// Optional list that will be populated with UI hits that should block world interaction. Provide a
        /// reusable buffer when tooling or diagnostics need insight into the UI elements beneath the cursor.
        /// </param>
        /// <returns>True when the pointer is over a UI element that should block world interaction.</returns>
        public static bool TryGetFilteredUiRaycasts(Vector2 screenPosition, List<RaycastResult> filteredResults)
        {
            if (EventSystem.current == null)
            {
                filteredResults?.Clear();
                return false;
            }

            var pointerEventData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };

            RaycastResults.Clear();
            EventSystem.current.RaycastAll(pointerEventData, RaycastResults);

            filteredResults?.Clear();

            bool hasUiHit = false;

            for (int i = 0; i < RaycastResults.Count; i++)
            {
                RaycastResult result = RaycastResults[i];
                if (result.module is PhysicsRaycaster || result.module is Physics2DRaycaster)
                    continue;

                hasUiHit = true;
                filteredResults?.Add(result);
            }

            RaycastResults.Clear();

            return hasUiHit;
        }
    }
}
