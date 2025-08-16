using System.Collections;
using UnityEngine;
using Util;

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
            var col = GetComponent<Collider2D>();
            if (col) col.enabled = false;
            if (spriteRenderer && depletedSprite) spriteRenderer.sprite = depletedSprite;
        }

        private void Respawn()
        {
            depleted = false;
            remainingOre = rockDef.DepleteAfterNOres > 0 ? rockDef.DepleteAfterNOres : 0;
            var col = GetComponent<Collider2D>();
            if (col) col.enabled = true;
            if (spriteRenderer && fullSprite) spriteRenderer.sprite = fullSprite;
        }

        public void Prospect(Transform requester)
        {
            StartCoroutine(ProspectRoutine(requester));
        }

        private IEnumerator ProspectRoutine(Transform requester)
        {
            if (requester == null)
                yield break;

            FloatingText.Show("Prospecting...", requester.position);
            yield return new WaitForSeconds(Ticker.TickDuration * 2f);
            FloatingText.Show($"This rock contains {rockDef.Ore.DisplayName} ore.", requester.position);
        }
    }
}
