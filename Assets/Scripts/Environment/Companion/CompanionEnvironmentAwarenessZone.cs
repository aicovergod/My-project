using System;
using System.Collections.Generic;
using Companions;
using Inventory;
using UI.Chat;
using UnityEngine;

namespace Environment.Companion
{
    /// <summary>
    /// Detects when the active companion enters a tagged environmental zone and, with a 1-in-20 chance,
    /// prompts them to react with flavour dialogue pulled from <see cref="CompanionChatLibrary"/>.
    /// The component should be attached to trigger colliders that bound contextual points of interest
    /// such as banks, caves, or coastal regions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class CompanionEnvironmentAwarenessZone : MonoBehaviour
    {
        private enum AwarenessArea
        {
            Bank,
            MiningCave,
            MiningShop,
            GoblinCave,
            Ocean,
            Graveyard
        }

        private const int TriggerChanceDenominator = 20;

        /// <summary>
        /// Global toggle that controls whether this component mirrors its decision making to the console.
        /// The Admin F2 menu wires directly into this flag so QA can inspect why a companion did or did not
        /// react when entering an awareness zone.
        /// </summary>
        public static bool EnableDebugLogging { get; set; }

        /// <summary>Prefix included in every debug log so the output is easy to filter in the Unity console.</summary>
        private const string DebugLogPrefix = "[CompanionAwarenessZone]";

        [Header("Area Flags"), Tooltip("Mark the contextual identity of this zone so companions know how to react.")]
        [SerializeField]
        private bool areaIsBank;

        [SerializeField]
        private bool areaIsMiningCave;

        [SerializeField]
        private bool areaIsMiningShop;

        [SerializeField]
        private bool areaIsGoblinCave;

        [SerializeField]
        private bool areaIsOcean;

        [SerializeField]
        private bool areaIsGraveyard;

        [Header("Cooldown"), Tooltip("Minimum number of seconds before the same companion can react again inside this zone.")]
        [SerializeField, Min(0f)]
        private float retriggerCooldownSeconds = 10f;

        /// <summary>Keeps track of the last time each companion reacted so we can apply per-zone cooldowns.</summary>
        private readonly Dictionary<CompanionController, float> lastReactionTimes = new Dictionary<CompanionController, float>();

        /// <summary>Reusable buffer that stores the active flags for the current evaluation.</summary>
        private readonly List<AwarenessArea> activeAreas = new List<AwarenessArea>(6);

        /// <summary>Cached collider reference so we can confirm trigger state during <see cref="Awake"/>.</summary>
        private Collider2D cachedCollider;

        /// <summary>Stores the most recent awareness area used to generate a reaction so debug logs can report it.</summary>
        private AwarenessArea lastSelectedArea;

        private void Awake()
        {
            cachedCollider = GetComponent<Collider2D>();
            if (cachedCollider == null)
                return;

            if (!cachedCollider.isTrigger)
            {
                cachedCollider.isTrigger = true;
                Debug.LogWarning(
                    $"{nameof(CompanionEnvironmentAwarenessZone)} on {name} requires its collider to be marked as a trigger. The flag has been enabled automatically.",
                    this);
            }
        }

        private void OnDisable()
        {
            lastReactionTimes.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var controller = ResolveCompanionController(other);
            if (controller == null)
            {
                LogDebug("Ignored trigger enter because no companion controller was found on the collider hierarchy.");
                return;
            }

            LogDebug($"Companion {controller.name} entered zone; evaluating awareness reaction.", controller);

            if (!HasAnyAreaFlag())
            {
                LogDebug("Zone has no area flags configured; skipping reaction.", controller);
                return;
            }

            bool shouldReact = RollTrigger();
            if (!shouldReact)
            {
                LogDebug("1-in-20 awareness roll failed; companion remains silent.", controller);
                return;
            }

            float now = Time.time;
            if (IsOnCooldown(controller, now, out float remainingCooldown))
            {
                LogDebug($"Companion is on cooldown for another {Mathf.Max(remainingCooldown, 0f):0.00}s; reaction aborted.", controller);
                return;
            }

            string message = BuildReaction(controller);
            if (string.IsNullOrWhiteSpace(message))
            {
                LogDebug("No awareness message could be resolved for the current zone configuration.", controller);
                return;
            }

            LogDebug($"Awareness roll succeeded for area {lastSelectedArea}; broadcasting: \"{message}\".", controller);

            var chat = ChatService.Instance;
            if (chat == null)
            {
                LogDebug("ChatService instance is unavailable; cannot publish awareness message.", controller);
                return;
            }

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
            lastReactionTimes[controller] = now;
            LogDebug("Awareness message published successfully; cooldown timer refreshed.", controller);
        }

        /// <summary>Resolves the <see cref="CompanionController"/> tied to the supplied collider.</summary>
        private static CompanionController ResolveCompanionController(Collider2D collider)
        {
            if (collider == null)
                return null;

            return collider.GetComponent<CompanionController>() ?? collider.GetComponentInParent<CompanionController>();
        }

        /// <summary>Returns <c>true</c> when any area flag is active on this zone.</summary>
        private bool HasAnyAreaFlag()
        {
            return areaIsBank || areaIsMiningCave || areaIsMiningShop || areaIsGoblinCave || areaIsOcean || areaIsGraveyard;
        }

        /// <summary>Rolls the 1-in-20 entry chance that governs whether the companion speaks.</summary>
        private static bool RollTrigger()
        {
            return UnityEngine.Random.Range(0, TriggerChanceDenominator) == 0;
        }

        /// <summary>Determines if the supplied companion is still cooling down from a previous reaction.</summary>
        private bool IsOnCooldown(CompanionController controller, float now, out float remainingCooldownSeconds)
        {
            remainingCooldownSeconds = 0f;
            if (controller == null)
                return true;

            if (!lastReactionTimes.TryGetValue(controller, out float lastTime))
                return false;

            float elapsed = now - lastTime;
            remainingCooldownSeconds = retriggerCooldownSeconds - elapsed;
            return remainingCooldownSeconds > 0f;
        }

        /// <summary>Builds the flavour message appropriate for the currently active zone flags.</summary>
        private string BuildReaction(CompanionController controller)
        {
            lastSelectedArea = default;
            activeAreas.Clear();
            if (areaIsBank)
                activeAreas.Add(AwarenessArea.Bank);
            if (areaIsMiningCave)
                activeAreas.Add(AwarenessArea.MiningCave);
            if (areaIsMiningShop)
                activeAreas.Add(AwarenessArea.MiningShop);
            if (areaIsGoblinCave)
                activeAreas.Add(AwarenessArea.GoblinCave);
            if (areaIsOcean)
                activeAreas.Add(AwarenessArea.Ocean);
            if (areaIsGraveyard)
                activeAreas.Add(AwarenessArea.Graveyard);

            if (activeAreas.Count == 0)
                return string.Empty;

            AwarenessArea selected = activeAreas[UnityEngine.Random.Range(0, activeAreas.Count)];
            lastSelectedArea = selected;
            switch (selected)
            {
                case AwarenessArea.Bank:
                    return CompanionChatLibrary.GetRandomBankAwarenessLine(IsInventoryFull(controller?.Inventory?.InventoryComponent));
                case AwarenessArea.MiningCave:
                    return CompanionChatLibrary.GetRandomMiningCaveAwarenessLine();
                case AwarenessArea.MiningShop:
                    return CompanionChatLibrary.GetRandomMiningShopAwarenessLine();
                case AwarenessArea.GoblinCave:
                    return CompanionChatLibrary.GetRandomGoblinCaveAwarenessLine();
                case AwarenessArea.Ocean:
                    return CompanionChatLibrary.GetRandomOceanAwarenessLine();
                case AwarenessArea.Graveyard:
                    return CompanionChatLibrary.GetRandomGraveyardAwarenessLine();
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the companion inventory exists and every slot is filled to capacity.
        /// Stackable entries must also be capped at their maximum stack size.
        /// </summary>
        private static bool IsInventoryFull(Inventory.Inventory inventory)
        {
            if (inventory == null)
                return false;

            var model = inventory.Model;
            if (model == null)
                return false;

            int slotCount = model.Size;
            for (int i = 0; i < slotCount; i++)
            {
                InventoryEntry entry = model.GetEntry(i);
                if (entry.item == null)
                    return false;

                if (entry.item.stackable && entry.count < entry.item.MaxStack)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Emits a formatted debug log when <see cref="EnableDebugLogging"/> is active.
        /// Provides the zone name and companion context so QA can trace behaviour in the console.
        /// </summary>
        private void LogDebug(string message, CompanionController controller = null)
        {
            if (!EnableDebugLogging)
                return;

            string companionName = controller != null ? controller.name : "None";
            Debug.Log($"{DebugLogPrefix} Zone: {name}, Companion: {companionName} => {message}", this);
        }
    }
}
