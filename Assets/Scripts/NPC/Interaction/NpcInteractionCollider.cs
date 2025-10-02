using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Ensures every NPC that exposes interaction scripts keeps a trigger collider active so navigation
    /// can pass through while interaction clicks remain responsive.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class NpcInteractionCollider : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Collider used to detect player interaction clicks.")]
        private Collider2D interactionCollider;

        /// <summary>
        /// Provides read-only access to the cached collider so other scripts can reference it if necessary.
        /// </summary>
        public Collider2D InteractionCollider => interactionCollider;

        private void Reset()
        {
            CacheCollider();
            EnsureColliderConfigured();
        }

        private void Awake()
        {
            CacheCollider();
            EnsureColliderConfigured();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheCollider();
            EnsureColliderConfigured();
        }
#endif

        /// <summary>
        /// Locates and stores the Collider2D component present on this GameObject.
        /// </summary>
        private void CacheCollider()
        {
            if (interactionCollider != null)
                return;

            interactionCollider = GetComponent<Collider2D>();
            if (interactionCollider == null)
            {
                Debug.LogError("NpcInteractionCollider requires a Collider2D component on the same GameObject.", this);
            }
        }

        /// <summary>
        /// Guarantees the collider is enabled and configured as a trigger so it does not obstruct movement.
        /// </summary>
        private void EnsureColliderConfigured()
        {
            if (interactionCollider == null)
                return;

            if (!interactionCollider.enabled)
                interactionCollider.enabled = true;

            if (!interactionCollider.isTrigger)
                interactionCollider.isTrigger = true;
        }
    }
}
