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

        private bool slamReady;
        private Coroutine slamCooldownRoutine;

        public override void BeginAttacking(CombatTarget target)
        {
            base.BeginAttacking(target);
            if (target != null)
            {
                if (slamCooldownRoutine == null)
                    slamCooldownRoutine = combatant.StartCoroutine(SlamCooldown());

                StartCoroutine(SlamWatcher(target));
                if (slamReady)
                {
                    // slam fires immediately on re-engagement
                }
            }
        }

        private IEnumerator SlamCooldown()
        {
            yield return new WaitForSeconds(slamInterval);
            slamReady = true;
            slamCooldownRoutine = null;
        }

        private IEnumerator SlamWatcher(CombatTarget target)
        {
            while (target != null && target.IsAlive && combatant.IsAlive)
            {
                if (slamReady)
                {
                    GoblinWarChiefSlam.Perform(this, target, slamDamage, slamDustPrefab, slamRange, shakeDuration, shakeMagnitude);
                    slamReady = false;
                    slamCooldownRoutine = combatant.StartCoroutine(SlamCooldown());
                }
                yield return null;
            }
        }
    }
}
