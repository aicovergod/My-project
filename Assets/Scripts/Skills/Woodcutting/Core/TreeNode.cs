using System;
using UnityEngine;
using Util;
using System.Collections;
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

        [Header("Colliders")]
        [SerializeField] private Collider2D stumpCollider;
        private Collider2D col;
        private BoxCollider2D interactionCollider;

        [Header("Offsets")]
        [SerializeField] private float stumpYOffset;
        private Vector3 initialPosition;
        private Vector2 initialColliderOffset;
        private Vector2 initialStumpColliderOffset;

        public bool IsDepleted { get; private set; }
        public bool IsBusy { get; set; }

        public event Action<TreeNode, float> OnTreeDepleted;
        public event Action<TreeNode> OnTreeRespawned;

        private double respawnAt;
        private Coroutine tickerSubscriptionRoutine;
        private bool tickerSubscribed;

        private void Awake()
        {
            if (sr == null)
                sr = GetComponent<SpriteRenderer>();
            col = GetComponent<Collider2D>();
            initialPosition = transform.position;
            if (col)
                initialColliderOffset = col.offset;
            if (stumpCollider)
            {
                initialStumpColliderOffset = stumpCollider.offset;
                stumpCollider.enabled = false;
            }
            if (def != null)
            {
                if (aliveSprite == null) aliveSprite = def.AliveSprite;
                if (depletedSprite == null) depletedSprite = def.DepletedSprite;
                if (sr != null && aliveSprite != null) sr.sprite = aliveSprite;
            }

            var boxColliders = GetComponents<BoxCollider2D>();
            foreach (var bc in boxColliders)
            {
                if (bc != col)
                {
                    interactionCollider = bc;
                    break;
                }
            }
            if (interactionCollider == null)
                interactionCollider = gameObject.AddComponent<BoxCollider2D>();
            interactionCollider.isTrigger = true;
            if (sr != null && sr.sprite != null)
            {
                var bounds = sr.sprite.bounds;
                interactionCollider.size = bounds.size;
                interactionCollider.offset = bounds.center;
            }
        }

        private void OnEnable()
        {
            EnsureTickerSubscription();
        }

        private void Start()
        {
            EnsureTickerSubscription();
        }

        private void OnDisable()
        {
            ReleaseTickerSubscription();
        }

        /// <summary>
        /// Ensures the tree is registered with the shared <see cref="Ticker"/> so respawn timing continues
        /// to progress after returning from login scenes or other loads where the singleton is spawned late.
        /// </summary>
        private void EnsureTickerSubscription()
        {
            if (tickerSubscribed)
                return;

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Subscribe(this);
                tickerSubscribed = true;
            }
            else if (tickerSubscriptionRoutine == null && isActiveAndEnabled)
            {
                tickerSubscriptionRoutine = StartCoroutine(WaitForTickerAndSubscribe());
            }
        }

        /// <summary>
        /// Cancels pending ticker waits and unsubscribes when the object is disabled so no orphaned
        /// subscriptions remain after scene transitions.
        /// </summary>
        private void ReleaseTickerSubscription()
        {
            if (tickerSubscriptionRoutine != null)
            {
                StopCoroutine(tickerSubscriptionRoutine);
                tickerSubscriptionRoutine = null;
            }

            if (tickerSubscribed && Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }

            tickerSubscribed = false;
        }

        /// <summary>
        /// Waits for the global ticker singleton to appear before subscribing so respawn timers pick back up
        /// correctly even when the ticker spawns a frame later (common after scene changes).
        /// </summary>
        private IEnumerator WaitForTickerAndSubscribe()
        {
            while (Ticker.Instance == null)
                yield return null;

            tickerSubscriptionRoutine = null;

            if (!isActiveAndEnabled)
                yield break;

            Ticker.Instance.Subscribe(this);
            tickerSubscribed = true;
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
            if (stumpCollider)
            {
                if (col) col.enabled = false;
                stumpCollider.enabled = true;
            }
            if (sr && depletedSprite) sr.sprite = depletedSprite;
            if (stumpYOffset != 0f)
            {
                transform.position = initialPosition + Vector3.up * stumpYOffset;
                if (col) col.offset = initialColliderOffset - Vector2.up * stumpYOffset;
                if (stumpCollider) stumpCollider.offset = initialStumpColliderOffset - Vector2.up * stumpYOffset;
            }
            IsBusy = false;
            OnTreeDepleted?.Invoke(this, def != null ? def.RespawnSeconds : 0f);
        }

        private void Respawn()
        {
            IsDepleted = false;
            if (stumpYOffset != 0f)
            {
                transform.position = initialPosition;
                if (col) col.offset = initialColliderOffset;
                if (stumpCollider) stumpCollider.offset = initialStumpColliderOffset;
            }
            if (stumpCollider)
            {
                stumpCollider.enabled = false;
                if (col) col.enabled = true;
            }
            else if (col)
            {
                col.enabled = true;
            }
            if (sr && aliveSprite) sr.sprite = aliveSprite;
            OnTreeRespawned?.Invoke(this);
        }
    }
}
