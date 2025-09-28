using System.Collections;
using UnityEngine;
using Combat;
using Player;

namespace NPC
{
    public static class GoblinWarChiefSlam
    {
        public static void Perform(BaseNpcCombat owner, CombatTarget target, int slamDamage, GameObject slamDustPrefab, float slamRange, float shakeDuration, float shakeMagnitude)
        {
            if (owner == null)
                return;

            var myFaction = owner.GetComponent<IFactionProvider>();
            var ownerTarget = owner.GetComponent<CombatTarget>();
            var hits = Physics2D.OverlapCircleAll(owner.transform.position, slamRange);
            foreach (var hit in hits)
            {
                var otherTarget = hit.GetComponent<CombatTarget>();
                if (otherTarget == null || otherTarget == ownerTarget || !otherTarget.IsAlive)
                    continue;
                if (myFaction != null)
                {
                    var otherFaction = hit.GetComponent<IFactionProvider>();
                    if (otherFaction != null && !myFaction.IsEnemy(otherFaction.Faction))
                        continue;
                }
                otherTarget.ApplyDamage(slamDamage, DamageType.Melee, SpellElement.None, owner);
            }

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (slamDustPrefab != null)
                    {
                        var dust = Object.Instantiate(slamDustPrefab,
                            (Vector2)owner.transform.position + new Vector2(x, y) * slamRange, Quaternion.identity);
                        Object.Destroy(dust, 3 * CombatMath.TICK_SECONDS);
                        owner.StartCoroutine(FadeOutDust(dust));
                    }
                }
            }

            var playerMover = Object.FindObjectOfType<PlayerMover>();
            if (playerMover != null)
            {
                float shakeRadius = slamRange + 2f;
                if (Vector2.Distance(owner.transform.position, playerMover.transform.position) <= shakeRadius)
                    owner.StartCoroutine(ScreenShake(shakeDuration, shakeMagnitude));
            }
        }

        private static IEnumerator FadeOutDust(GameObject dust)
        {
            var renderers = dust.GetComponentsInChildren<SpriteRenderer>();
            if (renderers.Length == 0)
            {
                yield return new WaitForSeconds(3 * CombatMath.TICK_SECONDS);
                Object.Destroy(dust);
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

            Object.Destroy(dust);
        }

        private static IEnumerator ScreenShake(float duration, float magnitude)
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
