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

        // References to the generated UI icons so they can be updated/destroyed
        internal RectTransform smallIcon;
        internal RectTransform bigIcon;

        private void OnEnable()
        {
            Minimap.Instance?.Register(this);
        }

        private void OnDisable()
        {
            Minimap.Instance?.Unregister(this);
        }
    }
}

