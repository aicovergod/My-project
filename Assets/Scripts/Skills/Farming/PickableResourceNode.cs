using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Companions;
using Inventory;
using Player;
using Skills.Common;
using UI.Chat;

namespace Skills.Farming
{
    /// <summary>
    ///     Simple interactable resource node that hands the player a configured item when clicked.
    ///     The node respects the shared OSRS tick cadence for respawning so it slots into the
    ///     existing gathering infrastructure without bespoke timers.
    /// </summary>
    [AddComponentMenu("Skills/Farming/Pickable Resource Node")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class PickableResourceNode : TickedSkillBehaviour, IPointerClickHandler
    {
        [Header("Resource")]
        [SerializeField]
        [Tooltip("Identifier resolved through the shared ItemDatabase.")]
        private string itemId = string.Empty;

        [SerializeField]
        [Min(1)]
        [Tooltip("Number of items awarded per successful pick.")]
        private int quantity = 1;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds before the node respawns after being harvested.")]
        private float respawnSeconds = 60f;

        [Header("Feedback")]
        [SerializeField]
        [TextArea]
        [Tooltip("Optional chat line broadcast when the pick succeeds. Supports {itemName} and {quantity} tokens.")]
        private string chatFeedback = "You pick up {quantity} x {itemName}.";

        [Header("Interaction")]
        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Maximum distance allowed between the player and the node before harvesting is blocked.")]
        private float interactRange = 1.5f;

        [SerializeField]
        [Tooltip("Optional transform used when resolving the active player for range checks.")]
        private Transform playerAnchorOverride;

        [Header("Visual Toggles")]
        [SerializeField]
        [Tooltip("Renderers disabled while the node is depleted.")]
        private SpriteRenderer[] renderersToToggle = Array.Empty<SpriteRenderer>();

        [SerializeField]
        [Tooltip("Colliders disabled while the node is depleted.")]
        private Collider2D[] collidersToToggle = Array.Empty<Collider2D>();

        [SerializeField]
        [Tooltip("Additional objects toggled while the node is depleted (particles, props, etc.).")]
        private GameObject[] extraObjectsToToggle = Array.Empty<GameObject>();

        private bool isDepleted;
        private double respawnAt;
        private ItemData cachedItem;
        private string cachedItemId = string.Empty;
        private Transform cachedPlayerTransform;
        private PlayerMover cachedPlayerMover;

        /// <summary>
        ///     Auto-populates the toggle arrays from the current hierarchy so designers can quickly
        ///     reset the component when the node layout changes.
        /// </summary>
        private void Reset()
        {
            renderersToToggle = GetComponentsInChildren<SpriteRenderer>(true);
            collidersToToggle = GetComponentsInChildren<Collider2D>(true);

            var transforms = GetComponentsInChildren<Transform>(true);
            if (transforms.Length > 1)
            {
                var extras = new List<GameObject>(transforms.Length - 1);
                for (int i = 0; i < transforms.Length; i++)
                {
                    var t = transforms[i];
                    if (t != transform)
                        extras.Add(t.gameObject);
                }

                extraObjectsToToggle = extras.ToArray();
            }
            else
            {
                extraObjectsToToggle = Array.Empty<GameObject>();
            }
        }

        /// <summary>
        ///     Ensures numeric inspector values stay within sensible bounds and that cached arrays are never null.
        ///     The item cache is also invalidated when the identifier changes.
        /// </summary>
        private void OnValidate()
        {
            quantity = Mathf.Max(1, quantity);
            respawnSeconds = Mathf.Max(0f, respawnSeconds);
            interactRange = Mathf.Max(0.1f, interactRange);

            renderersToToggle ??= Array.Empty<SpriteRenderer>();
            collidersToToggle ??= Array.Empty<Collider2D>();
            extraObjectsToToggle ??= Array.Empty<GameObject>();

            if (!string.Equals(cachedItemId, itemId, StringComparison.Ordinal))
            {
                cachedItem = null;
                cachedItemId = string.Empty;
            }
        }

        /// <summary>
        ///     Caches commonly accessed components and resolves the configured item definition so runtime
        ///     clicks can be processed without repeated lookups.
        /// </summary>
        private void Awake()
        {
            EnsureComponentCaches();
            ResolveItem();
        }

        /// <summary>
        ///     Subscribes to the shared ticker during startup so respawn logic executes on the global cadence.
        /// </summary>
        private void Start()
        {
            TrySubscribeToTicker();
        }

        /// <summary>
        ///     Handles pointer clicks emitted by the Unity EventSystem.
        ///     Only primary button presses trigger harvesting and the node must not already be depleted.
        /// </summary>
        /// <param name="eventData">Pointer event payload provided by the EventSystem.</param>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            if (isDepleted)
                return;

            var inventory = CompanionManager.GetPlayerInventory();
            if (inventory == null)
            {
                Debug.LogWarning("PickableResourceNode could not locate the player inventory.", this);
                return;
            }

            var item = ResolveItem();
            if (item == null)
            {
                Debug.LogError("PickableResourceNode is missing a valid item definition. Ensure itemId maps to an ItemData asset.", this);
                return;
            }

            var playerTransform = ResolvePlayerTransform();
            if (playerTransform != null && interactRange > 0f)
            {
                // Range enforcement keeps gathering behaviour consistent with other skills.
                float distance = Vector2.Distance(playerTransform.position, transform.position);
                if (distance > interactRange)
                {
                    PublishChatMessage("You need to get closer to pick that.");
                    return;
                }
            }

            // Prefer checking capacity before attempting to add items so we can exit early without side effects.
            if (!inventory.CanAddItem(item, quantity) || !inventory.AddItem(item, quantity))
            {
                PublishChatMessage("Your inventory is full");
                return;
            }

            PublishChatMessage(ComposeChatMessage(item));
            eventData.Use();

            BeginRespawnCountdown();
        }

        /// <summary>
        ///     Evaluates whether the node should respawn on this tick. The check only runs while the node is depleted.
        /// </summary>
        protected override void HandleTick()
        {
            if (!isDepleted)
                return;

            if (Time.timeAsDouble >= respawnAt)
                RespawnNow();
        }

        /// <summary>
        ///     Ensures the serialized arrays always point to instantiated collections before they are accessed at runtime.
        /// </summary>
        private void EnsureComponentCaches()
        {
            renderersToToggle ??= Array.Empty<SpriteRenderer>();
            collidersToToggle ??= Array.Empty<Collider2D>();
            extraObjectsToToggle ??= Array.Empty<GameObject>();
        }

        /// <summary>
        ///     Enables or disables the visual/collider arrays depending on whether the node is active.
        /// </summary>
        /// <param name="visible">True to show and enable the node, false to hide and disable it.</param>
        private void SetNodeActive(bool visible)
        {
            for (int i = 0; i < renderersToToggle.Length; i++)
            {
                var renderer = renderersToToggle[i];
                if (renderer != null)
                    renderer.enabled = visible;
            }

            for (int i = 0; i < collidersToToggle.Length; i++)
            {
                var collider = collidersToToggle[i];
                if (collider != null)
                    collider.enabled = visible;
            }

            for (int i = 0; i < extraObjectsToToggle.Length; i++)
            {
                var obj = extraObjectsToToggle[i];
                if (obj != null)
                    obj.SetActive(visible);
            }
        }

        /// <summary>
        ///     Resolves the configured item through the shared <see cref="ItemDatabase"/>. The lookup result is cached until the
        ///     identifier changes so repeated interactions remain fast.
        /// </summary>
        /// <returns>The resolved <see cref="ItemData"/>, or null when the identifier is empty or invalid.</returns>
        private ItemData ResolveItem()
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                cachedItem = null;
                cachedItemId = string.Empty;
                return null;
            }

