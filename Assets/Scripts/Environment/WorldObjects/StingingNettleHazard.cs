using System.Collections.Generic;
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

        private bool playerInsideArea;
        private bool tickerSubscribed;
        private bool nettleDialogueTriggered;
        private int nettleDialogueCooldownTicksRemaining;

        /// <summary>
        /// Tracks the shared nettle dialogue cooldown across every hazard instance so companions only react once
        /// regardless of how many nettle tiles the player clips during the same window.
        /// </summary>
        private static int globalNettleDialogueCooldownTicksRemaining;

        /// <summary>
        /// Records the last Unity frame that processed the global nettle cooldown so multiple hazards do not
        /// accidentally decrement the shared timer more than once per tick.
        /// </summary>
        private static int lastGlobalCooldownFrame = -1;

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
            StingingNettleContactCoordinator.BeginTick();
            TickGlobalDialogueCooldown();
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
                TryEmitCompanionDialogue();
                if (enableDebugLogging)
                {
                    Debug.Log($"{DebugLogPrefix} Player entered hazard tile.", this);
                }
            }

            StingingNettleContactCoordinator.RegisterContact(this, ticksBetweenDamage);
            StingingNettleContactCoordinator.MarkContactActiveThisTick(this);

            if (StingingNettleContactCoordinator.ShouldApplyDamage(this))
            {
                ApplyDamage();
            }
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
            if (playerInsideArea && enableDebugLogging)
            {
                Debug.Log($"{DebugLogPrefix} Player exited hazard tile.", this);
            }

            playerInsideArea = false;
            StingingNettleContactCoordinator.UnregisterContact(this);
        }

        /// <summary>
        /// Centralises the player's contact state across every nettle hazard so damage ticks
        /// are shared instead of stacking per overlapping instance.
        /// </summary>
        private static class StingingNettleContactCoordinator
        {
            private static readonly Dictionary<StingingNettleHazard, int> ActiveHazards = new Dictionary<StingingNettleHazard, int>();

            private static readonly TickListener SharedTickListener = new TickListener();

            private static int sharedContactTickCounter;
            private static int currentTickId;
            private static int lastProcessedTickId = -1;
            private static int lastContactIncrementTickId = -1;
            private static int pendingClearTickId = -1;
            private static bool damageAppliedThisTick;
            private static bool tickListenerSubscribed;
            private static Ticker cachedTicker;

            /// <summary>
            /// Ensures the shared tick listener is registered before hazards subscribe so we can track tick boundaries reliably.
            /// </summary>
            public static void EnsureTickListener()
            {
                Ticker ticker = Ticker.Instance;
                if (ticker == null)
                {
                    tickListenerSubscribed = false;
                    cachedTicker = null;
                    sharedContactTickCounter = 0;
                    currentTickId = 0;
                    lastProcessedTickId = -1;
                    lastContactIncrementTickId = -1;
                    pendingClearTickId = -1;
                    damageAppliedThisTick = false;
                    return;
                }

                if (cachedTicker != ticker)
                {
                    cachedTicker = ticker;
                    tickListenerSubscribed = false;
                    currentTickId = 0;
                    lastProcessedTickId = -1;
                    lastContactIncrementTickId = -1;
                    pendingClearTickId = -1;
                    damageAppliedThisTick = false;
                    sharedContactTickCounter = 0;
                }

                if (!tickListenerSubscribed)
                {
                    ticker.Subscribe(SharedTickListener);
                    tickListenerSubscribed = true;
                }
            }

            /// <summary>
            /// Prepares the coordinator for a new tick and ensures per-tick flags reset only once.
            /// </summary>
            public static void BeginTick()
            {
                EnsureTickListener();

                if (currentTickId == lastProcessedTickId)
                {
                    return;
                }

                lastProcessedTickId = currentTickId;
                damageAppliedThisTick = false;

                if (pendingClearTickId >= 0 && currentTickId > pendingClearTickId)
                {
                    sharedContactTickCounter = 0;
                    lastContactIncrementTickId = -1;
                    pendingClearTickId = -1;
                }
                else if (ActiveHazards.Count == 0)
                {
                    sharedContactTickCounter = 0;
                    lastContactIncrementTickId = -1;
                }
            }

            /// <summary>
            /// Registers or refreshes a hazard that currently overlaps the player.
            /// </summary>
            public static void RegisterContact(StingingNettleHazard hazard, int ticksBetweenDamage)
            {
                ActiveHazards[hazard] = Mathf.Max(1, ticksBetweenDamage);
                pendingClearTickId = -1;
            }

            /// <summary>
            /// Records that at least one hazard touched the player during the current tick and advances the shared counter.
            /// </summary>
            public static void MarkContactActiveThisTick(StingingNettleHazard hazard)
            {
                if (!ActiveHazards.ContainsKey(hazard) || ActiveHazards.Count == 0)
                {
                    return;
                }

                if (lastContactIncrementTickId == currentTickId)
                {
                    return;
                }

                lastContactIncrementTickId = currentTickId;
                sharedContactTickCounter++;
            }

            /// <summary>
            /// Removes a hazard from the contact set when the player exits or the hazard disables.
            /// </summary>
            public static void UnregisterContact(StingingNettleHazard hazard)
            {
                if (!ActiveHazards.Remove(hazard))
                {
                    return;
                }

                if (ActiveHazards.Count == 0)
                {
                    pendingClearTickId = currentTickId;
                }
            }

            /// <summary>
            /// Determines whether this hazard should apply damage on the current tick.
            /// Only one hazard will succeed each tick even if multiple overlap the player.
            /// </summary>
            public static bool ShouldApplyDamage(StingingNettleHazard hazard)
            {
                if (!ActiveHazards.ContainsKey(hazard))
                {
                    return false;
                }

                if (ActiveHazards.Count == 0)
                {
                    return false;
                }

                int requiredTicks = int.MaxValue;
                foreach (var kvp in ActiveHazards)
                {
                    if (kvp.Value < requiredTicks)
                    {
                        requiredTicks = kvp.Value;
                    }
                }

                requiredTicks = Mathf.Max(1, requiredTicks);

                if (sharedContactTickCounter < requiredTicks)
                {
                    return false;
                }

                if (damageAppliedThisTick)
                {
                    return false;
                }

                damageAppliedThisTick = true;
                sharedContactTickCounter = 0;
                lastContactIncrementTickId = currentTickId;
                return true;
            }

            /// <summary>
            /// Simple ticker proxy that increments the shared tick identifier once per OSRS tick.
            /// </summary>
            private sealed class TickListener : ITickable
            {
                public void OnTick()
                {
                    currentTickId++;
                }
            }
        }

        private void TryEmitCompanionDialogue()
        {
            if (nettleDialogueTriggered || nettleDialogueCooldownTicksRemaining > 0)
            {
                return;
            }

            if (globalNettleDialogueCooldownTicksRemaining > 0)
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
            int computedCooldownTicks = CalculateDialogueCooldownTicks();
            nettleDialogueCooldownTicksRemaining = computedCooldownTicks;
            globalNettleDialogueCooldownTicksRemaining = Mathf.Max(globalNettleDialogueCooldownTicksRemaining, computedCooldownTicks);
            lastGlobalCooldownFrame = Time.frameCount;
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

        /// <summary>
        /// Converts the configured cooldown seconds into the equivalent number of OSRS ticks, clamped to a minimum of one.
        /// </summary>
        private int CalculateDialogueCooldownTicks()
        {
            int ticks = Mathf.CeilToInt(nettleDialogueCooldownSeconds / Ticker.TickDuration);
            return Mathf.Max(1, ticks);
        }

        /// <summary>
        /// Updates the shared nettle dialogue cooldown exactly once per Unity frame so multiple hazards do not
        /// shorten the cooldown unintentionally.
        /// </summary>
        private static void TickGlobalDialogueCooldown()
        {
            if (globalNettleDialogueCooldownTicksRemaining <= 0)
            {
                return;
            }

            int currentFrame = Time.frameCount;
            if (currentFrame == lastGlobalCooldownFrame)
            {
                return;
            }

            globalNettleDialogueCooldownTicksRemaining = Mathf.Max(0, globalNettleDialogueCooldownTicksRemaining - 1);
            lastGlobalCooldownFrame = currentFrame;
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

            StingingNettleContactCoordinator.EnsureTickListener();
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
