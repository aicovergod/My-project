using System.Collections;
using UnityEngine;
using Combat;

namespace NPC
{
    /// <summary>
    /// Combat controller for the Goblin War Mage 2. Performs standard attacks
    /// and periodically ambushes the target with spectral clones.
    /// </summary>
    public class GoblinWarmage2Combat : BaseNpcCombat
    {
        [Header("Clone Ambush Settings")]
        [SerializeField] private float ambushInterval = 15f;
        [SerializeField] private int cloneCount = 3;
        [SerializeField] private float cloneLifespan = 6f;
        [SerializeField] private int realCloneDamage = 4;
        [SerializeField] private GameObject[] clonePrefabs;

        public override void BeginAttacking(CombatTarget target)
        {
            base.BeginAttacking(target);
            if (target != null)
                StartCoroutine(CloneAmbushRoutine(target));
        }

        private IEnumerator CloneAmbushRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(ambushInterval);
            while (target != null && target.IsAlive && combatant.IsAlive)
            {
                yield return wait;
                if (target == null || !target.IsAlive || !combatant.IsAlive)
                    break;
                SpectralCloneAmbush.Perform(this, target, clonePrefabs, cloneCount,
                    cloneLifespan, realCloneDamage);
            }
        }
    }
}

