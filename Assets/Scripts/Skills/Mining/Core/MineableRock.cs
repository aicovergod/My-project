using System.Collections;
using UnityEngine;
using Util;
using UI;

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

        /// <summary>
        /// Offset ensuring prospect feedback floats above the player's head for mining just like fishing.
        /// </summary>
        private static readonly Vector3 ProspectTextOffset = new Vector3(0f, 0.9f, 0f);

        /// <summary>
        /// Shared prospect text scale that keeps informational popups unobtrusive.
        /// </summary>
        private const float ProspectTextSize = 0.65f;

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

            Transform anchor = ResolveFloatingTextAnchor(requester);
            Vector3 anchorPosition = anchor != null ? anchor.position : requester.position;

            FloatingText.Show("Prospecting...", anchorPosition, null, ProspectTextSize, null, ProspectTextOffset);
            yield return new WaitForSeconds(Ticker.TickDuration * 2f);

            if (requester == null)
                yield break;

            anchor = ResolveFloatingTextAnchor(requester);
            anchorPosition = anchor != null ? anchor.position : requester.position;

            string message = rockDef != null && rockDef.Ore != null
                ? $"This rock contains {rockDef.Ore.DisplayName} here"
                : "There is nothing of interest in this rock";

            FloatingText.Show(message, anchorPosition, null, ProspectTextSize, null, ProspectTextOffset);
        }

        /// <summary>
        /// Finds the floating text anchor on the requester so prospect feedback appears relative to the character head.
        /// </summary>
        /// <param name="requester">Transform that initiated the prospect action.</param>
        /// <returns>The floating text anchor if present, otherwise the requester transform.</returns>
        private static Transform ResolveFloatingTextAnchor(Transform requester)
        {
            if (requester == null)
                return null;

            var anchor = requester.Find("FloatingTextAnchor");
            return anchor != null ? anchor : requester;
        }
    }
}
