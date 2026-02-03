using UnityEngine;

namespace Util
{
    /// <summary>
    ///     Provides helper methods for working with Unity layers. Centralising layer assignment keeps
    ///     UI, combat, and gathering overlays in sync and avoids re-implementing recursive walks.
    /// </summary>
    public static class LayerUtility
    {
        /// <summary>
        ///     Applies the supplied layer to the transform and all of its descendants. Safely ignores
        ///     null references so callers can forward optional objects without additional guards.
        /// </summary>
        /// <param name="root">Transform whose hierarchy should receive the new layer.</param>
        /// <param name="layer">Layer index resolved via <see cref="LayerMask.NameToLayer"/>.</param>
        public static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;

            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child != null)
                    SetLayerRecursively(child, layer);
            }
        }
    }
}
