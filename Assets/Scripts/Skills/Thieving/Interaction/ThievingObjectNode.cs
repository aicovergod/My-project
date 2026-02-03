using System.Collections;
using UnityEngine;
using Skills.Thieving.Data;
using Util;

namespace Skills.Thieving
{
    /// <summary>
    ///     MonoBehaviour representing a thievable world object such as a stall. Handles depletion timers and exposes helper
    ///     properties so the controller can determine interaction points and availability.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThievingObjectNode : MonoBehaviour
    {
        [SerializeField, Tooltip("Definition describing the loot and timing for this node.")]
        private ThievingObjectDefinition definition;

        [SerializeField, Tooltip("Collider that defines the interaction bounds.")]
        private Collider2D interactionCollider;

        [SerializeField, Tooltip("Optional root object used to hide the visuals while the stall is depleted.")]
        private GameObject visualRoot;

        private bool isDepleted;
        private Coroutine respawnRoutine;

        /// <summary>
        ///     True when the stall is currently depleted and cannot be interacted with.
        /// </summary>
        public bool IsDepleted => isDepleted;

        /// <summary>
        ///     World position used for interaction prompts and HUD anchoring.
        /// </summary>
        public Vector3 InteractionPoint => interactionCollider != null
            ? interactionCollider.bounds.center
            : transform.position;

        /// <summary>
        ///     Definition backing the node. May be null if the node has not been configured.
        /// </summary>
        public ThievingObjectDefinition Definition => definition;

        private void Awake()
        {
            if (interactionCollider == null)
                interactionCollider = GetComponent<Collider2D>();
        }

        private void OnValidate()
        {
            if (interactionCollider == null)
                interactionCollider = GetComponent<Collider2D>();
        }

        /// <summary>
        ///     Invoked by <see cref="ThievingSkill"/> when the node has been successfully looted.
        /// </summary>
        public void OnStolen()
        {
            if (isDepleted)
                return;

            isDepleted = true;
            SetColliderEnabled(false);
            SetVisualsActive(false);

            if (respawnRoutine != null)
                StopCoroutine(respawnRoutine);
            respawnRoutine = StartCoroutine(HandleDepletion());
        }

        private IEnumerator HandleDepletion()
        {
            float depletionDuration = definition != null
                ? definition.DepletionTicks * Ticker.TickDuration
                : 0f;
            float respawnDelay = definition != null
                ? definition.RespawnTicks * Ticker.TickDuration
                : 0f;

            if (depletionDuration > 0f)
                yield return new WaitForSeconds(depletionDuration);

            SetVisualsActive(false);
            SetColliderEnabled(false);

            if (respawnDelay > 0f)
                yield return new WaitForSeconds(respawnDelay);

            isDepleted = false;
            SetVisualsActive(true);
            SetColliderEnabled(true);
            respawnRoutine = null;
        }

        private void SetColliderEnabled(bool enabled)
        {
            if (interactionCollider == null)
                return;

            interactionCollider.enabled = enabled;
        }

        private void SetVisualsActive(bool active)
        {
            if (visualRoot != null)
            {
                visualRoot.SetActive(active);
            }
            else if (TryGetComponent(out SpriteRenderer sprite))
            {
                sprite.enabled = active;
            }
        }
    }
}
