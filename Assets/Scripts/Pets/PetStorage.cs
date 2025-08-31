using UnityEngine;
using Inventory;
using System.Collections;
using System;
using System.Collections.Generic;
using Skills.Fishing;
using Skills.Woodcutting;
using Skills.Mining;

namespace Pets
{
    /// <summary>
    /// Provides per-pet storage using the project's inventory system.
    /// </summary>
    [DisallowMultipleComponent]
    public class PetStorage : MonoBehaviour
    {
        public PetDefinition definition;
        private Inventory.Inventory inventory;
        private PetExperience experience;

        public void Initialize(PetDefinition def)
        {
            definition = def;
            experience = GetComponent<PetExperience>();
            if (inventory == null && definition != null && definition.hasInventory)
            {
                CreateInventory();
                if (experience != null)
                    experience.OnLevelChanged += HandleLevelChanged;
            }
        }

        private void OnEnable()
        {
            if (inventory == null && definition != null)
            {
                experience = GetComponent<PetExperience>();
                if (definition.hasInventory)
                {
                    CreateInventory();
                    if (experience != null)
                        experience.OnLevelChanged += HandleLevelChanged;
                }
            }
        }

        private void Start()
        {
            var playerInv = GameObject.FindGameObjectWithTag("Player")?.GetComponent<Inventory.Inventory>();
            if (playerInv != null && playerInv.IsOpen && !playerInv.BankOpen)
                StartCoroutine(OpenDelayed());
        }

        private void OnDestroy()
        {
            if (experience != null)
                experience.OnLevelChanged -= HandleLevelChanged;
            if (inventory != null)
            {
                inventory.OnInventoryChanged -= inventory.Save;
                inventory.Save();
            }
        }

        private void CreateInventory()
        {
            inventory = gameObject.AddComponent<Inventory.Inventory>();
            inventory.useSharedUIRoot = false;
            inventory.columns = 4;
            inventory.showCloseButton = false;
            inventory.emptySlotColor = Color.clear;
            inventory.centerOnScreen = true;
            inventory.size = GetSlotsForLevel(experience != null ? experience.Level : 1);
            inventory.saveKey = $"PetInv_{definition?.id}";
            inventory.OnInventoryChanged += inventory.Save;
            var inventories = FindObjectsOfType<Inventory.Inventory>();
            foreach (var inv in inventories)
            {
                if (inv.gameObject != gameObject)
                {
                    inventory.windowColor = inv.windowColor;
                    break;
                }
            }
        }

        private void HandleLevelChanged(int lvl)
        {
            if (inventory == null)
                return;
            int newSize = GetSlotsForLevel(lvl);
            if (newSize != inventory.size)
            {
                // Remember if the inventory UI was open so we can restore it.
                bool reopen = inventory.IsOpen;

                inventory.Save();
                // Close and deactivate the existing UI so it doesn't remain
                // on screen or intercept clicks after the component is destroyed.
                inventory.CloseUI();
                Destroy(inventory);
                CreateInventory();

                // Reopen the UI if it was visible before the rebuild so players
                // immediately see the updated slot count.
                if (reopen)
                    inventory.OpenUI();
            }
        }

        private int GetSlotsForLevel(int level)
        {
            if (level >= 99) return 20;
            if (level >= 75) return 16;
            if (level >= 50) return 12;
            if (level >= 25) return 8;
            return 4;
        }

        /// <summary>
        /// Attempts to add items to the pet's storage inventory.
        /// </summary>
        /// <param name="item">Item definition to store.</param>
        /// <param name="amount">Quantity of the item.</param>
        /// <returns>True if the items were stored successfully.</returns>
        public bool StoreItem(ItemData item, int amount = 1)
        {
            if (inventory == null || item == null)
                return false;
            if (!CanStore(item))
                return false;
            return inventory.AddItem(item, amount);
        }

        private static HashSet<string> fishItemIds;
        private static HashSet<string> logItemIds;
        private static HashSet<string> oreItemIds;

        private static void EnsureFishItemIds()
        {
            if (fishItemIds != null)
                return;
            fishItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var defs = Resources.LoadAll<FishDefinition>("FishingDatabase");
            foreach (var def in defs)
            {
                if (def != null && !string.IsNullOrEmpty(def.ItemId))
                    fishItemIds.Add(def.ItemId);
            }
        }

        private static void EnsureLogItemIds()
        {
            if (logItemIds != null)
                return;
            logItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var defs = Resources.LoadAll<TreeDefinition>("WoodcuttingDatabase");
            foreach (var def in defs)
            {
                if (def != null && !string.IsNullOrEmpty(def.LogItemId))
                    logItemIds.Add(def.LogItemId);
            }
            if (logItemIds.Count == 0)
            {
                var items = Resources.LoadAll<ItemData>("Item");
                foreach (var i in items)
                {
                    if (i != null && i.itemName.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0)
                        logItemIds.Add(i.id);
                }
            }
        }

        private static void EnsureOreItemIds()
        {
            if (oreItemIds != null)
                return;
            oreItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var defs = Resources.LoadAll<OreDefinition>("MiningDatabase");
            foreach (var def in defs)
            {
                if (def != null && !string.IsNullOrEmpty(def.Id))
                    oreItemIds.Add(def.Id);
            }
            if (oreItemIds.Count == 0)
            {
                var items = Resources.LoadAll<ItemData>("Item");
                foreach (var i in items)
                {
                    if (i != null && i.itemName.IndexOf("Ore", StringComparison.OrdinalIgnoreCase) >= 0)
                        oreItemIds.Add(i.id);
                }
            }
        }

        private bool CanStore(ItemData item)
        {
            if (definition == null || item == null)
                return false;
            switch (definition.id)
            {
                case "Beaver":
                    EnsureLogItemIds();
                    return logItemIds.Contains(item.id);
                case "Rock Golem":
                    EnsureOreItemIds();
                    return oreItemIds.Contains(item.id);
                case "Heron":
                    EnsureFishItemIds();
                    return fishItemIds.Contains(item.id);
                default:
                    return false;
            }
        }

        public void Open()
        {
            if (inventory != null)
                inventory.OpenUI();
        }

        public System.Collections.IEnumerator OpenDelayed()
        {
            yield return null;
            Open();
        }

        public void Close()
        {
            if (inventory != null)
                inventory.CloseUI();
        }
    }
}
