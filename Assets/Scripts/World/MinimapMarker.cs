using UnityEngine;

namespace World
{
    /// <summary>
    /// Component that registers itself with the <see cref="Minimap"/> so an
    /// icon can be displayed at this object's world position.
    /// </summary>
    public class MinimapMarker : MonoBehaviour
    {
        public enum MarkerType
        {
            Bank,
            Shop,
            Ore,
            Tree
        }

        [Tooltip("Type of icon to display on the minimap.")]
        public MarkerType type = MarkerType.Bank;

        [SerializeField]
        [Tooltip("Manually assigned icon used on the small minimap.")]
        private RectTransform smallIcon;

        [SerializeField]
        [Tooltip("Manually assigned icon used on the expanded minimap.")]
        private RectTransform bigIcon;

        /// <summary>
        ///     Provides the small minimap icon to the <see cref="Minimap"/> for manual positioning.
        /// </summary>
        internal RectTransform SmallIcon => smallIcon;

        /// <summary>
        ///     Provides the expanded minimap icon to the <see cref="Minimap"/> for manual positioning.
        /// </summary>
        internal RectTransform BigIcon => bigIcon;

        private void OnEnable()
        {
            SetIconActive(true);
            Minimap.Instance?.Register(this);
        }

        private void OnDisable()
        {
            Minimap.Instance?.Unregister(this);
            SetIconActive(false);
        }

        /// <summary>
        ///     Toggles the visibility of the manually assigned icon references.
        /// </summary>
        /// <param name="active">Whether the icons should be visible.</param>
        private void SetIconActive(bool active)
        {
            if (smallIcon != null)
                smallIcon.gameObject.SetActive(active);

            if (bigIcon != null)
                bigIcon.gameObject.SetActive(active);
        }
    }
}

