using UnityEngine;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Permanent bonfire world object used by <see cref="FiremakingSkill"/>. The component now
    ///     simply exposes the cancel distance and notifies the skill if the bonfire becomes
    ///     unavailable so active fueling sessions end immediately.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [DisallowMultipleComponent]
    public sealed class FiremakingBonfireObject : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Maximum distance the player can move away before fueling cancels.")]
        private float cancelDistance = 3f;

        private FiremakingSkill cachedSkill;

        /// <summary>
        ///     Distance threshold used when checking whether the player has stepped too far away from
        ///     the bonfire during fueling.
        /// </summary>
        public float CancelDistance => cancelDistance;

        /// <summary>
        ///     Cache the Firemaking skill reference so the component can request cancellation without
        ///     performing repeated scene lookups.
        /// </summary>
        private void Awake()
        {
            cachedSkill = FindObjectOfType<FiremakingSkill>();
        }

        /// <summary>
        ///     When the bonfire is disabled while fueling is active we notify the skill so the player
        ///     receives the appropriate cancellation feedback.
        /// </summary>
        private void OnDisable()
        {
            if (cachedSkill == null)
                cachedSkill = FindObjectOfType<FiremakingSkill>();

            if (cachedSkill != null && cachedSkill.IsFeedingBonfire && cachedSkill.ActiveBonfire == this)
                cachedSkill.StopBonfireFeeding(true, "The bonfire is no longer available.");
        }
    }
}
