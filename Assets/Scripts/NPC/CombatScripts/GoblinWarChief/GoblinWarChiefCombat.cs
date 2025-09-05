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
                PerformSlam(target);
            }
        }

        private void PerformSlam(CombatTarget target)
        {
            var targetBehaviour = target as MonoBehaviour;
            if (targetBehaviour != null)
            {
                Vector2 npcPos = transform.position;
                Vector2 targetPos = targetBehaviour.transform.position;
                Vector2 delta = targetPos - npcPos;
                if (Mathf.Abs(delta.x) <= slamRange && Mathf.Abs(delta.y) <= slamRange)
                {
                    target.ApplyDamage(slamDamage, DamageType.Melee, this);
                }
            }

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (slamDustPrefab != null)
                    {
                        var dust = Instantiate(slamDustPrefab,
                            (Vector2)transform.position + new Vector2(x, y), Quaternion.identity);
                        Destroy(dust, 3 * CombatMath.TICK_SECONDS);
                        StartCoroutine(FadeOutDust(dust));
                    }
                }
            }

            StartCoroutine(ScreenShake(shakeDuration, shakeMagnitude));
        }

        private IEnumerator FadeOutDust(GameObject dust)
        {
            var renderers = dust.GetComponentsInChildren<SpriteRenderer>();
            if (renderers.Length == 0)
            {
                yield return new WaitForSeconds(3 * CombatMath.TICK_SECONDS);
                Destroy(dust);
                yield break;
            }

            float duration = 3 * CombatMath.TICK_SECONDS;
            float elapsed = 0f;
            var originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                originalColors[i] = renderers[i].color;

            while (elapsed < duration)
            {
                float alpha = 1f - (elapsed / duration);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var c = originalColors[i];
                    renderers[i].color = new Color(c.r, c.g, c.b, alpha);
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                var c = originalColors[i];
                renderers[i].color = new Color(c.r, c.g, c.b, 0f);
            }

            Destroy(dust);
        }

        private IEnumerator ScreenShake(float duration, float magnitude)
        {
            var cam = Camera.main;
            if (cam == null)
                yield break;

            Vector3 originalPos = cam.transform.localPosition;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float offsetX = Random.Range(-1f, 1f) * magnitude;
                float offsetY = Random.Range(-1f, 1f) * magnitude;
                cam.transform.localPosition = new Vector3(originalPos.x + offsetX, originalPos.y + offsetY, originalPos.z);
                elapsed += Time.deltaTime;
                yield return null;
            }

            cam.transform.localPosition = originalPos;
        }
    }
}
