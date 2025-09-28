using System.Collections;
using UnityEngine;
using Combat;

namespace NPC
{
    /// <summary>
    /// Combat controller for the Goblin Warmage. Performs standard attacks
    /// and periodically bombards the target area with a meteor shower barrage.
    /// </summary>
    public class GoblinWarmageCombat : BaseNpcCombat
    {
        [Header("Meteor Shower Settings")]
        [SerializeField] private float meteorInterval = 12f;
        [SerializeField] private int meteorCount = 5;
        [SerializeField] private float spreadRadius = 3f;
        [SerializeField] private int impactDamage = 6;
        [SerializeField] private int burnDamagePerTick = 1;
        [SerializeField] private float burnDuration = 5f;
        [SerializeField] private GameObject meteorPrefab;
        [SerializeField] private GameObject burnPrefab;
        [SerializeField] private float dropHeight = 8f;
        [SerializeField] private float meteorSpeed = 8f;

        private Coroutine meteorRoutineHandle;

        public override void BeginAttacking(CombatTarget target)
        {
            base.BeginAttacking(target);
            if (target != null)
            {
                StopMeteorRoutine();
                meteorRoutineHandle = StartCoroutine(MeteorRoutine(target));
            }
        }

        public override void ResetCombatState(bool resetSpawnPosition = false)
        {
            base.ResetCombatState(resetSpawnPosition);
            StopMeteorRoutine();
        }

        private void OnDisable()
        {
            StopMeteorRoutine();
        }

        /// <summary>
        /// Stop the meteor barrage coroutine if it is currently active.
        /// </summary>
        private void StopMeteorRoutine()
        {
            if (meteorRoutineHandle != null)
            {
                StopCoroutine(meteorRoutineHandle);
                meteorRoutineHandle = null;
            }
        }

        private IEnumerator MeteorRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(meteorInterval);
            while (ShouldContinueMeteorRoutine(target))
            {
                yield return wait;
                if (!ShouldContinueMeteorRoutine(target))
                    break;
                MeteorShowerBarrage.Perform(this, target, meteorCount, spreadRadius,
                    impactDamage, burnDamagePerTick, burnDuration,
                    meteorPrefab, burnPrefab, dropHeight, meteorSpeed);
            }
            meteorRoutineHandle = null;
        }

        /// <summary>
        /// Determines whether the warmage should keep scheduling meteor waves against the
        /// current target.
        /// </summary>
        private bool ShouldContinueMeteorRoutine(CombatTarget target)
        {
            if (target == null || !combatant.IsAlive)
                return false;
            if (!target.IsAlive)
                return false;
            return activeAttacks.ContainsKey(target);
        }
    }
}
