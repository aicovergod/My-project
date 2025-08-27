using System;
using UnityEngine;
using Util;
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

        public bool IsDepleted { get; private set; }
        public bool IsBusy { get; set; }

        public event Action<FishableSpot, float> OnSpotDepleted;
        public event Action<FishableSpot> OnSpotRespawned;

        private double respawnAt;

        private void Awake()
        {
            if (sr == null)
                sr = GetComponent<SpriteRenderer>();
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
            IsBusy = false;
            OnSpotDepleted?.Invoke(this, def != null ? def.RespawnSeconds : 0f);
        }

        private void Respawn()
        {
            IsDepleted = false;
            var col = GetComponent<Collider2D>();
            if (col) col.enabled = true;
            if (sr && activeSprite) sr.sprite = activeSprite;
            OnSpotRespawned?.Invoke(this);
        }
    }
}
