// Assets/Scripts/Inventory/OreBag/OreBagService.cs
using System;
using BankSystem;
using Companions;
using Core.Save;
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

        [Header("Debug")]
        [SerializeField]
        [Tooltip("When enabled the ore bag service emits detailed logs for persistence, deposits, and transfers.")]
        private bool enableDebugLogging = false;

        /// <summary>
        /// Random companion chat lines broadcast after a successful ore transfer.
        /// Includes an optional player name placeholder so the companion can
        /// directly address the active account when flavour text requires it.
        /// </summary>
        private static readonly string[] CompanionDepositSuccessMessages =
        {
            "I've added the ores to your ore bag.",
            "All added.",
            "There, all added.",
            "I've added all the ores.",
            "Added the ores boss.",
            "Added the ores.",
            "There, all added {playerName}."
        };

        private OreBagInventory oreBagInventory;

        // Tracks which profile has already been evaluated so new logins can
        // re-run the bootstrap guard without repeatedly clearing the bag for
        // returning accounts that already possess persisted ore data.
        private string lastBootstrapProfileId = string.Empty;

        /// <summary>Singleton accessor. Ensures a service instance exists before returning it.</summary>
        public static OreBagService Instance => EnsureInstance();

        /// <summary>Exposes the debug logging flag so Admin tooling can toggle it at runtime.</summary>
        public static bool EnableDebugLogging
        {
            get
            {
                var resolvedInstance = EnsureInstance();
                return resolvedInstance != null && resolvedInstance.enableDebugLogging;
            }
            set
            {
                var resolvedInstance = EnsureInstance();
                if (resolvedInstance == null)
                    return;

                if (resolvedInstance.enableDebugLogging == value)
                    return;

                resolvedInstance.enableDebugLogging = value;
                resolvedInstance.ApplyDebugLoggingState(value
                    ? "External toggle enabled"
                    : "External toggle disabled");
            }
        }

        private static OreBagService EnsureInstance()
        {
            if (instance != null)
            {
                instance.Log("EnsureInstance returning cached singleton instance.");
                return instance;
            }

            instance = FindObjectOfType<OreBagService>(true);
            if (instance != null)
            {
                if (!instance.gameObject.activeInHierarchy)
                    instance.ConfigureInventoryWhileInactive();

                instance.Log("EnsureInstance located an existing inactive service in the scene.");
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
                createdInstance.ApplyDebugLoggingState("EnsureInstance initialised", false);
                createdInstance.Log("EnsureInstance created a new service instance at runtime.");
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

            // Cache whether the service needs to add its inventory companions. When either is
            // missing we temporarily deactivate the GameObject so Unity will not invoke
            // RuntimeInventory.OnEnable with the default "InventoryData" key as the components
            // are attached.
            bool requiresComponentCreation = GetComponent<RuntimeInventory>() == null ||
                                             GetComponent<OreBagInventory>() == null;
            bool reactivateGameObject = false;

            if (requiresComponentCreation && gameObject.activeSelf)
            {
                gameObject.SetActive(false);
                reactivateGameObject = true;
            }

            var runtimeInventory = GetComponent<RuntimeInventory>();
            if (runtimeInventory != null)
                runtimeInventory.enabled = false;

            base.Awake();

            runtimeInventory = ConfigureInventoryComponentsWhileInactive();

            if (reactivateGameObject)
                gameObject.SetActive(true);

            runtimeInventory.enabled = true;

            ApplyDebugLoggingState("Awake", false);

            SaveManager.ActiveAccountUsernameChanged += HandleActiveAccountUsernameChanged;

            EvaluateBootstrapForActiveAccount();
        }

        /// <summary>
        /// Ensures the ore bag inventory is present and configured while the GameObject
        /// remains inactive so the runtime inventory never enables itself with the default
        /// save key.
        /// </summary>
        private void ConfigureInventoryWhileInactive()
        {
            bool wasActive = gameObject.activeSelf;
            if (wasActive)
                gameObject.SetActive(false);

            var runtimeInventory = ConfigureInventoryComponentsWhileInactive();

            if (wasActive)
                gameObject.SetActive(true);

            runtimeInventory.enabled = true;

            Log("Configured ore bag inventory while inactive to prevent default save key usage.");
        }

        /// <summary>
        /// Ensures the ore bag inventory and runtime inventory components exist and are
        /// configured while the service is inactive so Unity cannot register them with the
        /// default save key.
        /// </summary>
        private RuntimeInventory ConfigureInventoryComponentsWhileInactive()
        {
            oreBagInventory = GetComponent<OreBagInventory>() ?? gameObject.AddComponent<OreBagInventory>();

            var runtimeInventory = oreBagInventory.InventoryComponent ??
                                    GetComponent<RuntimeInventory>() ??
                                    gameObject.AddComponent<RuntimeInventory>();

            runtimeInventory.enabled = false;

            oreBagInventory.EnsureInventoryConfigured();
            oreBagInventory.EnableDebugLogging = enableDebugLogging;

            return runtimeInventory;
        }

        /// <summary>
        /// Reacts to the active account changing so the ore bag is only cleared for
        /// fresh profiles that have never saved dedicated ore bag data.
        /// </summary>
        /// <param name="username">Username that became active. Empty when unbinding.</param>
        private void HandleActiveAccountUsernameChanged(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                // Reset the evaluation cache so a subsequent login re-runs the guard.
                lastBootstrapProfileId = string.Empty;
                Log("Active account cleared. Reset bootstrap evaluation cache.");
                return;
            }

            Log($"Active account changed to {username}. Evaluating ore bag bootstrap state.");
            EvaluateBootstrapForActiveAccount();
        }

        /// <summary>
        /// Inspects the save payload for the currently bound profile and clears the
        /// runtime inventory only when the profile lacks a dedicated ore bag entry.
        /// Returning accounts retain their restored ore stacks.
        /// </summary>
        private void EvaluateBootstrapForActiveAccount()
        {
            var runtimeInventory = oreBagInventory?.InventoryComponent ?? GetComponent<RuntimeInventory>();

            if (runtimeInventory == null)
            {
                LogWarning("EvaluateBootstrapForActiveAccount aborted because the runtime inventory could not be resolved.");
                return;
            }

            string profileId = SaveManager.ActiveProfileId;
            if (string.IsNullOrEmpty(profileId))
            {
                LogWarning("EvaluateBootstrapForActiveAccount aborted because there is no active profile id.");
                return;
            }

            if (string.Equals(lastBootstrapProfileId, profileId, StringComparison.Ordinal))
            {
                Log($"Bootstrap already evaluated for profile {profileId}. Skipping re-run.");
                return;
            }

            lastBootstrapProfileId = profileId;

            var data = SaveManager.Load<InventoryModel.InventorySaveData>(runtimeInventory.saveKey);
            Log(data == null
                ? $"No persisted ore bag data found for profile {profileId}. Clearing runtime slots to avoid stale contents."
                : $"Persisted ore bag payload detected for profile {profileId}. Preserving restored contents.");

            if (HasPersistedOreBagPayload(data))
                return;

            runtimeInventory.ClearAllSlotsWithoutPersistence();
            Log("Runtime ore bag inventory cleared because no dedicated payload was present in the save file.");
        }

        /// <summary>
        /// Determines whether the provided save data represents an ore bag payload.
        /// </summary>
        /// <param name="data">Persisted inventory data retrieved from <see cref="SaveManager"/>.</param>
        private bool HasPersistedOreBagPayload(InventoryModel.InventorySaveData data)
        {
            if (data == null)
            {
                Log("Persisted payload missing or null.");
                return false;
            }

            if (data.slots == null || data.slots.Length == 0)
            {
                Log("Persisted payload contained no slots array.");
                return false;
            }

            Log($"Persisted payload contains {data.slots.Length} slots.");
            return true;
        }

        private void OnDestroy()
        {
            SaveManager.ActiveAccountUsernameChanged -= HandleActiveAccountUsernameChanged;

            if (instance == this)
            {
                Log("OreBagService destroyed. Clearing singleton reference.");
                instance = null;
            }
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

            Log($"Opening ore bag from slot {slotIndex} using bag definition {bagData?.name ?? "<null>"}.");
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

            Log("Depositing all player ore into the ore bag.");
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
                    Log($"Failed to add ore {entry.item.id} x{entry.count} from slot {i}. Capacity likely hit.");
                    capacityHit = true;
                    continue;
                }

                totalAdded += added;
                model.RemoveFromSlot(i, added);

                Log($"Transferred {added}/{entry.count} ores from player slot {i}.");

                if (added < entry.count)
                    capacityHit = true;
            }

            if (totalAdded > 0)
            {
                playerInventory.WindowController?.RefreshAllSlots();
                oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();

                if (showMessages)
                    PublishPlayerDepositMessage(totalAdded);

                Log($"Player ore deposit complete. Total moved: {totalAdded}. Capacity hit: {capacityHit}.");
            }

            if ((capacityHit || totalAdded == 0) && showMessages)
                PublishPlayerBagFullMessage();

            bagFull = capacityHit || totalAdded == 0;
            Log($"Deposit all result - success: {totalAdded > 0}, bag full: {bagFull}.");
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

            Log($"Attempting to deposit player slot {slotIndex} item {entry.item.id} x{entry.count}.");
            int accepted = oreBagInventory.AddOre(entry.item, entry.count);
            if (accepted <= 0)
            {
                bagFull = true;
                if (showMessages)
                    PublishPlayerBagFullMessage();
                Log("Slot deposit failed because the bag was full.");
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
                Log($"Slot deposit partially succeeded ({accepted}/{entry.count}). Bag reported as full.");
            }

            added = accepted;
            Log($"Slot deposit succeeded with {accepted} ores moved. Bag full flag: {bagFull}.");
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

            Log("Depositing companion ore into the ore bag.");
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
                    Log($"Failed to add companion ore {entry.item.id} x{entry.count} from slot {i}.");
                    capacityHit = true;
                    continue;
                }

                totalAdded += added;
                model.RemoveFromSlot(i, added);

                Log($"Transferred {added}/{entry.count} companion ores from slot {i}.");

                if (added < entry.count)
                    capacityHit = true;
            }

            if (totalAdded > 0)
            {
                PublishPlayerDepositMessage(totalAdded);
                PublishCompanionDepositSuccessMessage();
                companionInventory.WindowController?.RefreshAllSlots();
                oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();
                Log($"Companion deposit complete. Total moved: {totalAdded}.");
            }
            else
            {
                if (capacityHit)
                {
                    PublishPlayerBagFullMessage();
                }
                else
                {
                    PublishCompanionNoOreMessage();
                }

                Log("Companion deposit failed because no ores were moved.");
            }

            if (capacityHit)
                PublishCompanionBagOverflowMessage();

            Log($"Companion deposit result - success: {totalAdded > 0}, capacity hit: {capacityHit}.");
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
                Log($"Upgrade aborted. Owned {owned}/{required} fragments.");
                return false;
            }

            model.RemoveItem(fragmentItem, required);
            model.ReplaceItem(slotIndex, bagData, nextTier, 1);
            playerInventory.WindowController?.RefreshAllSlots();

            PublishPlayerMessage($"Your ore bag has been upgraded to tier {nextTier.Tier}.");
            ApplyActiveBag(nextTier, playerInventory);
            oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();
            Log($"Ore bag upgraded from tier {bagData.Tier} to tier {nextTier.Tier} using {required} fragments.");
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
                Log("Bank transfer aborted because the ore bag was empty.");
                return true;
            }

            var bagInventory = oreBagInventory.InventoryComponent;
            if (bagInventory == null)
                return false;

            totalTransferred = bank.DepositAllFromInventory(bagInventory);
            oreBagInventory.InventoryComponent.WindowController?.RefreshAllSlots();
            playerInventory.WindowController?.RefreshSlot(slotIndex);

            int remainingAfterTransfer = oreBagInventory.GetCurrentOreCount();

            Log($"Transferred {totalTransferred} ores to the bank. Remaining in bag: {remainingAfterTransfer}.");

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
            oreBagInventory.EnableDebugLogging = enableDebugLogging;
            Log(bagData == null
                ? "Cleared active bag definition while applying active bag."
                : $"Active bag set to {bagData.name} (tier {bagData.Tier}).");
        }

        private bool TryFindBag(out RuntimeInventory playerInventory, out int slotIndex, out OreBagItemData bagData)
        {
            playerInventory = CompanionManager.GetPlayerInventory();
            slotIndex = -1;
            bagData = null;

            if (playerInventory == null)
                return false;

            bool found = TryLocateBag(playerInventory, out slotIndex, out bagData);
            Log(found
                ? $"Located ore bag in player inventory slot {slotIndex} ({bagData?.name ?? "<null>"})."
                : "Player inventory does not currently contain an ore bag.");
            return found;
        }

        private bool TryResolveBagForInventory(RuntimeInventory inventory, out OreBagItemData bagData)
        {
            bool found = TryLocateBag(inventory, out _, out bagData);
            Log(found
                ? $"Resolved ore bag definition {bagData?.name ?? "<null>"} for provided inventory."
                : "Failed to resolve ore bag for provided inventory.");
            return found;
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
            Log($"Resolved ore bag from explicit slot {slotIndex}: {bagData.name} (tier {bagData.Tier}).");
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
                    Log($"TryLocateBag found bag {data.name} in slot {i}.");
                    return true;
                }
            }

            Log("TryLocateBag did not find an ore bag in the provided inventory.");
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
            Log($"Published player chat message: {text}");
        }

        /// <summary>
        /// Sends a companion chat acknowledgement when ore transfers succeed so the
        /// player receives flavourful confirmation from their follower.
        /// </summary>
        private void PublishCompanionDepositSuccessMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string companionName = CompanionManager.GetCompanionDisplayName();
            if (string.IsNullOrWhiteSpace(companionName))
                companionName = "Companion";

            string message = ResolveCompanionDepositSuccessLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(companionName, message);
            Log($"Companion deposit success message published: {message}");
        }

        /// <summary>
        /// Resolves the chat line used after a successful companion ore transfer.
        /// Applies the active player's name to any placeholders when available.
        /// </summary>
        private string ResolveCompanionDepositSuccessLine()
        {
            if (CompanionDepositSuccessMessages == null || CompanionDepositSuccessMessages.Length == 0)
                return "I've added the ores to your ore bag.";

            int index = UnityEngine.Random.Range(0, CompanionDepositSuccessMessages.Length);
            string template = CompanionDepositSuccessMessages[index] ?? string.Empty;

            string playerName = ResolveActivePlayerName();
            string safePlayerName = string.IsNullOrWhiteSpace(playerName) ? "friend" : playerName.Trim();

            return template.Replace("{playerName}", safePlayerName);
        }

        /// <summary>
        /// Publishes a corrected game message when the companion has no ore to share.
        /// Keeps the messaging accurate instead of implying the bag is already full.
        /// </summary>
        private void PublishCompanionNoOreMessage()
        {
            string companionName = CompanionManager.GetCompanionDisplayName();
            if (string.IsNullOrWhiteSpace(companionName))
                companionName = "Your companion";

            PublishPlayerMessage($"{companionName} doesn't have anything to add to your ore bag.");
        }

        /// <summary>
        /// Pulls the active player's username from the chat service so the companion
        /// can reference them directly when flavour text allows.
        /// </summary>
        private static string ResolveActivePlayerName()
        {
            var chat = ChatService.Instance;
            return chat != null ? chat.ActiveUsername : string.Empty;
        }

        private void PublishCompanionBagOverflowMessage()
        {
            var chat = ChatService.Instance;
            if (chat != null)
            {
                string speaker = CompanionManager.GetCompanionDisplayName();
                chat.PublishCompanionMessage(speaker, "There Isn't enough room in the ore bag.");
            }
            Log("Companion bag overflow message published.");
        }

        private void ApplyDebugLoggingState(string reason, bool logStateChange = true)
        {
            if (oreBagInventory != null)
                oreBagInventory.EnableDebugLogging = enableDebugLogging;

            if (!logStateChange)
                return;

            Debug.Log($"[OreBagService] Debug logging {(enableDebugLogging ? "enabled" : "disabled")} ({reason}).", this);
        }

        /// <summary>Writes an always-on debug log message for ore bag service flows.</summary>
        private void Log(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[OreBagService] {message}", this);
        }

        /// <summary>Writes an always-on warning log message for ore bag service flows.</summary>
        private void LogWarning(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.LogWarning($"[OreBagService] {message}", this);
        }
    }
}
