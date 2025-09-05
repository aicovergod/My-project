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

        public override void BeginAttacking(CombatTarget target)
        {
            base.BeginAttacking(target);
            if (target != null)
                StartCoroutine(MeteorRoutine(target));
        }

        private IEnumerator MeteorRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(meteorInterval);
            while (target != null && target.IsAlive && combatant.IsAlive)
            {
                yield return wait;
                if (target == null || !target.IsAlive || !combatant.IsAlive)
                    break;
                MeteorShowerBarrage.Perform(this, target, meteorCount, spreadRadius,
                    impactDamage, burnDamagePerTick, burnDuration,
                    meteorPrefab, burnPrefab, dropHeight, meteorSpeed);
            }
        }
    }
}
