using Companions;
using Player;
using UI.Chat;
using UnityEngine;
using Util;

namespace Environment.WorldObjects
{
    /// <summary>
    /// Applies periodic chip damage to the player whenever they stand on the nettle tile.
    /// Operates without a collider by performing manual overlap checks each OSRS tick.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StingingNettleHazard : MonoBehaviour, ITickable
    {
        private const string DebugLogPrefix = "[StingingNettleHazard]";

        [Header("Damage"), Tooltip("Number of ticks the player must remain on the tile before taking damage again."), Min(1)]
        [SerializeField] private int ticksBetweenDamage = 4;

        [Tooltip("Damage applied every time the tick counter completes."), Min(1)]
        [SerializeField] private int damagePerPulse = 1;

        [Header("Area"), Tooltip("Size of the tile in world units that should be considered harmful.")]
        [SerializeField] private Vector2 damageAreaSize = Vector2.one;

        [Tooltip("Offset applied to the damage area relative to the GameObject position (in world units).")]
        [SerializeField] private Vector2 damageAreaOffset = Vector2.zero;

        [Header("Debug"), Tooltip("When enabled, hazard behaviour details are logged to the console.")]
        [SerializeField] private bool enableDebugLogging;

        [Tooltip("Toggle gizmo rendering for the damage tile.")]
        [SerializeField] private bool drawGizmos = true;

        private Transform playerTransform;
        private PlayerHitpoints playerHitpoints;
        private Collider2D playerCollider;

        private int contactTickCounter;
        private bool playerInsideArea;
        private bool tickerSubscribed;
        private bool nettleDialogueTriggered;
        private int nettleDialogueCooldownTicksRemaining;

        [Header("Dialogue"), Tooltip("Cooldown in seconds before the companion can comment on the nettles again."), Min(1f)]
        [SerializeField] private float nettleDialogueCooldownSeconds = 30f;

        private void OnValidate()
        {
            damageAreaSize = new Vector2(Mathf.Max(0.01f, damageAreaSize.x), Mathf.Max(0.01f, damageAreaSize.y));
        }

        private void Awake()
        {
            OnValidate();
        }

        private void OnEnable()
        {
            TryResolvePlayer();
            TrySubscribeToTicker();
        }

        private void OnDisable()
        {
            UnsubscribeFromTicker();
            ResetContactState();
            nettleDialogueTriggered = false;
            nettleDialogueCooldownTicksRemaining = 0;
        }

        private void Update()
        {
            if (!tickerSubscribed)
            {
                TrySubscribeToTicker();
            }
        }

        /// <inheritdoc />
        public void OnTick()
        {
            UpdateNettleDialogueCooldown();

            if (!EnsurePlayerReference())
            {
                ResetContactState();
                return;
            }

            bool isPlayerInside = IsPlayerWithinDamageArea();
            if (isPlayerInside)
            {
                HandlePlayerInsideArea();
            }
            else if (playerInsideArea)
            {
                ResetContactState();
            }
        }

        private void HandlePlayerInsideArea()
        {
            if (!playerInsideArea)
            {
                playerInsideArea = true;
                contactTickCounter = 0;
                TryEmitCompanionDialogue();
                if (enableDebugLogging)
                {
                    Debug.Log($"{DebugLogPrefix} Player entered hazard tile.", this);
                }
            }

            contactTickCounter++;
            if (contactTickCounter < ticksBetweenDamage)
            {
                return;
            }

            contactTickCounter = 0;
            ApplyDamage();
        }

        private void ApplyDamage()
        {
            if (playerHitpoints == null)
            {
                return;
            }

            playerHitpoints.ApplyDamage(damagePerPulse);
            if (enableDebugLogging)
            {
                Debug.Log($"{DebugLogPrefix} Applied {damagePerPulse} damage to the player.", this);
            }
        }

        private bool EnsurePlayerReference()
        {
            if (playerTransform != null && playerHitpoints != null)
            {
                return true;
            }

            TryResolvePlayer();
            return playerTransform != null && playerHitpoints != null;
        }

        private void TryResolvePlayer()
        {
            if (!PlayerLocator.TryFindPlayer(out var player))
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"{DebugLogPrefix} Unable to locate player instance.", this);
                }
                playerTransform = null;
                playerHitpoints = null;
                playerCollider = null;
                return;
            }

            playerTransform = player.transform;
            playerHitpoints = player.GetComponent<PlayerHitpoints>();
            playerCollider = player.GetComponent<Collider2D>();

            if (playerHitpoints == null && enableDebugLogging)
            {
                Debug.LogWarning($"{DebugLogPrefix} Located player lacks a PlayerHitpoints component.", player);
            }
        }

        private bool IsPlayerWithinDamageArea()
        {
            if (playerTransform == null)
            {
                return false;
            }

            Vector3 center = GetAreaCenter();
            Vector2 halfExtents = damageAreaSize * 0.5f;

            if (playerCollider != null)
            {
                var areaBounds = new Bounds(center, new Vector3(damageAreaSize.x, damageAreaSize.y, Mathf.Max(damageAreaSize.x, damageAreaSize.y)));
                return areaBounds.Intersects(playerCollider.bounds);
            }

            Vector3 position = playerTransform.position;
            return Mathf.Abs(position.x - center.x) <= halfExtents.x &&
                   Mathf.Abs(position.y - center.y) <= halfExtents.y;
        }

        private Vector3 GetAreaCenter()
        {
            Vector3 offset = new Vector3(damageAreaOffset.x, damageAreaOffset.y, 0f);
            return transform.TransformPoint(offset);
        }

        private void ResetContactState()
        {
            playerInsideArea = false;
            contactTickCounter = 0;
        }

        private void TryEmitCompanionDialogue()
        {
            if (nettleDialogueTriggered || nettleDialogueCooldownTicksRemaining > 0)
            {
                return;
            }

            if (!CompanionManager.HasActiveCompanion)
            {
                return;
            }

            var chat = ChatService.Instance;
            if (chat == null)
            {
                return;
            }

            string line = CompanionChatLibrary.GetRandomStingingNettlePainLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), line);
            nettleDialogueTriggered = true;
            nettleDialogueCooldownTicksRemaining = Mathf.CeilToInt(nettleDialogueCooldownSeconds / Ticker.TickDuration);
        }

        /// <summary>
        /// Counts down the nettle dialogue cooldown and re-enables dialogue once the timer completes.
        /// </summary>
        private void UpdateNettleDialogueCooldown()
        {
            if (nettleDialogueCooldownTicksRemaining <= 0)
            {
                return;
            }

            nettleDialogueCooldownTicksRemaining--;
            if (nettleDialogueCooldownTicksRemaining <= 0)
            {
                nettleDialogueTriggered = false;
            }
        }

        private void TrySubscribeToTicker()
        {
            if (tickerSubscribed)
            {
                return;
            }

            if (Ticker.Instance == null)
            {
                return;
            }

            Ticker.Instance.Subscribe(this);
            tickerSubscribed = true;
        }

        private void UnsubscribeFromTicker()
        {
            if (!tickerSubscribed)
            {
                return;
            }

            if (Ticker.Instance != null)
            {
                Ticker.Instance.Unsubscribe(this);
            }

            tickerSubscribed = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawGizmos)
            {
                return;
            }

            Matrix4x4 originalMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            float clampedWidth = Mathf.Max(0.01f, damageAreaSize.x);
            float clampedHeight = Mathf.Max(0.01f, damageAreaSize.y);
            Vector3 size = new Vector3(clampedWidth, clampedHeight, 0f);
            Vector3 offset = new Vector3(damageAreaOffset.x, damageAreaOffset.y, 0f);

            Color fillColor = new Color(0f, 0.2f, 0f, 0.35f);
            Color borderColor = new Color(0f, 0.35f, 0f, 1f);
            Color innerLineColor = Color.yellow;

            Gizmos.color = fillColor;
            Gizmos.DrawCube(offset, size);

            Gizmos.color = borderColor;
            Gizmos.DrawWireCube(offset, size);

            Gizmos.color = innerLineColor;
            Vector3 halfSize = size * 0.5f;
            Gizmos.DrawLine(offset + new Vector3(-halfSize.x, 0f, 0f), offset + new Vector3(halfSize.x, 0f, 0f));
            Gizmos.DrawLine(offset + new Vector3(0f, -halfSize.y, 0f), offset + new Vector3(0f, halfSize.y, 0f));
            Gizmos.DrawLine(offset + new Vector3(-halfSize.x, -halfSize.y, 0f), offset + new Vector3(halfSize.x, halfSize.y, 0f));
            Gizmos.DrawLine(offset + new Vector3(-halfSize.x, halfSize.y, 0f), offset + new Vector3(halfSize.x, -halfSize.y, 0f));

            Gizmos.matrix = originalMatrix;
        }
#endif
    }
}
