using UI;
using UnityEngine;

namespace Pets
{
    /// <summary>
    ///     Maintains a floating-text anchor for pet feedback so pets can emit contextual messages.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PetFloatingTextController : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Transform floatingTextAnchor;
        [SerializeField] private float verticalPadding = 0.25f;
        [SerializeField] private Vector3 manualWorldOffset;

        /// <summary>
        ///     Gets the transform used as the floating-text anchor for this pet.
        /// </summary>
        public Transform FloatingTextAnchor => floatingTextAnchor;

        /// <summary>
        ///     Gets the current world-space anchor position, falling back to the pet root when no anchor exists.
        /// </summary>
        public Vector3 AnchorWorldPosition => floatingTextAnchor != null ? floatingTextAnchor.position : transform.position;

        /// <summary>
        ///     Ensures dependencies are wired and the anchor is positioned when the component wakes.
        /// </summary>
        private void Awake()
        {
            PrepareAnchor();
        }

        /// <summary>
        ///     Reapplies anchor preparation whenever the component is enabled so pet spawn/restore flows stay aligned.
        /// </summary>
        private void OnEnable()
        {
            PrepareAnchor();
        }

        /// <summary>
        ///     Keeps serialized references valid while editing prefabs or scene instances.
        /// </summary>
        private void OnValidate()
        {
            PrepareAnchor();
        }

        /// <summary>
        ///     Updates the floating-text anchor to sit above the pet sprite each frame.
        /// </summary>
        private void LateUpdate()
        {
            RefreshAnchorPosition();
        }

        /// <summary>
        ///     Attempts to display a floating text message at the pet's anchor using the shared UI helper.
        /// </summary>
        /// <param name="message">Message to display.</param>
        /// <param name="color">Optional colour override.</param>
        /// <param name="size">Optional scale override.</param>
        /// <param name="background">Optional sprite displayed behind the text.</param>
        /// <returns>True when the message is shown successfully.</returns>
        public bool TryShowMessage(string message, Color? color = null, float? size = null, Sprite background = null)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            CacheSpriteRenderer();
            EnsureAnchor();
            if (floatingTextAnchor == null)
                return false;

            RefreshAnchorPosition();
            FloatingText.Show(message, AnchorWorldPosition, color, size, background);
            return true;
        }

        /// <summary>
        ///     Repositions the floating-text anchor above the sprite bounds, applying manual offsets when configured.
        /// </summary>
        public void RefreshAnchorPosition()
        {
            CacheSpriteRenderer();
            EnsureAnchor();
            if (floatingTextAnchor == null)
                return;

            Vector3 worldCenter = transform.position;
            float spriteHalfHeight = 0f;

            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                var bounds = spriteRenderer.bounds;
                worldCenter = bounds.center;
                spriteHalfHeight = bounds.extents.y;
            }

            Vector3 worldPosition = worldCenter + Vector3.up * (spriteHalfHeight + verticalPadding) + manualWorldOffset;
            floatingTextAnchor.position = worldPosition;
            floatingTextAnchor.rotation = Quaternion.identity;
            floatingTextAnchor.localScale = Vector3.one;
        }

        /// <summary>
        ///     Caches the sprite renderer reference when missing so bounds can be evaluated reliably.
        /// </summary>
        private void CacheSpriteRenderer()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>
        ///     Ensures an anchor transform exists and is parented correctly beneath the pet root.
        /// </summary>
        private void EnsureAnchor()
        {
            if (floatingTextAnchor == null)
            {
                var existing = transform.Find("FloatingTextAnchor");
                if (existing != null)
                {
                    floatingTextAnchor = existing;
                }
                else
                {
                    var anchorGO = new GameObject("FloatingTextAnchor");
                    floatingTextAnchor = anchorGO.transform;
                    floatingTextAnchor.SetParent(transform, false);
                }
            }

            if (floatingTextAnchor == null)
                return;

            if (floatingTextAnchor.parent != transform)
                floatingTextAnchor.SetParent(transform, false);

            floatingTextAnchor.localPosition = Vector3.zero;
            floatingTextAnchor.localRotation = Quaternion.identity;
            floatingTextAnchor.localScale = Vector3.one;
        }

        /// <summary>
        ///     Sets up cached references and anchor placement, used by all lifecycle entry points.
        /// </summary>
        private void PrepareAnchor()
        {
            CacheSpriteRenderer();
            EnsureAnchor();
            RefreshAnchorPosition();
        }
    }
}
