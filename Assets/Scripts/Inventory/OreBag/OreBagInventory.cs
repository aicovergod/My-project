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

        private RuntimeInventory inventory;
        private InventoryModel model;
        private OreBagItemData activeBagDefinition;
        private int activeCapacity;
        private bool inventoryChangeSubscribed;
        private bool sanitizedAfterInitialRestore;

        /// <summary>Expose the underlying inventory component for UI refreshes.</summary>
        public RuntimeInventory InventoryComponent => inventory;

        /// <summary>Combined ore capacity enforced for the currently active bag tier.</summary>
        public int CurrentCapacity => activeCapacity;

        private void Awake()
        {
            EnsureInventoryConfigured();
        }

        /// <summary>
        /// Ensures the runtime inventory component is initialised with the ore bag layout
        /// and filtering rules before the component participates in save/load.
        /// </summary>
        public void EnsureInventoryConfigured()
        {
            if (inventory == null)
                inventory = GetComponent<RuntimeInventory>();

            if (inventory == null)
                return;

            ConfigureInventoryWindow();

            model = inventory.Model;
            model.CanStoreRule = CanStoreOreOnly;

            if (!inventoryChangeSubscribed && inventory != null)
            {
                inventory.OnInventoryChanged += HandleInventoryChanged;
                inventoryChangeSubscribed = true;
            }
        }

        /// <summary>
        /// Scrubs any non-ore data that may have been restored into the bag before the
        /// dedicated save migrated. Valid ore entries are preserved in their original
        /// slots so the player retains stack positions.
        /// </summary>
        public void SanitizeLoadedContents()
        {
            EnsureInventoryConfigured();

            if (inventory == null || model == null)
                return;

            // Cache valid ore entries so their original slot positions can be restored after scrubbing.
            var validEntries = new List<(int slotIndex, InventoryEntry entry)>();
            bool scrubbed = false;

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);

                if (entry.item == null)
                {
                    if (entry.count != 0)
                        scrubbed = true;
                    continue;
                }

                if (!IsOre(entry.item) || entry.count <= 0)
                {
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
                return;

            inventory.RunWithoutPersistence(targetModel =>
            {
                // Clear everything so only verified ore stacks return to the bag.
                targetModel.ClearAllSlots();
                foreach (var (slotIndex, entry) in validEntries)
                    targetModel.SetEntry(slotIndex, entry);
            });

            inventory.Save();
        }

        /// <summary>Copies fonts/visual settings from the player inventory for consistent styling.</summary>
        public void SyncStylingFrom(RuntimeInventory source)
        {
            if (source == null || inventory == null || source == inventory)
                return;

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

            return total;
        }

        /// <summary>Remaining combined capacity before the bag reaches its tier limit.</summary>
        public int GetCapacityRemaining()
        {
            return Mathf.Max(0, activeCapacity - GetCurrentOreCount());
        }

        /// <summary>
        /// Attempts to add up to <paramref name="quantity"/> ores, respecting tier capacity and slot availability.
        /// Returns the number of ores successfully stored. The ore bag treats every ore as stackable regardless
        /// of the underlying <see cref="ItemData"/> configuration so identical ores always occupy a single slot.
        /// </summary>
        public int AddOre(ItemData item, int quantity)
        {
            if (!IsOre(item) || quantity <= 0)
                return 0;

            int remainingCapacity = GetCapacityRemaining();
            if (remainingCapacity <= 0)
                return 0;

            int toAdd = Mathf.Min(quantity, remainingCapacity);
            int added = 0;

            if (model == null)
                EnsureInventoryConfigured();

            if (model == null)
                return 0;

            // Merge any pre-existing stacks for this ore so the bag can reclaim slots that were previously
            // fragmented by per-item entries (the behaviour reported by the user).
            MergeExistingStacksForOre(item);

            // Try to top up an existing stack first so the bag retains a single stack per ore type.
            int primaryIndex = FindFirstSlotWithItem(item);
            if (primaryIndex != -1)
            {
                var entry = model.GetEntry(primaryIndex);
                int addToExisting = Mathf.Min(toAdd, Mathf.Max(0, int.MaxValue - entry.count));
                if (addToExisting > 0)
                {
                    entry.count += addToExisting;
                    if (model.SetEntry(primaryIndex, entry))
                    {
                        added += addToExisting;
                        toAdd -= addToExisting;
                    }
                }
            }

            // If there is still ore remaining to be stored, allocate a fresh slot.
            if (toAdd > 0)
            {
                int emptyIndex = FindFirstEmptySlot();
                if (emptyIndex != -1)
                {
                    var entry = new InventoryEntry
                    {
                        item = item,
                        count = toAdd
                    };

                    if (model.SetEntry(emptyIndex, entry))
                    {
                        added += toAdd;
                        toAdd = 0;
                    }
                }
            }

            if (added > 0)
                inventory.WindowController?.RefreshAllSlots();

            return added;
        }

        /// <summary>Opens the ore bag UI window.</summary>
        public void OpenWindow()
        {
            inventory.OpenUI();
        }

        /// <summary>Closes the ore bag UI window.</summary>
        public void CloseWindow()
        {
            inventory.CloseUI();
        }

        /// <summary>Returns true when the supplied item is recognised as an ore.</summary>
        public bool IsOre(ItemData item)
        {
            if (item == null)
                return false;

            // Prevent the ore bag item itself from being considered an ore so players cannot
            // accidentally stash the bag inside its own storage and lose access to it.
            if (item is OreBagItemData)
                return false;

            EnsureOreItemIds();
            return oreItemIds.Contains(item.id);
        }

        private void ConfigureInventoryWindow()
        {
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

            sanitizedAfterInitialRestore = true;
            SanitizeLoadedContents();
        }

        private static void EnsureOreItemIds()
        {
            if (oreItemIds != null)
                return;

            oreItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var definitions = Resources.LoadAll<OreDefinition>("MiningDatabase");
            foreach (var def in definitions)
            {
                if (def != null && !string.IsNullOrEmpty(def.Id))
                    oreItemIds.Add(def.Id);
            }

            if (oreItemIds.Count > 0)
                return;

            var fallbackItems = Resources.LoadAll<ItemData>("Item");
            foreach (var candidate in fallbackItems)
            {
                if (candidate == null || string.IsNullOrEmpty(candidate.id))
                    continue;

                if (candidate.itemName.IndexOf("Ore", StringComparison.OrdinalIgnoreCase) >= 0)
                    oreItemIds.Add(candidate.id);
            }
        }

        /// <summary>
        /// Collapses any duplicate stacks for the supplied ore so the bag keeps a single stack per ore type.
        /// </summary>
        private void MergeExistingStacksForOre(ItemData ore)
        {
            if (model == null || ore == null)
                return;

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
                        model.SetEntry(i, default);
                        continue;
                    }

                    combinedCount += entry.count;

                    if (primaryIndex == -1)
                    {
                        primaryIndex = i;
                    }
                    else
                    {
                        duplicates ??= new List<int>();
                        duplicates.Add(i);
                    }
                }
                else if (entry.item == null && entry.count > 0)
                {
                    // Clean up invalid entries that may have been created by corrupted save data.
                    model.SetEntry(i, default);
                }
            }

            if (primaryIndex == -1)
                return;

            var primary = model.GetEntry(primaryIndex);

            if (duplicates != null)
            {
                foreach (int duplicateIndex in duplicates)
                    model.SetEntry(duplicateIndex, default);
            }

            long sanitizedTotal = combinedCount < 0 ? 0 : combinedCount;
            int clampedTotal = sanitizedTotal > int.MaxValue ? int.MaxValue : (int)sanitizedTotal;
            if (primary.count != clampedTotal)
            {
                primary.count = clampedTotal;
                model.SetEntry(primaryIndex, primary);
            }
        }

        /// <summary>
        /// Finds the first slot containing <paramref name="item"/>. Returns -1 when no matching slot exists.
        /// </summary>
        private int FindFirstSlotWithItem(ItemData item)
        {
            if (model == null || item == null)
                return -1;

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item == item && entry.count > 0)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Returns the index of the first empty slot that can be reused for ore storage.
        /// </summary>
        private int FindFirstEmptySlot()
        {
            if (model == null)
                return -1;

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item == null || entry.count <= 0)
                    return i;
            }

            return -1;
        }

        private void OnDisable()
        {
            sanitizedAfterInitialRestore = false;
        }

        private void OnDestroy()
        {
            if (inventory != null && inventoryChangeSubscribed)
            {
                inventory.OnInventoryChanged -= HandleInventoryChanged;
                inventoryChangeSubscribed = false;
            }
        }
    }
}
