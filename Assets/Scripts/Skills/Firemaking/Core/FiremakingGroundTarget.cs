using Inventory;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Handles ground clicks for Firemaking. Translates pointer input into ignition attempts on the active skill.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class FiremakingGroundTarget : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private FiremakingSkill skill;
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private LayerMask fireSearchLayers;
        [SerializeField] private bool allowManualIgnitionPlacement;
        [SerializeField, Tooltip("Feedback shown when manual placement is disabled and the helper is invoked instead.")]
        private string manualPlacementDisabledMessage = "Use a tinderbox on the logs to light a fire.";

        /// <summary>
        ///     Locates the Firemaking skill, player inventory, and ensures the camera has a Physics2D raycaster for UI clicks.
        /// </summary>
        private void Awake()
        {
            if (skill == null)
                skill = FindObjectOfType<FiremakingSkill>();
            if (inventory == null && skill != null)
                inventory = skill.GetComponent<Inventory.Inventory>();
            if (worldCamera == null)
                worldCamera = Camera.main;

            if (worldCamera != null && worldCamera.GetComponent<Physics2DRaycaster>() == null)
                worldCamera.gameObject.AddComponent<Physics2DRaycaster>();
        }

        /// <summary>
        ///     Responds to left clicks by validating the selected logs and delegating to <see cref="FiremakingSkill"/>.
        /// </summary>
        /// <param name="eventData">Pointer payload provided by Unity's event system.</param>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            if (skill == null || inventory == null)
                return;

            if (inventory.selectedIndex < 0)
                return;

            var entry = inventory.GetSlot(inventory.selectedIndex);
            if (entry.item == null)
                return;

            var definition = skill.GetDefinitionForItem(entry.item.id);
            if (definition == null)
                return;

            Vector3 rawWorld = eventData.pointerCurrentRaycast.worldPosition;
            if (worldCamera != null && (rawWorld == Vector3.zero || float.IsNaN(rawWorld.x)))
            {
                // Fall back to a manual ray conversion when the event system does not provide a world position.
                Vector3 screenPoint = eventData.position;
                rawWorld = worldCamera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, worldCamera.nearClipPlane));
            }
            rawWorld.z = 0f;

            Vector3 snapped = SnapToWorld(rawWorld);

            FiremakingFire targetFire = null;
            if (fireSearchLayers.value != 0)
            {
                // Probe the configured fire layers so feeding an existing bonfire picks the correct component.
                Collider2D hit = Physics2D.OverlapPoint(snapped, fireSearchLayers);
                if (hit != null)
                    targetFire = hit.GetComponentInParent<FiremakingFire>() ?? hit.GetComponent<FiremakingFire>();
            }

            if (targetFire == null && !allowManualIgnitionPlacement)
            {
                if (skill != null && inventory != null && inventory.selectedIndex >= 0)
                {
                    if (!skill.BeginLightingFromInventory(inventory.selectedIndex, out var helperFailure) &&
                        !string.IsNullOrEmpty(helperFailure))
                    {
                        FloatingText.Show(helperFailure, snapped);
                    }
                }
                else if (!string.IsNullOrEmpty(manualPlacementDisabledMessage))
                {
                    FloatingText.Show(manualPlacementDisabledMessage, snapped);
                }

                return;
            }

            if (!skill.TryBeginLighting(inventory.selectedIndex, snapped, targetFire, out var failureReason))
            {
                if (!string.IsNullOrEmpty(failureReason))
                    FloatingText.Show(failureReason, snapped);
            }
        }

        /// <summary>
        ///     Snaps the incoming pointer position to the Firemaking grid rules so placement is consistent.
        /// </summary>
        /// <param name="rawWorld">Unsnapped pointer position.</param>
        /// <returns>Grid aligned position used for the ignition attempt.</returns>
        private Vector3 SnapToWorld(Vector3 rawWorld)
        {
            // Delegate to the skill whenever possible so snapping stays in sync with configuration toggles.
            return skill != null
                ? skill.SnapToIgnitionPoint(rawWorld)
                : new Vector3(Mathf.Round(rawWorld.x), Mathf.Round(rawWorld.y), 0f);
        }
    }
}
