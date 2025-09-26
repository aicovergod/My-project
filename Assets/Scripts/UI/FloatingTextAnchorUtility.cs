using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// Utility helpers that resolve the ideal world position for floating combat text. The
    /// resolution order mirrors OSRS behaviour: prefer a dedicated <c>FloatingTextAnchor</c>,
    /// fall back to sprite or collider bounds, then finally apply a configurable upward
    /// offset to the root transform.
    /// </summary>
    public static class FloatingTextAnchorUtility
    {
        /// <summary>
        /// Cached data used to avoid repeated hierarchy scans for floating text anchors.
        /// </summary>
        public struct AnchorCache
        {
            internal Transform anchor;
            internal bool hasAttemptedSearch;
            internal int anchorInstanceId;

            /// <summary>Invalidate any cached anchor data so a fresh lookup occurs.</summary>
            public void Invalidate()
            {
                anchor = null;
                hasAttemptedSearch = false;
                anchorInstanceId = 0;
            }
        }

        private const string AnchorName = "FloatingTextAnchor";

        /// <summary>
        /// Resolve the best floating text position, maintaining cache state for the supplied
        /// transform so future calls reuse the same anchor when possible.
        /// </summary>
        /// <param name="root">The transform that owns the combat target.</param>
        /// <param name="fallbackOffset">
        /// Upward offset applied when no anchor, sprite, or collider bounds are available.
        /// </param>
        /// <param name="cache">Mutable cache state tied to the target transform.</param>
        /// <returns>The world position where the hitsplat should appear.</returns>
        public static Vector3 ResolveAnchorPosition(Transform root, float fallbackOffset, ref AnchorCache cache)
        {
            if (root == null)
                return Vector3.zero;

            // When the cached anchor is valid we can skip the more expensive hierarchy search.
            if (cache.anchor != null)
            {
                if (cache.anchor)
                    return cache.anchor.position;

                // The previously cached anchor has been destroyed. Clear cached data so we
                // perform a fresh lookup and avoid stale references.
                cache.Invalidate();
            }
            else if (cache.anchorInstanceId != 0)
            {
                // Unity null comparison can become true when a cached anchor is destroyed, but
                // we still want to attempt a fresh lookup in case a replacement exists.
                cache.Invalidate();
            }

            if (!cache.hasAttemptedSearch)
            {
                cache.anchor = FindFloatingTextAnchor(root);
                cache.hasAttemptedSearch = true;
                cache.anchorInstanceId = cache.anchor != null ? cache.anchor.GetInstanceID() : 0;
                if (cache.anchor != null)
                    return cache.anchor.position;
            }

            if (TryGetSpriteBounds(root, out var spriteBounds))
                return spriteBounds.center + Vector3.up * spriteBounds.extents.y;

            if (TryGetColliderBounds(root, out var colliderBounds))
                return colliderBounds.center + Vector3.up * colliderBounds.extents.y;

            return root.position + Vector3.up * fallbackOffset;
        }

        /// <summary>
        /// Resolve the best floating text position while storing cache state inside the
        /// provided dictionary. This variant is convenient for systems that track many
        /// concurrent targets, such as the player or pets attacking multiple enemies.
        /// </summary>
        /// <param name="root">The transform that owns the combat target.</param>
        /// <param name="fallbackOffset">
        /// Upward offset applied when no anchor, sprite, or collider bounds are available.
        /// </param>
        /// <param name="cache">
        /// Dictionary keyed by the target transform that stores anchor cache data.
        /// </param>
        /// <returns>The world position where the hitsplat should appear.</returns>
        public static Vector3 ResolveAnchorPosition(Transform root, float fallbackOffset, IDictionary<Transform, AnchorCache> cache)
        {
            if (root == null)
                return Vector3.zero;

            if (cache == null)
            {
                var tempCache = default(AnchorCache);
                return ResolveAnchorPosition(root, fallbackOffset, ref tempCache);
            }

            if (!cache.TryGetValue(root, out var anchorCache))
                anchorCache = default;

            var position = ResolveAnchorPosition(root, fallbackOffset, ref anchorCache);
            cache[root] = anchorCache;
            return position;
        }

        /// <summary>Search the hierarchy for a child named <c>FloatingTextAnchor</c>.</summary>
        private static Transform FindFloatingTextAnchor(Transform root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i];
                if (child != null && child != root && child.name == AnchorName)
                    return child;
            }
            return null;
        }

        /// <summary>Attempt to pull sprite renderer bounds from the target hierarchy.</summary>
        private static bool TryGetSpriteBounds(Transform root, out Bounds bounds)
        {
            var renderer = root.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null)
            {
                bounds = renderer.bounds;
                return true;
            }

            bounds = default;
            return false;
        }

        /// <summary>Attempt to pull collider bounds from the target hierarchy.</summary>
        private static bool TryGetColliderBounds(Transform root, out Bounds bounds)
        {
            var collider2D = root.GetComponentInChildren<Collider2D>();
            if (collider2D != null)
            {
                bounds = collider2D.bounds;
                return true;
            }

            var collider3D = root.GetComponentInChildren<Collider>();
            if (collider3D != null)
            {
                bounds = collider3D.bounds;
                return true;
            }

            bounds = default;
            return false;
        }
    }
}
