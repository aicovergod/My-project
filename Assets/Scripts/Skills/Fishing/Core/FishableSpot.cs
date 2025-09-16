using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Util;
using UI;
using Random = UnityEngine.Random;

namespace Skills.Fishing
{
    [RequireComponent(typeof(Collider2D))]
    public class FishableSpot : MonoBehaviour, ITickable
    {
        [Header("Definition")]
        public FishingSpotDefinition def;

        [Header("Visuals")]
        [SerializeField] private SpriteRenderer sr;
        [SerializeField] private Sprite activeSprite;
        [SerializeField] private Sprite depletedSprite;
        [SerializeField] private FishingSpotAlphaOscillator oscillator;

        public bool IsDepleted { get; private set; }
        public bool IsBusy { get; set; }

        public event Action<FishableSpot, float> OnSpotDepleted;
        public event Action<FishableSpot> OnSpotRespawned;

        private double respawnAt;

        /// <summary>
        /// Consistent offset used so prospect feedback renders comfortably above the player's head.
        /// </summary>
        private static readonly Vector3 ProspectTextOffset = new Vector3(0f, 0.9f, 0f);

        /// <summary>
        /// Slightly reduced size so prospect text matches the scale of other OSRS-style popups.
        /// </summary>
        private const float ProspectTextSize = 0.65f;

        private void Awake()
        {
            if (sr == null)
                sr = GetComponent<SpriteRenderer>();
            if (oscillator == null)
                oscillator = GetComponent<FishingSpotAlphaOscillator>();
            if (def != null)
            {
                if (activeSprite == null && sr != null) activeSprite = sr.sprite;
            }
        }

        private void OnEnable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void OnDisable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
        }

        public void OnTick()
        {
            if (IsDepleted && Time.timeAsDouble >= respawnAt)
                Respawn();
        }

        public void OnFishCaught()
        {
            if (IsDepleted || def == null)
                return;

            if (def.DepletesAfterCatch)
            {
                Deplete();
            }
            else if (def.DepleteRollInverse > 0 && Random.Range(0, def.DepleteRollInverse) == 0)
            {
                Deplete();
            }
        }

        private void Deplete()
        {
            IsDepleted = true;
            respawnAt = Time.timeAsDouble + def.RespawnSeconds;
            var col = GetComponent<Collider2D>();
            if (col) col.enabled = false;
            if (sr && depletedSprite) sr.sprite = depletedSprite;
            if (oscillator)
            {
                oscillator.enabled = false;
                if (sr)
                {
                    var color = sr.color;
                    color.a = 1f;
                    sr.color = color;
                }
            }
            IsBusy = false;
            OnSpotDepleted?.Invoke(this, def != null ? def.RespawnSeconds : 0f);
        }

        private void Respawn()
        {
            IsDepleted = false;
            var col = GetComponent<Collider2D>();
            if (col) col.enabled = true;
            if (sr && activeSprite) sr.sprite = activeSprite;
            if (oscillator) oscillator.enabled = true;
            OnSpotRespawned?.Invoke(this);
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

            FloatingText.Show("Checking...", anchorPosition, null, ProspectTextSize, null, ProspectTextOffset);
            yield return new WaitForSeconds(Ticker.TickDuration * 2f);

            if (requester == null)
                yield break;

            anchor = ResolveFloatingTextAnchor(requester);
            anchorPosition = anchor != null ? anchor.position : requester.position;

            var fishNames = new List<string>();
            if (def != null && def.AvailableFish != null)
            {
                foreach (var fish in def.AvailableFish)
                {
                    if (fish != null)
                    {
                        var cleanName = fish.DisplayName;
                        if (cleanName.StartsWith("Raw "))
                            cleanName = cleanName.Substring(4);
                        fishNames.Add(cleanName);
                    }
                }
            }

            string message;
            if (fishNames.Count == 0)
                message = "There are no fish in this spot";
            else if (fishNames.Count == 1)
                message = $"This spot contains {fishNames[0]} here";
            else
                message = $"This spot contains {string.Join(" & ", fishNames)} here";

            FloatingText.Show(message, anchorPosition, null, ProspectTextSize, null, ProspectTextOffset);
        }

        /// <summary>
        /// Attempts to locate a dedicated floating text anchor on the requester so popups appear at head height.
        /// </summary>
        /// <param name="requester">Transform responsible for triggering the prospect action.</param>
        /// <returns>The floating text anchor if one exists, otherwise the requester transform.</returns>
        private static Transform ResolveFloatingTextAnchor(Transform requester)
        {
            if (requester == null)
                return null;

            var anchor = requester.Find("FloatingTextAnchor");
            return anchor != null ? anchor : requester;
        }
    }
}
