using Inventory;
using Player;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Permanent bonfire world object. Players can click it while having a log
    ///     selected to begin the automated fuel workflow handled by <see cref="FiremakingSkill"/>.
    ///     The component resolves the player references on awake and forwards pointer
    ///     clicks to the skill so the shared tick loop can process each 4-tick fuel
    ///     cycle.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [DisallowMultipleComponent]
    public sealed class FiremakingBonfireObject : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField]
        [Tooltip("Maximum distance the player can move away before fueling cancels.")]
        private float cancelDistance = 3f;

        private Inventory.Inventory inventory;
        private FiremakingSkill firemakingSkill;
        private PlayerMover playerMover;
        private Transform playerTransform;

        /// <summary>
        ///     Distance threshold used when checking whether the player has stepped
        ///     too far away from the bonfire during fueling.
        /// </summary>
        public float CancelDistance => cancelDistance;

        /// <summary>
        ///     Caches the player components and ensures the active camera can raycast
        ///     into 2D physics so pointer clicks reach this object.
        /// </summary>
        private void Awake()
        {
            EnsurePlayerReferences();

            var mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.GetComponent<Physics2DRaycaster>() == null)
                mainCamera.gameObject.AddComponent<Physics2DRaycaster>();
        }

        /// <summary>
        ///     Ensures the bonfire is still tracking the active player after
        ///     disable/enable cycles so runtime-spawned characters can interact
        ///     without requiring a scene reload.
        /// </summary>
        private void OnEnable()
        {
            EnsurePlayerReferences();
        }

        /// <summary>
        ///     Attempts to locate the player object via tag lookups and caches
        ///     the components the bonfire relies on for interaction. When the
        ///     search fails a floating text toast informs the player so clicks
        ///     do not silently do nothing.
        /// </summary>
        /// <param name="showFailureMessage">
        ///     When true, a floating text message is displayed if the player
        ///     cannot be located.
        /// </param>
        /// <returns>
        ///     True when the required references (inventory, firemaking skill
        ///     and player transform) were located; otherwise false.
        /// </returns>
        private bool EnsurePlayerReferences(bool showFailureMessage = false)
        {
            bool needsRefresh = inventory == null || firemakingSkill == null ||
                                playerMover == null || playerTransform == null;
            if (!needsRefresh)
                return true;

            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                inventory ??= playerObject.GetComponent<Inventory.Inventory>();
                firemakingSkill ??= playerObject.GetComponent<FiremakingSkill>();
                playerMover ??= playerObject.GetComponent<PlayerMover>();
                playerTransform = playerObject.transform;
            }

            bool hasEssentials = inventory != null && firemakingSkill != null && playerTransform != null;
            if (!hasEssentials && showFailureMessage)
                FloatingText.Show("You need your adventurer to use the bonfire.", transform.position);

            return hasEssentials;
        }

        /// <summary>
        ///     Responds to player clicks by asking the Firemaking skill to begin a
        ///     bonfire fueling session. Validation (selected logs, level checks, etc.)
        ///     happens inside the skill so the feedback stays centralised.
        /// </summary>
        /// <param name="eventData">Pointer payload provided by Unity's event system.</param>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            if (!EnsurePlayerReferences(true))
                return;

            int selectedIndex = inventory.selectedIndex;
            if (selectedIndex < 0)
                return;

            var entry = inventory.GetSlot(selectedIndex);
            if (entry.item == null)
                return;

            if (playerMover != null && playerMover.IsMoving)
            {
                // Prompt the player to stand still before attempting to feed the bonfire.
                FloatingText.Show("You need to stop moving first.", transform.position);
                return;
            }

            if (playerTransform != null && cancelDistance > 0f &&
                Vector3.Distance(playerTransform.position, transform.position) > cancelDistance)
            {
                FloatingText.Show("You are too far away from the bonfire.", transform.position);
                return;
            }

            if (!firemakingSkill.TryStartBonfireFeeding(this, selectedIndex, out var failureReason))
            {
                if (!string.IsNullOrEmpty(failureReason))
                    FloatingText.Show(failureReason, transform.position);
                return;
            }

            // Clear the inventory highlight so repeated clicks continue to use the same log stack.
            inventory.ClearSelection();
        }

        /// <summary>
        ///     When the bonfire is disabled while fueling is active we notify the
        ///     skill so the player receives the appropriate cancellation feedback.
        /// </summary>
        private void OnDisable()
        {
            if (firemakingSkill != null && firemakingSkill.IsFeedingBonfire && firemakingSkill.ActiveBonfire == this)
                firemakingSkill.StopBonfireFeeding(true, "The bonfire is no longer available.");
        }
    }
}
