using Combat;
using UnityEngine;

namespace NPC
{
    /// <summary>
    /// Handles standard NPC melee attacks using BaseNpcCombat functionality.
    /// Falls back to the shared <see cref="PathfindingService"/> whenever line-of-sight is obstructed so the NPC can re-path around obstacles.
    /// </summary>
    [RequireComponent(typeof(NpcPathMover))]
    public class NpcAttackController : BaseNpcCombat
    {
        [Header("Pathfinding")]
        [Tooltip("Minimum time between successive path requests when chasing a target without line-of-sight.")]
        [SerializeField] private float replanCooldownSeconds = 0.6f;

        [Tooltip("Optional logging that highlights when the controller triggers a replan.")]
        [SerializeField] private bool enablePathDebugLogging;

        private NpcPathMover pathMover;
        private float nextAllowedPathRequestTime;

        /// <inheritdoc />
        protected override void Awake()
        {
            base.Awake();
            pathMover = GetComponent<NpcPathMover>();
        }

        private void OnEnable()
        {
            if (pathMover != null)
            {
                pathMover.DestinationReached += HandleDestinationReached;
            }
        }

        private void OnDisable()
        {
            if (pathMover != null)
            {
                pathMover.DestinationReached -= HandleDestinationReached;
                pathMover.CancelPath();
            }
        }

        /// <inheritdoc />
        protected override bool HasLineOfSight(CombatTarget target, Transform targetTransform)
        {
            bool hasLine = base.HasLineOfSight(target, targetTransform);
            if (hasLine)
            {
                if (pathMover != null && pathMover.IsFollowingPath)
                {
                    pathMover.CancelPath();
                }
                return true;
            }

            if (target == null || targetTransform == null)
            {
                return false;
            }

            TryRequestPath(targetTransform);
            return false;
        }

        /// <summary>
        /// Requests a fresh path to the supplied target transform when line-of-sight is obstructed.
        /// </summary>
        private void TryRequestPath(Transform targetTransform)
        {
            if (pathMover == null)
            {
                return;
            }

            if (Time.time < nextAllowedPathRequestTime)
            {
                return;
            }

            float preferredRange = combatant != null && combatant.Profile != null
                ? combatant.Profile.GetPreferredAttackRange()
                : CombatMath.MELEE_RANGE;

            pathMover.RequestPathTo(targetTransform.position, Mathf.Max(preferredRange, CombatMath.MELEE_RANGE));
            nextAllowedPathRequestTime = Time.time + replanCooldownSeconds;

            if (enablePathDebugLogging)
            {
                Debug.Log($"{name} requesting navigation path towards {targetTransform.name}.", this);
            }
        }

        /// <summary>
        /// Resets the replan timer once the mover reports it has reached its destination.
        /// </summary>
        private void HandleDestinationReached(NpcPathMover mover)
        {
            nextAllowedPathRequestTime = Time.time + replanCooldownSeconds;
        }
    }
}
