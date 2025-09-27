using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Combat;

namespace NPC
{
    /// <summary>
    /// Handles periodic damage to targets standing on burning ground.
    /// </summary>
    public class GroundFlame : MonoBehaviour
    {
        public int damagePerTick;
        public float duration;

        private readonly HashSet<CombatTarget> targets = new HashSet<CombatTarget>();
        private readonly List<CombatTarget> targetSnapshot = new List<CombatTarget>(8);

        private void Start()
        {
            StartCoroutine(BurnRoutine());
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var tgt = other.GetComponent<CombatTarget>();
            if (tgt != null)
                targets.Add(tgt);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var tgt = other.GetComponent<CombatTarget>();
            if (tgt != null)
                targets.Remove(tgt);
        }

        private IEnumerator BurnRoutine()
        {
            float elapsed = 0f;
            var wait = new WaitForSeconds(CombatMath.TICK_SECONDS);
            while (elapsed < duration)
            {
                // Copy the current targets into a snapshot so we can safely iterate even if the HashSet changes mid-loop.
                targetSnapshot.Clear();
                targetSnapshot.AddRange(targets);

                var requiresCleanup = false;
                foreach (var tgt in targetSnapshot)
                {
                    if (tgt == null || !tgt.IsAlive)
                    {
                        requiresCleanup = true;
                        continue;
                    }

                    tgt.ApplyDamage(damagePerTick, DamageType.Burn, SpellElement.Fire, this);
                }

                if (requiresCleanup)
                {
                    // Remove any null or dead entries after processing so the main set stays accurate for the next tick.
                    targets.RemoveWhere(target => target == null || !target.IsAlive);
                }
                elapsed += CombatMath.TICK_SECONDS;
                yield return wait;
            }

            Destroy(gameObject);
        }
    }
}