            if (!string.Equals(cachedItemId, itemId, StringComparison.Ordinal) || cachedItem == null)
            {
                cachedItem = ItemDatabase.GetItem(itemId);
                cachedItemId = itemId;
            }

            return cachedItem;
        }

        /// <summary>
        ///     Retrieves the active player transform so range checks can run without repeated scene queries.
        /// </summary>
        /// <returns>The player transform if available; otherwise, null.</returns>
        private Transform ResolvePlayerTransform()
        {
            if (playerAnchorOverride != null)
                return playerAnchorOverride;

            if (cachedPlayerTransform != null)
                return cachedPlayerTransform;

            if (cachedPlayerMover == null)
                cachedPlayerMover = FindObjectOfType<PlayerMover>();

            if (cachedPlayerMover != null)
            {
                cachedPlayerTransform = cachedPlayerMover.transform;
                return cachedPlayerTransform;
            }

            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                cachedPlayerTransform = playerObject.transform;
                return cachedPlayerTransform;
            }

            return null;
        }

        /// <summary>
        ///     Formats the configured chat feedback message, replacing supported tokens with runtime values.
        /// </summary>
        /// <param name="item">Item definition granted to the player.</param>
        /// <returns>The composed chat line, or an empty string when no message should be emitted.</returns>
        private string ComposeChatMessage(ItemData item)
        {
            if (string.IsNullOrWhiteSpace(chatFeedback))
                return string.Empty;

            string displayName = item != null && !string.IsNullOrWhiteSpace(item.itemName)
                ? item.itemName
                : (!string.IsNullOrWhiteSpace(itemId) ? itemId : "item");

            string message = chatFeedback.Replace("{itemName}", displayName);
            message = message.Replace("{quantity}", quantity.ToString());
            return message;
        }

        /// <summary>
        ///     Publishes a system chat message when a non-empty payload is provided and the chat service exists.
        /// </summary>
        /// <param name="message">Text to deliver to the Game channel.</param>
        private void PublishChatMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var chatService = ChatService.Instance;
            if (chatService != null)
                chatService.PublishGameMessage(message);
        }

        /// <summary>
        ///     Marks the node as depleted and starts (or skips) the respawn timer as appropriate.
        /// </summary>
        private void BeginRespawnCountdown()
        {
            isDepleted = true;
            SetNodeActive(false);

            if (respawnSeconds <= 0f)
            {
                RespawnNow();
                return;
            }

            respawnAt = Time.timeAsDouble + respawnSeconds;
        }

        /// <summary>
        ///     Restores the node to an active state and clears cached runtime state so the next interaction behaves as expected.
        /// </summary>
        private void RespawnNow()
        {
            isDepleted = false;
            respawnAt = 0d;
            SetNodeActive(true);

            // Clearing the cached player transform ensures we reacquire the latest instance after scene loads.
            cachedPlayerTransform = null;
        }
    }
}
