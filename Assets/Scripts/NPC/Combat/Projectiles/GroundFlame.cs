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
                foreach (var tgt in targets)
                {
                    if (tgt != null)
                        tgt.ApplyDamage(damagePerTick, DamageType.Burn, SpellElement.Fire, this);
                }
                elapsed += CombatMath.TICK_SECONDS;
                yield return wait;
            }

            Destroy(gameObject);
        }
    }
}
