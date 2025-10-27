// Assets/Scripts/Inventory/OreBag/OreBagInventory.cs
using System;
using System.Collections.Generic;
using Inventory;
using Inventory.Core;
using Skills.Mining;
using UI;
using UnityEngine;
using RuntimeInventory = global::Inventory.Inventory;

namespace Inventory.OreBag
{
    /// <summary>
    /// Dedicated inventory wrapper for the ore bag. Enforces ore-only storage,
    /// 12-slot (3x4) layout, and a combined capacity derived from the active bag tier.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RuntimeInventory))]
    public sealed class OreBagInventory : MonoBehaviour
    {
        private static HashSet<string> oreItemIds;
        private static bool globalDebugLogging = false;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("When enabled the ore bag prints verbose logging for persistence, sanitisation, and transfer flows.")]
        private bool enableDebugLogging = false;

        private RuntimeInventory inventory;
        private InventoryModel model;
        private OreBagItemData activeBagDefinition;
        private int activeCapacity;
        private bool inventoryChangeSubscribed;
        private bool sanitizedAfterInitialRestore;

        /// <summary>
        /// Allows <see cref="OreBagService"/> and the Admin F2 menu to toggle verbose logging at runtime.
        /// </summary>
        internal bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set
            {
                if (enableDebugLogging == value && globalDebugLogging == value)
                    return;

                enableDebugLogging = value;
                globalDebugLogging = value;

                Debug.Log($"[OreBagInventory] Debug logging {(value ? "enabled" : "disabled")}.", this);
            }
        }

        /// <summary>Expose the underlying inventory component for UI refreshes.</summary>
        public RuntimeInventory InventoryComponent => inventory;

        /// <summary>Combined ore capacity enforced for the currently active bag tier.</summary>
        public int CurrentCapacity => activeCapacity;

        private void Awake()
        {
            globalDebugLogging = enableDebugLogging;
            Log("Awake invoked. Ensuring inventory is configured.");
            EnsureInventoryConfigured();
        }

        /// <summary>
        /// Ensures the runtime inventory component is initialised with the ore bag layout
        /// and filtering rules before the component participates in save/load.
        /// </summary>
        public void EnsureInventoryConfigured()
        {
            Log("EnsureInventoryConfigured invoked.");

            if (inventory == null)
            {
                Log("Caching runtime inventory component reference.");
                inventory = GetComponent<RuntimeInventory>();
            }

            if (inventory == null)
            {
                LogWarning("Runtime inventory component missing. Configuration aborted.");
                return;
            }

            ConfigureInventoryWindow();

            Log("Inventory window configured. Resolving inventory model.");

            model = inventory.Model;
            model.CanStoreRule = CanStoreOreOnly;

            if (!inventoryChangeSubscribed && inventory != null)
            {
                inventory.OnInventoryChanged += HandleInventoryChanged;
                inventoryChangeSubscribed = true;
                Log("Subscribed to inventory change notifications for sanitisation.");
            }
        }

        /// <summary>
        /// Scrubs any non-ore data that may have been restored into the bag before the
        /// dedicated save migrated. Valid ore entries are preserved in their original
        /// slots so the player retains stack positions.
        /// </summary>
        public void SanitizeLoadedContents()
        {
            Log("SanitizeLoadedContents invoked. Verifying restored slots for non-ore data.");
            EnsureInventoryConfigured();

            if (inventory == null || model == null)
            {
                LogWarning("Cannot sanitize contents because the inventory or model reference is missing.");
                return;
            }

            // Cache valid ore entries so their original slot positions can be restored after scrubbing.
            var validEntries = new List<(int slotIndex, InventoryEntry entry)>();
            bool scrubbed = false;

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);

                Log($"Inspecting slot {i}: item={(entry.item != null ? entry.item.id : "<null>")}, count={entry.count}.");

                if (entry.item == null)
                {
                    if (entry.count != 0)
                    {
                        LogWarning($"Slot {i} contained a null item with count {entry.count}. Marking for scrub.");
                        scrubbed = true;
                    }
                    continue;
                }

                if (!IsOre(entry.item) || entry.count <= 0)
                {
                    LogWarning($"Slot {i} contained non-ore item {entry.item.id} (count {entry.count}). Removing entry.");
                    scrubbed = true;
                    continue;
                }

                validEntries.Add((i, new InventoryEntry
                {
                    item = entry.item,
                    count = entry.count
                }));
            }

            if (!scrubbed)
            {
                Log("No sanitisation required. All restored entries already valid ores.");
                return;
            }

            inventory.RunWithoutPersistence(targetModel =>
            {
                // Clear everything so only verified ore stacks return to the bag.
                Log("Scrub detected. Clearing and restoring verified ore entries.");
                targetModel.ClearAllSlots();
                foreach (var (slotIndex, entry) in validEntries)
                {
                    Log($"Restoring ore {entry.item.id} x{entry.count} into slot {slotIndex}.");
                    targetModel.SetEntry(slotIndex, entry);
                }
            });

            Log("Saving sanitized ore bag payload.");
            inventory.Save();
        }

        /// <summary>Copies fonts/visual settings from the player inventory for consistent styling.</summary>
        public void SyncStylingFrom(RuntimeInventory source)
        {
            if (source == null || inventory == null || source == inventory)
                return;

            Log("Syncing ore bag UI styling from player inventory.");
            inventory.windowColor = source.windowColor;
            inventory.slotSize = source.slotSize;
            inventory.slotSpacing = source.slotSpacing;
            inventory.windowPadding = source.windowPadding;
            inventory.windowSize = source.windowSize;
            inventory.stackCountFont = source.stackCountFont ?? LegacyFontProvider.GetLegacyFont();
            inventory.tooltipNameFont = source.tooltipNameFont ?? LegacyFontProvider.GetLegacyFont();
            inventory.tooltipDescriptionFont = source.tooltipDescriptionFont ?? LegacyFontProvider.GetLegacyFont();
            inventory.combinationDatabase = source.combinationDatabase;
            inventory.RefreshWindowLayout();
        }

        /// <summary>Applies the supplied bag definition so the capacity limit matches the active tier.</summary>
        public void ApplyBagDefinition(OreBagItemData bag)
        {
            activeBagDefinition = bag;
            activeCapacity = bag != null ? bag.OreCapacity : 0;
            Log(bag == null
                ? "Cleared active bag definition. Capacity reset to zero."
                : $"Applied bag definition {bag.name} (tier {bag.Tier}) with capacity {activeCapacity}.");
        }

        /// <summary>Total number of ores currently stored in the bag.</summary>
        public int GetCurrentOreCount()
        {
            if (model == null)
                return 0;

            int total = 0;
            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item != null)
                    total += entry.count;
            }

            Log($"Computed current ore count: {total}.");
            return total;
        }

        /// <summary>Remaining combined capacity before the bag reaches its tier limit.</summary>
        public int GetCapacityRemaining()
        {
            int remaining = Mathf.Max(0, activeCapacity - GetCurrentOreCount());
            Log($"Capacity remaining: {remaining} (active capacity {activeCapacity}).");
            return remaining;
        }

        /// <summary>
        /// Attempts to add up to <paramref name="quantity"/> ores, respecting tier capacity and slot availability.
        /// Returns the number of ores successfully stored. The ore bag treats every ore as stackable regardless
        /// of the underlying <see cref="ItemData"/> configuration so identical ores always occupy a single slot.
        /// </summary>
        public int AddOre(ItemData item, int quantity)
        {
            Log($"AddOre requested for item={(item != null ? item.id : "<null>")} quantity={quantity}.");
            if (!IsOre(item) || quantity <= 0)
            {
                LogWarning("AddOre rejected because the item is not a valid ore or quantity was non-positive.");
                return 0;
            }

            int remainingCapacity = GetCapacityRemaining();
            if (remainingCapacity <= 0)
            {
                LogWarning("AddOre aborted because the bag has no remaining capacity.");
                return 0;
            }

            int toAdd = Mathf.Min(quantity, remainingCapacity);
            int added = 0;

            if (model == null)
            {
                Log("Inventory model missing. Attempting to reconfigure before adding ore.");
                EnsureInventoryConfigured();
            }

            if (model == null)
            {
                LogWarning("AddOre failed because the inventory model could not be resolved.");
                return 0;
            }

            // Merge any pre-existing stacks for this ore so the bag can reclaim slots that were previously
            // fragmented by per-item entries (the behaviour reported by the user).
            Log("Collapsing existing stacks for incoming ore type.");
            MergeExistingStacksForOre(item);

            // Try to top up an existing stack first so the bag retains a single stack per ore type.
            int primaryIndex = FindFirstSlotWithItem(item);
            if (primaryIndex != -1)
            {
                Log($"Found existing stack for {item.id} in slot {primaryIndex}. Attempting to top up by {toAdd}.");
                var entry = model.GetEntry(primaryIndex);
                int addToExisting = Mathf.Min(toAdd, Mathf.Max(0, int.MaxValue - entry.count));
                if (addToExisting > 0)
                {
                    entry.count += addToExisting;
                    if (model.SetEntry(primaryIndex, entry))
                    {
                        added += addToExisting;
                        toAdd -= addToExisting;
                        Log($"Topped up existing stack by {addToExisting}. Remaining to allocate: {toAdd}.");
                    }
                }
            }

            // If there is still ore remaining to be stored, allocate a fresh slot.
            if (toAdd > 0)
            {
                int emptyIndex = FindFirstEmptySlot();
                if (emptyIndex != -1)
                {
                    Log($"Allocating new slot {emptyIndex} for {item.id} with quantity {toAdd}.");
                    var entry = new InventoryEntry
                    {
                        item = item,
                        count = toAdd
                    };

                    if (model.SetEntry(emptyIndex, entry))
                    {
                        added += toAdd;
                        toAdd = 0;
                        Log("New slot allocation succeeded.");
                    }
                }
                else
                {
                    LogWarning("No empty slot available while attempting to add ore. Remaining quantity could not be stored.");
                }
            }

            if (added > 0)
            {
                Log($"AddOre completed. Total stored: {added}. Triggering window refresh.");
                inventory.WindowController?.RefreshAllSlots();
            }
            else
            {
                LogWarning("AddOre completed without storing any ore.");
            }

            return added;
        }

        /// <summary>Opens the ore bag UI window.</summary>
        public void OpenWindow()
        {
            Log("Opening ore bag window.");
            inventory.OpenUI();
        }

        /// <summary>Closes the ore bag UI window.</summary>
        public void CloseWindow()
        {
            Log("Closing ore bag window.");
            inventory.CloseUI();
        }

        /// <summary>Returns true when the supplied item is recognised as an ore.</summary>
        public bool IsOre(ItemData item)
        {
            if (item == null)
            {
                LogWarning("IsOre called with a null item reference.");
                return false;
            }

            // Prevent the ore bag item itself from being considered an ore so players cannot
            // accidentally stash the bag inside its own storage and lose access to it.
            if (item is OreBagItemData)
            {
                LogWarning("IsOre rejected the ore bag item itself to prevent recursive storage.");
                return false;
            }

            EnsureOreItemIds();
            bool result = oreItemIds.Contains(item.id);
            Log($"IsOre evaluated item {item.id}: {(result ? "VALID" : "INVALID")} ore entry.");
            return result;
        }

        private void ConfigureInventoryWindow()
        {
            Log("Configuring ore bag inventory window defaults.");
            inventory.saveKey = "OreBagInventory";
            inventory.size = 12;
            inventory.columns = 3;
            inventory.useSharedUIRoot = false;
            inventory.centerOnScreen = true;
            inventory.showCloseButton = true;
            inventory.slotSize = new Vector2(64f, 64f);
            inventory.slotSpacing = new Vector2(4f, 4f);
            inventory.windowPadding = new Vector2(8f, 8f);
            inventory.windowSize = new Vector2(256f, 340f);
            inventory.windowColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            inventory.referenceResolution = new Vector2(1024f, 768f);
            inventory.stackCountFont = LegacyFontProvider.GetLegacyFont();
            inventory.tooltipNameFont = LegacyFontProvider.GetLegacyFont();
            inventory.tooltipDescriptionFont = LegacyFontProvider.GetLegacyFont();

            inventory.RefreshWindowLayout();
            inventory.ForceDedicatedUiRoot();
            inventory.CloseUI();
        }

        private bool CanStoreOreOnly(ItemData item)
        {
            return IsOre(item);
        }

        private void HandleInventoryChanged()
        {
            if (sanitizedAfterInitialRestore)
                return;

            if (inventory == null || !inventory.isActiveAndEnabled)
                return;

            Log("Inventory change detected after restore. Scheduling single sanitisation pass.");
            sanitizedAfterInitialRestore = true;
            SanitizeLoadedContents();
        }

        private static void EnsureOreItemIds()
        {
            if (oreItemIds != null)
                return;

            oreItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var definitions = Resources.LoadAll<OreDefinition>("MiningDatabase");
            int definitionCount = 0;
            foreach (var def in definitions)
            {
                if (def != null && !string.IsNullOrEmpty(def.Id))
                {
                    oreItemIds.Add(def.Id);
                    definitionCount++;
                }
            }

            if (definitionCount > 0)
            {
                if (globalDebugLogging)
                    Debug.Log($"[OreBagInventory] Cached {definitionCount} ore definitions from the mining database.");
            }

            if (oreItemIds.Count > 0)
                return;

            var fallbackItems = Resources.LoadAll<ItemData>("Item");
            foreach (var candidate in fallbackItems)
            {
                if (candidate == null || string.IsNullOrEmpty(candidate.id))
                    continue;

                if (ContainsOreToken(candidate.itemName) || ContainsOreToken(candidate.id))
                    oreItemIds.Add(candidate.id);
            }

            if (globalDebugLogging)
                Debug.Log($"[OreBagInventory] Fallback ore cache populated with {oreItemIds.Count} entries.");
        }

        /// <summary>
        /// Returns true when the supplied value contains the standalone word "Ore".
        /// </summary>
        private static bool ContainsOreToken(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            int searchIndex = 0;
            while (searchIndex < value.Length)
            {
                int matchIndex = value.IndexOf("Ore", searchIndex, StringComparison.OrdinalIgnoreCase);
                if (matchIndex == -1)
                    return false;

                bool hasValidPrefix = matchIndex == 0 || !char.IsLetter(value[matchIndex - 1]);
                int suffixIndex = matchIndex + 3;
                bool hasValidSuffix = suffixIndex >= value.Length || !char.IsLetter(value[suffixIndex]);

                if (hasValidPrefix && hasValidSuffix)
                    return true;

                searchIndex = matchIndex + 3;
            }

            return false;
        }

        /// <summary>
        /// Collapses any duplicate stacks for the supplied ore so the bag keeps a single stack per ore type.
        /// </summary>
        private void MergeExistingStacksForOre(ItemData ore)
        {
            if (model == null || ore == null)
            {
                LogWarning("MergeExistingStacksForOre aborted because the model or ore reference is null.");
                return;
            }

            int primaryIndex = -1;
            long combinedCount = 0;
            List<int> duplicates = null;

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item == ore)
                {
                    if (entry.count <= 0)
                    {
                        // Remove empty remnants so the bag does not lose slots to zero-count stacks.
                        LogWarning($"Removing empty stack for {ore.id} in slot {i}.");
                        model.SetEntry(i, default);
                        continue;
                    }

                    combinedCount += entry.count;

                    if (primaryIndex == -1)
                    {
                        Log($"Slot {i} selected as primary stack for {ore.id}.");
                        primaryIndex = i;
                    }
                    else
                    {
                        duplicates ??= new List<int>();
                        Log($"Marking duplicate stack for {ore.id} in slot {i} for consolidation.");
                        duplicates.Add(i);
                    }
                }
                else if (entry.item == null && entry.count > 0)
                {
                    // Clean up invalid entries that may have been created by corrupted save data.
                    LogWarning($"Slot {i} contains null item with residual count {entry.count}. Clearing entry.");
                    model.SetEntry(i, default);
                }
            }

            if (primaryIndex == -1)
            {
                Log("No existing stacks found for incoming ore. Nothing to merge.");
                return;
            }

            var primary = model.GetEntry(primaryIndex);

            if (duplicates != null)
            {
                Log($"Consolidating {duplicates.Count} duplicate stacks for {ore.id}.");
                foreach (int duplicateIndex in duplicates)
                    model.SetEntry(duplicateIndex, default);
            }

            long sanitizedTotal = combinedCount < 0 ? 0 : combinedCount;
            int clampedTotal = sanitizedTotal > int.MaxValue ? int.MaxValue : (int)sanitizedTotal;
            if (primary.count != clampedTotal)
            {
                primary.count = clampedTotal;
                model.SetEntry(primaryIndex, primary);
                Log($"Primary stack for {ore.id} now contains {clampedTotal} after consolidation.");
            }
        }

        /// <summary>
        /// Finds the first slot containing <paramref name="item"/>. Returns -1 when no matching slot exists.
        /// </summary>
        private int FindFirstSlotWithItem(ItemData item)
        {
            if (model == null || item == null)
            {
                LogWarning("FindFirstSlotWithItem invoked with null model or item.");
                return -1;
            }

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item == item && entry.count > 0)
                {
                    Log($"Found item {item.id} in slot {i} while searching for existing stack.");
                    return i;
                }
            }

            Log($"No slot currently contains item {item.id}.");
            return -1;
        }

        /// <summary>
        /// Returns the index of the first empty slot that can be reused for ore storage.
        /// </summary>
        private int FindFirstEmptySlot()
        {
            if (model == null)
            {
                LogWarning("FindFirstEmptySlot invoked with null model reference.");
                return -1;
            }

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item == null || entry.count <= 0)
                {
                    Log($"Found empty slot at index {i}.");
                    return i;
                }
            }

            LogWarning("No empty slot available in ore bag inventory.");
            return -1;
        }

        private void OnDisable()
        {
            // Persist any ore layout adjustments before the bag is hidden so exiting to menus/desktops keeps the latest state.
            Log("OnDisable invoked. Saving ore bag state and resetting sanitisation guard.");
            inventory?.Save();
            sanitizedAfterInitialRestore = false;
        }

        private void OnDestroy()
        {
            // Mirror the disable safeguard in destruction paths so runtime teardown without disable still commits the bag state.
            if (inventory != null)
            {
                Log("OnDestroy saving ore bag inventory prior to teardown.");
                inventory.Save();
            }

            if (inventory != null && inventoryChangeSubscribed)
            {
                inventory.OnInventoryChanged -= HandleInventoryChanged;
                inventoryChangeSubscribed = false;
                Log("Unsubscribed from inventory change notifications on destroy.");
            }
        }

        /// <summary>Utility wrapper that always writes a debug message for the ore bag inventory.</summary>
        private void Log(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[OreBagInventory] {message}", this);
        }

        /// <summary>Utility wrapper that always writes a warning for the ore bag inventory.</summary>
        private void LogWarning(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.LogWarning($"[OreBagInventory] {message}", this);
        }
    }
}
