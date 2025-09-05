using System.Collections;
using UnityEngine;
using Combat;

namespace NPC
{
    /// <summary>
    /// Combat controller for the Goblin War Chief. Performs standard melee attacks
    /// and executes a periodic slam dealing area damage with visual effects.
    /// </summary>
    public class GoblinWarChiefCombat : BaseNpcCombat
    {
        [SerializeField] private float slamInterval = 10f;
        [SerializeField] private int slamDamage = 10;
        [SerializeField] private GameObject slamDustPrefab;
        [SerializeField] private float slamRange = 1.5f;
        [SerializeField] private float shakeDuration = 0.2f;
        [SerializeField] private float shakeMagnitude = 0.1f;

        public override void BeginAttacking(CombatTarget target)
        {
            base.BeginAttacking(target);
            if (target != null)
                StartCoroutine(SlamRoutine(target));
        }

        private IEnumerator SlamRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(slamInterval);
            while (target != null && target.IsAlive && combatant.IsAlive)
            {
                yield return wait;
                if (target == null || !target.IsAlive || !combatant.IsAlive)
                    break;
                GoblinWarChiefSlam.Perform(this, target, slamDamage, slamDustPrefab, slamRange, shakeDuration, shakeMagnitude);
            }
        }
    }
}
