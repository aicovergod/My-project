using UnityEngine;

namespace Skills.Cooking
{
    /// <summary>
    ///     Passive world component representing a cooking station.
    ///     The station only exposes distance settings so <see cref="CookingController"/>
    ///     can handle all player interaction logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public class CookingObject : MonoBehaviour
    {
        [Header("Interaction")]
        [SerializeField]
        [Tooltip("Maximum distance the player can stand from this station to start cooking.")]
        private float interactionRange = 1.5f;

        [SerializeField]
        [Tooltip("Distance at which active cooking automatically cancels.")]
        private float cancelDistance = 3f;

        [SerializeField]
        [Tooltip("Optional anchor transform used when auto-moving the player into position.")]
        private Transform approachAnchor;

        private CookingSkill cookingSkill;

        /// <summary>
        ///     Range used when checking whether the player can interact with this station.
        /// </summary>
        public float InteractionRange => interactionRange;

        /// <summary>
        ///     Maximum distance allowed before an active cooking action is cancelled.
        /// </summary>
        public float CancelDistance => cancelDistance;

        /// <summary>
        ///     Optional transform for auto-move targeting. Falls back to <see cref="Component.transform"/> when null.
        /// </summary>
        public Transform ApproachAnchor => approachAnchor != null ? approachAnchor : transform;

        private void Awake()
        {
            // Cache the player's cooking skill so we can cancel if the station disappears.
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                cookingSkill = player.GetComponent<CookingSkill>();
        }

        private void OnDisable()
        {
            // If this station goes away while in use ensure the active session stops cleanly.
            if (cookingSkill != null && cookingSkill.ActiveCookingObject == this)
                cookingSkill.StopCooking();
        }

        private void OnValidate()
        {
            interactionRange = Mathf.Max(0f, interactionRange);
            cancelDistance = Mathf.Max(0f, cancelDistance);
        }
    }
}
