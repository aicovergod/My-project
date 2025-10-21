using System;
using Inventory;
using UI;
using UnityEngine;

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
        private Inventory.Inventory inventory;

        /// <summary>Tracks whether the inventory UI is currently visible.</summary>
        private bool isOpen;

        /// <summary>Raised whenever the inventory window opens or closes.</summary>
        public event Action<bool> VisibilityChanged;

        /// <summary>Initialises the underlying inventory component using the player's styling as a template.</summary>
        public void Initialise()
        {
            // Try to reuse an existing inventory component so pet storage leftovers are retained.
            inventory = GetComponent<Inventory.Inventory>();

            if (inventory == null)
            {
                inventory = gameObject.AddComponent<Inventory.Inventory>();
            }

            // If we still failed to acquire an inventory component something is misconfigured, so abort.
            if (inventory == null)
            {
                Debug.LogError("CompanionInventory.Initialise failed because no Inventory component could be resolved.");
                return;
            }

            inventory.columns = 4;
            inventory.size = 28;
            inventory.useSharedUIRoot = false;
            inventory.centerOnScreen = true;
            inventory.showCloseButton = false;
            inventory.saveKey = "CompanionInventory";
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

            var playerInventory = GameObject.FindGameObjectWithTag("Player")?.GetComponent<Inventory.Inventory>();
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

            inventory.RefreshWindowLayout();
            inventory.ForceDedicatedUiRoot();
            inventory.CloseUI();
            isOpen = false;
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

        private void OnDestroy()
        {
            if (inventory != null)
                inventory.CloseUI();
            VisibilityChanged = null;
        }
    }
}
