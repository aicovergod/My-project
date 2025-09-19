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
        private bool _tickerSubscribed;
        private Coroutine _tickerSubscriptionRoutine;

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
            EnsureTickerSubscription();
        }

        private void OnDisable()
        {
            ReleaseTickerSubscription();
        }

        /// <summary>
        /// Makes sure the spot is registered with the <see cref="Ticker"/>. When loading into a scene
        /// the ticker singleton might not exist yet, so this method waits for it before subscribing to
        /// avoid missed ticks or duplicate registrations.
        /// </summary>
        private void EnsureTickerSubscription()
        {
            if (_tickerSubscribed)
                return;

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Subscribe(this);
                _tickerSubscribed = true;
            }
            else if (_tickerSubscriptionRoutine == null && isActiveAndEnabled)
            {
                _tickerSubscriptionRoutine = StartCoroutine(WaitForTickerAndSubscribe());
            }
        }

        /// <summary>
        /// Removes the ticker subscription and cancels any pending waiters so the component stops
        /// receiving ticks while disabled and does not leak routines across scene transitions.
        /// </summary>
        private void ReleaseTickerSubscription()
        {
            if (_tickerSubscriptionRoutine != null)
            {
                StopCoroutine(_tickerSubscriptionRoutine);
                _tickerSubscriptionRoutine = null;
            }

            if (_tickerSubscribed && Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }

            _tickerSubscribed = false;
        }

        /// <summary>
        /// Coroutine that blocks until the ticker singleton is available before performing the
        /// subscription. This mirrors the wanderer handling so fishing spots work after scene loads.
        /// </summary>
        private IEnumerator WaitForTickerAndSubscribe()
        {
            while (Ticker.Instance == null)
                yield return null;

            _tickerSubscriptionRoutine = null;

            if (!isActiveAndEnabled)
                yield break;

            Ticker.Instance.Subscribe(this);
            _tickerSubscribed = true;
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

            FloatingText.Show("Checking...", requester.position);
            yield return new WaitForSeconds(Ticker.TickDuration * 2f);

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
            if (fishNames.Count == 1)
                message = $"This spot contains {fishNames[0]} here";
            else
                message = $"This spot contains {string.Join(" & ", fishNames)} here";

            FloatingText.Show(message, requester.position);
        }
    }
}
