// Assets/Scripts/Inventory/OreBag/OreBagService.cs
using System;
using BankSystem;
using Companions;
using Inventory;
using Inventory.Core;
using UI.Chat;
using UnityEngine;
using World;
using RuntimeInventory = global::Inventory.Inventory;

namespace Inventory.OreBag
{
    /// <summary>
    /// Scene-persistent coordinator that exposes ore bag operations (open, deposit, upgrade)
    /// to inventories, HUD menus, and companion flows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OreBagService : ScenePersistentObject
    {
        private static OreBagService instance;

        [SerializeField]
        [Tooltip("Item id used when consuming fragments to upgrade the ore bag.")]
        private string hadesFragmentItemId = "HadesFragment";

        private OreBagInventory oreBagInventory;

        /// <summary>Singleton accessor. Ensures a service instance exists before returning it.</summary>
        public static OreBagService Instance => EnsureInstance();

        private static OreBagService EnsureInstance()
        {
            if (instance != null)
                return instance;

            instance = FindObjectOfType<OreBagService>(true);
            if (instance != null)
            {
                if (!instance.gameObject.activeInHierarchy)
                    instance.ConfigureInventoryWhileInactive();

                return instance;
            }

            var go = new GameObject(nameof(OreBagService));
            go.SetActive(false);

            var createdInstance = go.AddComponent<OreBagService>();

            if (createdInstance != null)
            {
                instance = createdInstance;
                createdInstance.ConfigureInventoryWhileInactive();
                go.SetActive(true);
            }

            return instance;
        }

        protected override void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            var runtimeInventory = GetComponent<RuntimeInventory>();

            if (runtimeInventory != null)
                runtimeInventory.enabled = false;

            base.Awake();

            oreBagInventory = GetComponent<OreBagInventory>();
            if (oreBagInventory == null)
                oreBagInventory = gameObject.AddComponent<OreBagInventory>();

            runtimeInventory = oreBagInventory?.InventoryComponent ?? GetComponent<RuntimeInventory>();

            if (runtimeInventory != null)
            {
                runtimeInventory.enabled = false;

                oreBagInventory?.EnsureInventoryConfigured();

                runtimeInventory.ClearAllSlotsWithoutPersistence();

                runtimeInventory.enabled = true;
            }
            else
            {
                oreBagInventory?.EnsureInventoryConfigured();
            }
        }

        /// <summary>
        /// Ensures the ore bag inventory is present and configured while the GameObject
        /// remains inactive so the runtime inventory never enables itself with the default
        /// save key.
        /// </summary>
        private void ConfigureInventoryWhileInactive()
        {
            if (oreBagInventory == null)
                oreBagInventory = GetComponent<OreBagInventory>();

            if (oreBagInventory == null)
                oreBagInventory = gameObject.AddComponent<OreBagInventory>();

            var runtimeInventory = oreBagInventory.InventoryComponent ?? GetComponent<RuntimeInventory>();

            if (runtimeInventory != null)
                runtimeInventory.enabled = false;

            oreBagInventory.EnsureInventoryConfigured();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>Returns true when the player currently has any ore bag in their inventory.</summary>
        public bool HasBagInInventory()
        {
            return TryFindBag(out _, out _, out _);
        }

        /// <summary>Returns true if the supplied item is recognised as an ore.</summary>
        public bool IsOre(ItemData item)
        {
            return oreBagInventory != null && oreBagInventory.IsOre(item);
        }

        /// <summary>
        /// Opens the ore bag window when the supplied slot contains a valid bag.
        /// </summary>
        public bool TryOpenBagFromSlot(RuntimeInventory playerInventory, int slotIndex)
        {
            if (oreBagInventory == null || playerInventory == null)
                return false;

            if (!TryResolveBagFromSlot(playerInventory, slotIndex, out var bagData))
                return false;

            ApplyActiveBag(bagData, playerInventory);
            oreBagInventory.OpenWindow();
            oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();
            return true;
        }

        /// <summary>
        /// Deposits every ore stack in the player inventory into the bag.
        /// </summary>
        public bool TryDepositAllPlayerOre(RuntimeInventory playerInventory, bool showMessages, out int totalAdded, out bool bagFull)
        {
            totalAdded = 0;
            bagFull = false;

            if (oreBagInventory == null || playerInventory == null)
                return false;

            if (!TryResolveBagForInventory(playerInventory, out var bagData))
                return false;

            ApplyActiveBag(bagData, playerInventory);

            var model = playerInventory.Model;
            bool capacityHit = false;

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                // Skip empty slots, the ore bag item itself, and anything that is not a valid ore stack.
                if (entry.item == null || entry.item is OreBagItemData || !oreBagInventory.IsOre(entry.item) || entry.count <= 0)
                    continue;

                int added = oreBagInventory.AddOre(entry.item, entry.count);
                if (added <= 0)
                {
                    capacityHit = true;
                    continue;
                }

                totalAdded += added;
                model.RemoveFromSlot(i, added);

                if (added < entry.count)
                    capacityHit = true;
            }

            if (totalAdded > 0)
            {
                playerInventory.WindowController?.RefreshAllSlots();
                oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();

                if (showMessages)
                    PublishPlayerDepositMessage(totalAdded);
            }

            if ((capacityHit || totalAdded == 0) && showMessages)
                PublishPlayerBagFullMessage();

            bagFull = capacityHit || totalAdded == 0;
            return totalAdded > 0;
        }

