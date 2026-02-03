using System.Collections;
using UnityEngine;
using Util;
using Skills.Common;

namespace Skills.Mining
{
    /// <summary>
    /// Represents a rock in the world that can be mined.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class MineableRock : MonoBehaviour
    {
        [Header("Definition")]
        public RockDefinition rockDef;

        [Header("Visuals")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite fullSprite;
        [SerializeField] private Sprite depletedSprite;

        [Header("Collision")]
        [SerializeField] private Collider2D rockCollider;

        private int remainingOre;
        private bool depleted;
        private float respawnTimer;

        public RockDefinition RockDef => rockDef;
        public bool IsDepleted => depleted;

        private void Awake()
        {
            remainingOre = rockDef != null && rockDef.DepleteAfterNOres > 0 ? rockDef.DepleteAfterNOres : 0;
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            // Cache the collider reference so we can safely re-enable it when the rock depletes.
            if (rockCollider == null)
                rockCollider = GetComponent<Collider2D>();

            // Ensure the collider starts enabled even if the prefab was misconfigured.
            EnsureColliderBlocking();
        }

        private void Update()
        {
            if (!depleted)
                return;

            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f)
                Respawn();
        }

        public OreDefinition MineOre()
        {
            if (depleted || rockDef == null)
                return null;

            OreDefinition ore = rockDef.Ore;

            if (rockDef.DepleteAfterNOres > 0)
            {
                remainingOre--;
                if (remainingOre <= 0)
                    Deplete();
            }
            else if (Random.value < rockDef.DepletionRoll)
            {
                Deplete();
            }

            return ore;
        }

        private void Deplete()
        {
            depleted = true;
            respawnTimer = Random.Range(rockDef.RespawnTimeSecondsMin, rockDef.RespawnTimeSecondsMax);
            EnsureColliderBlocking();
            if (spriteRenderer && depletedSprite) spriteRenderer.sprite = depletedSprite;
        }

        private void Respawn()
        {
            depleted = false;
            remainingOre = rockDef.DepleteAfterNOres > 0 ? rockDef.DepleteAfterNOres : 0;
            EnsureColliderBlocking();
            if (spriteRenderer && fullSprite) spriteRenderer.sprite = fullSprite;
        }

        /// <summary>
        /// Guarantees the collider stays enabled so the depleted rock continues to block movement.
        /// </summary>
        private void EnsureColliderBlocking()
        {
            if (rockCollider == null)
                return;

            // We never want the collider disabled because players should not clip through depleted nodes.
            rockCollider.enabled = true;
            rockCollider.isTrigger = false;
        }

        public void Prospect(Transform requester)
        {
            StartCoroutine(ProspectRoutine(requester));
        }

        private IEnumerator ProspectRoutine(Transform requester)
        {
            if (requester == null)
                yield break;

            Vector3 resourcePosition = transform.position;
            GatheringFloatingTextService.TryShowNow("Prospecting...", requester, resourcePosition);
            yield return new WaitForSeconds(Ticker.TickDuration * 2f);
            string message = $"This rock contains {rockDef.Ore.DisplayName} here";
            GatheringFloatingTextService.TryShowNow(message, requester, resourcePosition);
        }
    }
}
