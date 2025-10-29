using System;
using System.Collections.Generic;
using UnityEngine;
using Player;

namespace NPC.Navigation
{
    /// <summary>
    /// Component that links a navmesh streaming zone to the chunk identifiers baked by <see cref="NavGridChunkDefinition"/>.
    /// Runtime systems can query the zone to determine which chunk assets should be loaded as the player enters
    /// or leaves the region.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("NPC/Navigation/Nav Grid Streaming Zone")]
    public sealed class NavGridStreamingZone : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Unique identifier used by streaming systems to match runtime zone events to baked data.")]
        private string zoneId;

        [SerializeField]
        [Tooltip("Chunk identifiers (chunk_X_Y) baked to Resources/NavGridChunks that should stream when this zone is active.")]
        private List<string> chunkIds = new List<string>();

        [SerializeField]
        [Tooltip("When enabled, draws the 2D collider bounds for quick visual verification in the scene view.")]
        private bool drawBoundsGizmo;

        [SerializeField]
        [Tooltip("Color used when drawing the optional gizmo bounds in the editor.")]
        private Color gizmoColor = new Color(0f, 0.6f, 1f, 0.3f);

        /// <summary>
        /// Unique identifier for the navmesh zone.
        /// </summary>
        public string ZoneId => zoneId;

        /// <summary>
        /// Chunk identifiers that should stream alongside this zone.
        /// </summary>
        public IReadOnlyList<string> ChunkIds => chunkIds;

        /// <summary>
        /// Returns <c>true</c> when the zone maps to at least one chunk identifier.
        /// </summary>
        public bool HasChunks => chunkIds != null && chunkIds.Count > 0;

        /// <summary>
        /// Tests whether the supplied chunk identifier is associated with this zone.
        /// </summary>
        public bool ContainsChunk(string chunkId)
        {
            if (string.IsNullOrEmpty(chunkId) || chunkIds == null)
            {
                return false;
            }

            return chunkIds.Contains(chunkId);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!IsPlayerCollider(other))
            {
                return;
            }

            NavGridStreamingService.Instance?.ActivateZone(zoneId, chunkIds);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!IsPlayerCollider(other))
            {
                return;
            }

            NavGridStreamingService.Instance?.DeactivateZone(zoneId);
        }

        private static bool IsPlayerCollider(Collider2D collider)
        {
            if (collider == null)
            {
                return false;
            }

            if (collider.CompareTag("Player"))
            {
                return true;
            }

            return collider.GetComponentInParent<PlayerMover>() != null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            zoneId = string.IsNullOrWhiteSpace(zoneId) ? name : zoneId.Trim();
            if (chunkIds == null)
            {
                chunkIds = new List<string>();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = chunkIds.Count - 1; i >= 0; i--)
            {
                string id = chunkIds[i];
                if (string.IsNullOrWhiteSpace(id))
                {
                    chunkIds.RemoveAt(i);
                    continue;
                }

                string trimmed = id.Trim();
                if (!seen.Add(trimmed))
                {
                    chunkIds.RemoveAt(i);
                    continue;
                }

                chunkIds[i] = trimmed;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawBoundsGizmo)
            {
                return;
            }

            Collider2D collider2D = GetComponent<Collider2D>();
            if (collider2D == null)
            {
                return;
            }

            Gizmos.color = gizmoColor;
            Bounds bounds = collider2D.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
#endif
    }
}