        /// <summary>
        /// Deposits a single player inventory slot (used by drag-and-drop flows).
        /// </summary>
        public bool TryDepositSlotFromPlayer(RuntimeInventory playerInventory, int slotIndex, bool showMessages, out int added, out bool bagFull)
        {
            added = 0;
            bagFull = false;

            if (oreBagInventory == null || playerInventory == null)
                return false;

            var model = playerInventory.Model;
            if (slotIndex < 0 || slotIndex >= model.Size)
                return false;

            if (!TryResolveBagForInventory(playerInventory, out var bagData))
                return false;

            var entry = model.GetEntry(slotIndex);
            // Ignore empty slots, the ore bag item, and anything that fails the ore validation.
            if (entry.item == null || entry.item is OreBagItemData || !oreBagInventory.IsOre(entry.item) || entry.count <= 0)
                return false;

            ApplyActiveBag(bagData, playerInventory);

            int accepted = oreBagInventory.AddOre(entry.item, entry.count);
            if (accepted <= 0)
            {
                bagFull = true;
                if (showMessages)
                    PublishPlayerBagFullMessage();
                return false;
            }

            model.RemoveFromSlot(slotIndex, accepted);
            playerInventory.WindowController?.RefreshSlot(slotIndex);
            oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();

            if (showMessages)
                PublishPlayerDepositMessage(accepted);

            if (accepted < entry.count)
            {
                bagFull = true;
                if (showMessages)
                    PublishPlayerBagFullMessage();
            }

            added = accepted;
            return true;
        }

        /// <summary>
        /// Transfers every ore in the companion inventory into the bag, used by the HUD button.
        /// </summary>
        public bool TryDepositCompanionOre(out int totalAdded)
        {
            totalAdded = 0;

            if (oreBagInventory == null)
                return false;

            if (!TryFindBag(out var playerInventory, out _, out var bagData))
                return false;

            var companionWrapper = CompanionManager.CompanionInventory;
            var companionInventory = companionWrapper?.InventoryComponent;
            if (companionInventory == null)
                return false;

            ApplyActiveBag(bagData, playerInventory);

            var model = companionInventory.Model;
            bool capacityHit = false;

            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item == null || !oreBagInventory.IsOre(entry.item) || entry.count <= 0)
                    continue;

                int added = oreBagInventory.AddOre(entry.item, entry.count);
                if (added <= 0)
                {
                    capacityHit = true;
                    continue;
                }

                totalAdded += added;
                model.RemoveFromSlot(i, added);

