/// Feature: Added stack-based add helper for companion pickup commands.
using System;
using Core.Save;
using Inventory;
using Inventory.Core;
using UI;
using UnityEngine;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions
{
    /// <summary>
    /// Configures an OSRS-style inventory for the companion so it mirrors the player's 28-slot backpack.
    /// Handles window presentation, font wiring, and visibility toggles without relying on inspector setup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionInventory : MonoBehaviour
    {
        /// <summary>Inventory component that renders the companion's backpack.</summary>
        private RuntimeInventory inventory;

        /// <summary>Tracks whether the inventory UI is currently visible.</summary>
        private bool isOpen;

        /// <summary>Indicates whether the inventory load must wait for an active profile.</summary>
        private bool deferredLoadPending;

        /// <summary>Raised whenever the inventory window opens or closes.</summary>
        public event Action<bool> VisibilityChanged;

        /// <summary>Exposes the underlying inventory component for other companion systems.</summary>
        public RuntimeInventory InventoryComponent => inventory;

        /// <summary>Initialises the underlying inventory component using the player's styling as a template.</summary>
        public void Initialise()
        {
            // Try to reuse an existing inventory component so pet storage leftovers are retained.
            inventory = GetComponent<RuntimeInventory>();

            if (inventory == null)
            {
                inventory = gameObject.AddComponent<RuntimeInventory>();
            }

            // If we still failed to acquire an inventory component something is misconfigured, so abort.
            if (inventory == null)
            {
                Debug.LogError("CompanionInventory.Initialise failed because no Inventory component could be resolved.");
                return;
            }

            // The inventory registers with the save manager during OnEnable. Remove it so we can
            // normalise the save key and contents without writing to the player profile slot.
            SaveManager.Unregister(inventory);

            inventory.saveKey = "CompanionInventory";
            inventory.columns = 4;
            inventory.size = 28;
            inventory.useSharedUIRoot = false;
            inventory.centerOnScreen = true;
            inventory.showCloseButton = true;
            inventory.slotSize = new Vector2(64f, 64f);
            inventory.slotSpacing = new Vector2(4f, 4f);
            inventory.windowPadding = new Vector2(6f, 6f);
            inventory.windowSize = new Vector2(260f, 430f);
            inventory.referenceResolution = new Vector2(1024f, 768f);
            inventory.windowColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            inventory.emptySlotColor = Color.black;
            inventory.stackCountFont = LegacyFontProvider.GetLegacyFont();
            inventory.tooltipNameFont = LegacyFontProvider.GetLegacyFont();
            inventory.tooltipDescriptionFont = LegacyFontProvider.GetLegacyFont();

            var playerInventory = GameObject.FindGameObjectWithTag("Player")?.GetComponent<RuntimeInventory>();
            if (playerInventory != null && playerInventory != inventory)
            {
                inventory.windowColor = playerInventory.windowColor;
                inventory.slotSize = playerInventory.slotSize;
                inventory.slotSpacing = playerInventory.slotSpacing;
                inventory.windowPadding = playerInventory.windowPadding;
                inventory.windowSize = playerInventory.windowSize;
                inventory.referenceResolution = playerInventory.referenceResolution;
                inventory.stackCountFont = playerInventory.stackCountFont ?? LegacyFontProvider.GetLegacyFont();
                inventory.tooltipNameFont = playerInventory.tooltipNameFont ?? LegacyFontProvider.GetLegacyFont();
                inventory.tooltipDescriptionFont = playerInventory.tooltipDescriptionFont ?? LegacyFontProvider.GetLegacyFont();
                inventory.combinationDatabase = playerInventory.combinationDatabase;
            }

            // Ensure any residual items from default registration are cleared before we reload with the
            // companion-specific save key. This prevents wiping existing pet data with empty payloads.
            ClearPreloadedContents();

            inventory.RefreshWindowLayout();
            inventory.ForceDedicatedUiRoot();

            // Defer loading if no account is active so we bind to the correct profile-scoped key.
            if (string.IsNullOrEmpty(SaveManager.ActiveProfileId))
            {
                if (!deferredLoadPending)
                {
                    SaveManager.ActiveAccountUsernameChanged += HandleActiveAccountUsernameChanged;
                }

                deferredLoadPending = true;
            }
            else
            {
                LoadAndRegisterInventory();
            }
        }

        /// <summary>
        /// Performs the companion inventory load, re-registers it with the save system, and
        /// ensures the UI reflects the refreshed contents.
        /// </summary>
        private void LoadAndRegisterInventory()
        {
            if (inventory == null)
                return;

            inventory.Load();
            SaveManager.Register(inventory);

            inventory.RefreshWindowLayout();
            inventory.CloseUI();
            isOpen = false;
        }

        /// <summary>
        /// Clears any items that may have been loaded with the default inventory key so the companion
        /// starts with a clean state before reloading the dedicated save slot.
        /// </summary>
        private void ClearPreloadedContents()
        {
            if (inventory == null)
                return;

            var model = inventory.Model;
            int slotCount = Mathf.Max(0, model.Size);
            var emptyData = new InventoryModel.InventorySaveData
            {
                slots = new InventoryModel.SlotData[slotCount]
            };

            model.RestoreState(emptyData);
        }

        /// <summary>
        /// Toggles the inventory window and raises the visibility event so menus can refresh.
        /// </summary>
        public bool ToggleInventory()
        {
            if (inventory == null)
                return false;

            if (isOpen)
            {
                inventory.CloseUI();
                isOpen = false;
            }
            else
            {
                inventory.OpenUI();
                isOpen = true;
            }

            VisibilityChanged?.Invoke(isOpen);
            return isOpen;
        }

        /// <summary>
        /// Synchronises the cached visibility flag with the inventory window so external listeners
        /// are notified when the UI is closed through the new close button or other indirect paths.
        /// </summary>
        private void Update()
        {
            if (inventory == null)
                return;

            bool currentlyOpen = inventory.IsOpen;
            if (currentlyOpen == isOpen)
                return;

            isOpen = currentlyOpen;
            VisibilityChanged?.Invoke(isOpen);
        }

        /// <summary>
        /// Forces the inventory closed without toggling so guard-mode transitions remain deterministic.
        /// </summary>
        public void ForceClosed()
        {
            if (inventory == null)
                return;

            inventory.CloseUI();
            if (isOpen)
            {
                isOpen = false;
                VisibilityChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// Attempts to add the provided stack to the companion inventory.
        /// </summary>
        /// <param name="stack">Item stack that should be inserted.</param>
        /// <returns>True when the stack was successfully added.</returns>
        public bool TryAddItem(ItemStack stack)
        {
            if (!stack.IsValid || inventory == null)
                return false;

            if (!inventory.CanAddItem(stack.Item, stack.Quantity))
                return false;

            return inventory.AddItem(stack.Item, stack.Quantity);
        }

        private void OnDestroy()
        {
            if (deferredLoadPending)
            {
                SaveManager.ActiveAccountUsernameChanged -= HandleActiveAccountUsernameChanged;
                deferredLoadPending = false;
            }

            if (inventory != null)
                inventory.CloseUI();
            VisibilityChanged = null;
        }

        /// <summary>
        /// Handles the save manager activating a profile so the companion inventory can bind to it.
        /// </summary>
        /// <param name="_">Unused username argument supplied by the save manager.</param>
        private void HandleActiveAccountUsernameChanged(string _)
        {
            if (!deferredLoadPending)
                return;

            // Wait for the profile ID to bind before mutating subscription state so another
            // notification can retry if the account is still initialising.
            if (string.IsNullOrEmpty(SaveManager.ActiveProfileId))
                return;

            SaveManager.ActiveAccountUsernameChanged -= HandleActiveAccountUsernameChanged;
            deferredLoadPending = false;

            LoadAndRegisterInventory();
        }
    }
}
