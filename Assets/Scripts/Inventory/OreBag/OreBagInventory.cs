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

        /// <summary>Expose the underlying inventory component for UI refreshes.</summary>
        public RuntimeInventory InventoryComponent => inventory;

        /// <summary>Combined ore capacity enforced for the currently active bag tier.</summary>
        public int CurrentCapacity => activeCapacity;

        private void Awake()
        {
            inventory = GetComponent<RuntimeInventory>();
            ConfigureInventoryWindow();

            model = inventory.Model;
            model.CanStoreRule = CanStoreOreOnly;
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
        /// Returns the number of ores successfully stored.
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

            if (item.stackable)
            {
                int attempt = toAdd;
                while (attempt > 0)
                {
                    if (inventory.AddItem(item, attempt))
                    {
                        added += attempt;
                        break;
                    }

                    attempt--;
                }
            }
            else
            {
                int remaining = toAdd;
                while (remaining > 0)
                {
                    if (!inventory.AddItem(item, 1))
                        break;

                    added++;
                    remaining--;
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
    }
}
