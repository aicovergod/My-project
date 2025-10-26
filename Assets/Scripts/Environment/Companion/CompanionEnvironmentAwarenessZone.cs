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
            GoblinCave,
            Ocean,
            Graveyard
        }

        private const int TriggerChanceDenominator = 20;

        [Header("Area Flags"), Tooltip("Mark the contextual identity of this zone so companions know how to react.")]
        [SerializeField]
        private bool areaIsBank;

        [SerializeField]
        private bool areaIsMiningCave;

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
        private readonly List<AwarenessArea> activeAreas = new List<AwarenessArea>(5);

        /// <summary>Cached collider reference so we can confirm trigger state during <see cref="Awake"/>.</summary>
        private Collider2D cachedCollider;

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
                return;

            if (!HasAnyAreaFlag())
                return;

            if (!RollTrigger())
                return;

            float now = Time.time;
            if (IsOnCooldown(controller, now))
                return;

            string message = BuildReaction(controller);
            if (string.IsNullOrWhiteSpace(message))
                return;

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
            lastReactionTimes[controller] = now;
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
            return areaIsBank || areaIsMiningCave || areaIsGoblinCave || areaIsOcean || areaIsGraveyard;
        }

        /// <summary>Rolls the 1-in-20 entry chance that governs whether the companion speaks.</summary>
        private static bool RollTrigger()
        {
            return UnityEngine.Random.Range(0, TriggerChanceDenominator) == 0;
        }

        /// <summary>Determines if the supplied companion is still cooling down from a previous reaction.</summary>
        private bool IsOnCooldown(CompanionController controller, float now)
        {
            if (controller == null)
                return true;

            if (!lastReactionTimes.TryGetValue(controller, out float lastTime))
                return false;

            return now - lastTime < retriggerCooldownSeconds;
        }

        /// <summary>Builds the flavour message appropriate for the currently active zone flags.</summary>
        private string BuildReaction(CompanionController controller)
        {
            activeAreas.Clear();
            if (areaIsBank)
                activeAreas.Add(AwarenessArea.Bank);
            if (areaIsMiningCave)
                activeAreas.Add(AwarenessArea.MiningCave);
            if (areaIsGoblinCave)
                activeAreas.Add(AwarenessArea.GoblinCave);
            if (areaIsOcean)
                activeAreas.Add(AwarenessArea.Ocean);
            if (areaIsGraveyard)
                activeAreas.Add(AwarenessArea.Graveyard);

            if (activeAreas.Count == 0)
                return string.Empty;

            AwarenessArea selected = activeAreas[UnityEngine.Random.Range(0, activeAreas.Count)];
            switch (selected)
            {
                case AwarenessArea.Bank:
                    return CompanionChatLibrary.GetRandomBankAwarenessLine(IsInventoryFull(controller?.Inventory?.InventoryComponent));
                case AwarenessArea.MiningCave:
                    return CompanionChatLibrary.GetRandomMiningCaveAwarenessLine();
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
    }
}
