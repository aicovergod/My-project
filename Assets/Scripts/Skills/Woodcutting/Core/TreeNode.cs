using System;
using UnityEngine;
using Util;
using Random = UnityEngine.Random;

namespace Skills.Woodcutting
{
    [RequireComponent(typeof(Collider2D))]
    public class TreeNode : MonoBehaviour, ITickable
    {
        [Header("Definition")]
        public TreeDefinition def;

        [Header("Visuals")]
        [SerializeField] private SpriteRenderer sr;
        [SerializeField] private Sprite aliveSprite;
        [SerializeField] private Sprite depletedSprite;

        public bool IsDepleted { get; private set; }
        public bool IsBusy { get; set; }

        public event Action<TreeNode, float> OnTreeDepleted;
        public event Action<TreeNode> OnTreeRespawned;

        private double respawnAt;

        private void Awake()
        {
            if (sr == null)
                sr = GetComponent<SpriteRenderer>();
            if (def != null)
            {
                if (aliveSprite == null) aliveSprite = def.AliveSprite;
                if (depletedSprite == null) depletedSprite = def.DepletedSprite;
                if (sr != null && aliveSprite != null) sr.sprite = aliveSprite;
            }
        }

        private void OnEnable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Subscribe(this);
        }

        private void Start()
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
            {
                Respawn();
            }
        }

        public void OnLogChopped()
        {
            if (IsDepleted || def == null)
                return;

            if (def.DepletesAfterOneLog)
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
            OnTreeDepleted?.Invoke(this, def != null ? def.RespawnSeconds : 0f);
        }

        private void Respawn()
        {
            IsDepleted = false;
            var col = GetComponent<Collider2D>();
            if (col) col.enabled = true;
            if (sr && aliveSprite) sr.sprite = aliveSprite;
            OnTreeRespawned?.Invoke(this);
        }
    }
}