                if (added < entry.count)
                    capacityHit = true;
            }

            if (totalAdded > 0)
            {
                PublishPlayerDepositMessage(totalAdded);
                companionInventory.WindowController?.RefreshAllSlots();
                oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();
            }
            else
            {
                PublishPlayerBagFullMessage();
            }

            if (capacityHit)
                PublishCompanionBagOverflowMessage();

            return totalAdded > 0;
        }

        /// <summary>
        /// Attempts to upgrade the bag located at <paramref name="slotIndex"/> using Hades fragments.
        /// </summary>
        public bool TryUpgradeBag(RuntimeInventory playerInventory, int slotIndex)
        {
            if (oreBagInventory == null || playerInventory == null)
                return false;

            var model = playerInventory.Model;
            if (slotIndex < 0 || slotIndex >= model.Size)
                return false;

            var entry = model.GetEntry(slotIndex);
            if (entry.item is not OreBagItemData bagData)
                return false;

            var nextTier = bagData.UpgradeTarget;
            if (nextTier == null)
                return false;

            var fragmentItem = ItemDatabase.GetItem(hadesFragmentItemId);
            if (fragmentItem == null)
            {
                PublishPlayerMessage($"Missing item definition for \"{hadesFragmentItemId}\".");
                return false;
            }

            int required = Math.Max(0, bagData.UpgradeCost);
            int owned = model.GetItemCount(fragmentItem);
            if (owned < required)
            {
                PublishPlayerMessage($"You need {required} Hades fragments to upgrade your ore bag.");
                return false;
            }

            model.RemoveItem(fragmentItem, required);
            model.ReplaceItem(slotIndex, bagData, nextTier, 1);
            playerInventory.WindowController?.RefreshAllSlots();

            PublishPlayerMessage($"Your ore bag has been upgraded to tier {nextTier.Tier}.");
            ApplyActiveBag(nextTier, playerInventory);
            oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();
            return true;
        }

        /// <summary>
        /// Transfers the entire contents of the active ore bag into the supplied bank instance.
        /// </summary>
        /// <param name="playerInventory">Inventory that owns the ore bag item.</param>
        /// <param name="slotIndex">Inventory slot index that was right-clicked.</param>
        /// <param name="bank">Bank UI that should receive the ores.</param>
        /// <param name="showMessages">Whether chat feedback should be emitted.</param>
        /// <param name="totalTransferred">Total number of ores moved into the bank.</param>
        /// <returns>True when the transfer attempt completed (even if nothing moved).</returns>
        public bool TryTransferAllOreToBank(
            RuntimeInventory playerInventory,
            int slotIndex,
            BankUI bank,
            bool showMessages,
            out int totalTransferred)
        {
            totalTransferred = 0;

            if (oreBagInventory == null || playerInventory == null || bank == null)
                return false;

            if (!TryResolveBagFromSlot(playerInventory, slotIndex, out var bagData) &&
                !TryResolveBagForInventory(playerInventory, out bagData))
            {
                return false;
            }

            ApplyActiveBag(bagData, playerInventory);

            int existingOre = oreBagInventory.GetCurrentOreCount();
            if (existingOre <= 0)
            {
                if (showMessages)
                    PublishPlayerMessage("Your ore bag is empty.");

                oreBagInventory.InventoryComponent?.WindowController?.RefreshAllSlots();
                playerInventory.WindowController?.RefreshSlot(slotIndex);
                return true;
            }

            var bagInventory = oreBagInventory.InventoryComponent;
            if (bagInventory == null)
                return false;

            totalTransferred = bank.DepositAllFromInventory(bagInventory);
            oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();
            playerInventory.WindowController?.RefreshSlot(slotIndex);

            int remainingAfterTransfer = oreBagInventory.GetCurrentOreCount();

            if (showMessages)
            {
                if (totalTransferred > 0 && remainingAfterTransfer <= 0)
                    PublishPlayerMessage("You have transferred all the ores to your bank.");
                else
                    PublishPlayerMessage("Your bank doesn't have enough space for more ores.");
            }

            return true;
        }

        private void ApplyActiveBag(OreBagItemData bagData, RuntimeInventory playerInventory)
        {
            oreBagInventory.ApplyBagDefinition(bagData);
            oreBagInventory.SyncStylingFrom(playerInventory);
        }

        private bool TryFindBag(out RuntimeInventory playerInventory, out int slotIndex, out OreBagItemData bagData)
        {
            playerInventory = CompanionManager.GetPlayerInventory();
            slotIndex = -1;
            bagData = null;

            if (playerInventory == null)
                return false;

            return TryLocateBag(playerInventory, out slotIndex, out bagData);
        }

        private bool TryResolveBagForInventory(RuntimeInventory inventory, out OreBagItemData bagData)
        {
            return TryLocateBag(inventory, out _, out bagData);
        }

        private bool TryResolveBagFromSlot(RuntimeInventory inventory, int slotIndex, out OreBagItemData bagData)
        {
            bagData = null;
            if (inventory == null)
                return false;

            var model = inventory.Model;
            if (slotIndex < 0 || slotIndex >= model.Size)
                return false;

            var entry = model.GetEntry(slotIndex);
            if (entry.item is not OreBagItemData data)
                return false;

            bagData = data;
            return true;
        }

        private bool TryLocateBag(RuntimeInventory inventory, out int slotIndex, out OreBagItemData bagData)
        {
            slotIndex = -1;
            bagData = null;

            var model = inventory.Model;
            for (int i = 0; i < model.Size; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item is OreBagItemData data)
                {
                    slotIndex = i;
                    bagData = data;
                    return true;
                }
            }

            return false;
        }

        private void PublishPlayerDepositMessage(int amount)
        {
            var chat = ChatService.Instance;
            if (chat != null)
                chat.PublishGameMessage($"You added {amount} ores to your ore bag.");
        }

        private void PublishPlayerBagFullMessage()
        {
            PublishPlayerMessage("My ore bag is full up");
        }

        private void PublishPlayerMessage(string text)
        {
            var chat = ChatService.Instance;
            if (chat != null)
                chat.PublishGameMessage(text);
        }

        private void PublishCompanionBagOverflowMessage()
        {
            var chat = ChatService.Instance;
            if (chat != null)
            {
                string speaker = CompanionManager.GetCompanionDisplayName();
                chat.PublishCompanionMessage(speaker, "There Isn't enough room in the ore bag.");
            }
        }
    }
}
