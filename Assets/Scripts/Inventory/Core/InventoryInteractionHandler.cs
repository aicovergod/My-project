using System;
using System.Collections.Generic;
using BankSystem;
using Books;
using Companions;
using Inventory.OreBag;
using Inventory.UI;
using InventoryComponent = global::Inventory.Inventory;
using MyGame.Drops;
using Pets;
using Player;
using Quests;
using ShopSystem;
using Skills.Common;
using Skills.Firemaking;
using UI;
using UI.Chat;
using UI.Utilities;
using Companions.Equipment;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Inventory.Core
{
    /// <summary>
    ///     Mediates user intents coming from <see cref="InventoryWindowController"/> and
    ///     coordinates gameplay side effects for the owning <see cref="InventoryComponent"/>.
    ///     The handler keeps UI and gameplay concerns separate so inventory windows remain
    ///     presentation-only while this class mutates the <see cref="InventoryModel"/> and
    ///     talks to external systems such as equipment, shops, quests, and player movement.
    /// </summary>
    public sealed class InventoryInteractionHandler : IDisposable
    {
        /// <summary>
        ///     Bundles optional service references that the handler relies on when
        ///     resolving user actions (player movement, equipment, firemaking, etc.).
        /// </summary>
        public struct ServiceContext
        {
            public Equipment Equipment;
            public PlayerMover PlayerMover;
            public FiremakingSkill FiremakingSkill;
            public QuestUI QuestUi;
            public PetStorage PetStorage;
            public GroundItemSpawner GroundItemSpawner;
        }

        private readonly InventoryComponent owner;
        private readonly InventoryModel model;
        private readonly InventoryWindowController controller;

        private Equipment equipment;
        private PlayerMover playerMover;
        private FiremakingSkill firemakingSkill;
        private QuestUI questUi;
        private PetStorage petStorage;
        private GroundItemSpawner groundItemSpawner;
        private CompanionInventory companionInventory;
        private OreBagInventory oreBagInventory;
        private CompanionEquipment companionEquipment;
        private readonly List<InventoryItemContextMenu.Option> contextMenuOptions = new();

        private bool IsCompanionOwner => companionInventory != null && companionInventory.InventoryComponent == owner;
        private bool IsOreBagOwner => oreBagInventory != null && oreBagInventory.InventoryComponent == owner;

        private Shop currentShop;

        // Tracks whether a close operation is already in progress so nested
        // calls originating from pet storage do not recurse indefinitely.
        private bool isClosing;

        private bool controllerEventsSubscribed;
        private bool questEventsSubscribed;

        /// <summary>
        ///     Creates a new handler bound to the supplied inventory model and window controller.
        /// </summary>
        public InventoryInteractionHandler(InventoryComponent owner, InventoryModel model, InventoryWindowController controller)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));

            companionInventory = owner.GetComponent<CompanionInventory>() ??
                                 owner.GetComponentInParent<CompanionInventory>();
            oreBagInventory = owner.GetComponent<OreBagInventory>() ??
                               owner.GetComponentInParent<OreBagInventory>();
            if (IsCompanionOwner)
            {
                companionEquipment = owner.GetComponent<CompanionEquipment>() ??
                                      owner.GetComponentInParent<CompanionEquipment>() ??
                                      CompanionManager.CompanionEquipment;
            }

            SubscribeToController();
        }

        /// <summary>
        ///     Current shop context. When non-null the handler routes clicks to the shop API.
        /// </summary>
        public Shop CurrentShop => currentShop;

        /// <summary>
        ///     True when the inventory is currently paired with an active shop window.
        /// </summary>
        public bool HasActiveShop => currentShop != null;

        /// <summary>
        ///     True when the player is allowed to drop items from the inventory.
        /// </summary>
        public bool CanDropItems => playerMover == null || playerMover.CanDrop;

        /// <summary>
        ///     Updates cached service references used when resolving user actions.
        /// </summary>
        public void UpdateServiceContext(ServiceContext context)
        {
            equipment = context.Equipment ?? equipment;
            playerMover = context.PlayerMover ?? playerMover;
            firemakingSkill = context.FiremakingSkill ?? firemakingSkill;
            questUi = context.QuestUi ?? questUi;
            petStorage = context.PetStorage ?? petStorage;
            groundItemSpawner = context.GroundItemSpawner ?? groundItemSpawner;

            if (companionInventory == null)
            {
                companionInventory = owner.GetComponent<CompanionInventory>() ??
                                     owner.GetComponentInParent<CompanionInventory>();
            }

            oreBagInventory = owner.GetComponent<OreBagInventory>() ??
                               owner.GetComponentInParent<OreBagInventory>();

            if (IsCompanionOwner)
            {
                if (companionEquipment == null)
                {
                    companionEquipment = owner.GetComponent<CompanionEquipment>() ??
                                         owner.GetComponentInParent<CompanionEquipment>();
                }

                if (companionEquipment == null)
                    companionEquipment = CompanionManager.CompanionEquipment;
            }

            RefreshControllerState();
        }

        /// <summary>
        ///     Registers quest UI listeners so the inventory can react to quest window state changes.
        /// </summary>
        public void OnEnable()
        {
            EnsureQuestUiReference();
            if (!questEventsSubscribed)
            {
                QuestUI.QuestUIOpened += HandleQuestUiOpened;
                QuestUI.QuestUIClosed += HandleQuestUiClosed;
                questEventsSubscribed = true;
            }
        }

        /// <summary>
        ///     Unregisters quest UI listeners when the owning inventory is disabled.
        /// </summary>
        public void OnDisable()
        {
            if (questEventsSubscribed)
            {
                QuestUI.QuestUIOpened -= HandleQuestUiOpened;
                QuestUI.QuestUIClosed -= HandleQuestUiClosed;
                questEventsSubscribed = false;
            }
        }

        /// <summary>
        ///     Unsubscribes controller listeners and quest hooks.
        /// </summary>
        public void Dispose()
        {
            UnsubscribeFromController();
            OnDisable();
        }

        /// <summary>
        ///     Pushes shop state and drop permissions to the window controller.
        /// </summary>
        public void RefreshControllerState()
        {
            if (controller == null)
                return;

            controller.IsBankOpen = owner.BankOpen;
            controller.InShop = HasActiveShop;
            controller.CanDropItems = CanDropItems;
            controller.CurrentShop = currentShop;

            if (owner.selectedIndex >= 0)
                controller.SetSelectedIndex(owner.selectedIndex);
            else
                controller.ClearHighlight();
        }

        /// <summary>
        ///     Synchronises the inventory window with current modal state (quests, shops, banks).
        ///     Call this every frame from <see cref="InventoryComponent.Update"/>.
        /// </summary>
        public void Tick()
        {
            RefreshControllerState();

            if (playerMover == null)
                return;

            EnsureQuestUiReference();

            if (questUi != null && questUi.IsOpen)
            {
                if (owner.IsOpen)
                    RequestClose();
                return;
            }

            if (HasActiveShop)
            {
                if (!owner.IsOpen)
                    RequestOpen();
                return;
            }

            if (owner.BankOpen && !owner.IsOpen)
                RequestOpen();
        }

        /// <summary>
        ///     Requests that the inventory UI open, respecting modal locks and tab mutex rules.
        /// </summary>
        public void RequestOpen()
        {
            if (!owner.BankOpen && !HasActiveShop && owner.useSharedUIRoot)
            {
                var uiManager = UIManager.Instance;
                if (uiManager != null && !uiManager.TryOpenWindow(owner))
                    return;
            }

            if (owner.useSharedUIRoot)
            {
                // Only enforce the shared tab mutex when this inventory participates in the
                // shared UI root. Dedicated inventories such as pet storage maintain their own
                // window lifecycle and should remain unaffected by tab exclusivity.
                InterfaceTabMutexUtility.CloseAllTabWindowsExcept(owner);
            }
            controller.Show();
            UpdatePetStorageVisibility();
        }

        /// <summary>
        ///     Attempts to close the inventory UI. Closing is skipped while trading or banking.
        /// </summary>
        public void RequestClose()
        {
            if (owner.BankOpen || HasActiveShop)
                return;

            if (isClosing)
                return;

            isClosing = true;

            try
            {
                controller.Hide();
                controller.DismissTooltip();
                controller.DismissContextMenus();
                ClosePetStorage();
            }
            finally
            {
                isClosing = false;
            }
        }

        /// <summary>
        ///     Clears the active slot selection and hides the highlight.
        /// </summary>
        public void ClearSelection()
        {
            owner.selectedIndex = -1;
            controller.ClearHighlight();
        }

        /// <summary>
        ///     Tries to equip the item located at <paramref name="index"/>.
        /// </summary>
        public bool EquipItem(int index)
        {
            if (equipment == null)
                return false;
            if (index < 0 || index >= model.Size)
                return false;

            var entry = model.GetEntry(index);
            if (entry.item == null || entry.item.equipmentSlot == EquipmentSlot.None)
                return false;

            model.RemoveFromSlot(index, entry.count);
            var companionResult = CompanionManager.TryEquipItemFromPlayerInventory(owner, entry);
            if (companionResult != CompanionEquipAttemptResult.NotHandled)
            {
                controller.RefreshSlot(index);
                return companionResult == CompanionEquipAttemptResult.Equipped;
            }
            if (equipment.Equip(entry))
            {
                controller.RefreshSlot(index);
                return true;
            }

            model.ReplaceItem(index, null, entry.item, entry.count);
            controller.RefreshSlot(index);
            return false;
        }

        /// <summary>
        ///     Uses the item located at <paramref name="index"/> when possible.
        /// </summary>
        public bool UseItem(int index)
        {
            if (index < 0 || index >= model.Size)
                return false;

            var entry = model.GetEntry(index);
            var item = entry.item;
            if (item == null)
                return false;

            if (item is BookItemData bookItem && bookItem.book != null)
            {
                BookUI.Instance.Open(bookItem.book);
                return true;
            }

            var eater = owner.GetComponent<PlayerEat>();
            if (eater != null && item.healAmount > 0 && eater.Eat(item))
            {
                if (!string.IsNullOrEmpty(item.replacementItemId))
                {
                    var next = ItemDatabase.GetItem(item.replacementItemId);
                    if (!model.ReplaceItem(index, item, next, next != null ? 1 : 0))
                        model.RemoveFromSlot(index, entry.count);
                }
                else
                {
                    model.RemoveFromSlot(index, 1);
                }

                ItemUseResolver.NotifyItemUsed(owner.gameObject, item, ItemUseType.Consumed);
                controller.RefreshSlot(index);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Sells up to <paramref name="quantity"/> items from <paramref name="slotIndex"/> to the active shop.
        /// </summary>
        public void SellItem(int slotIndex, int quantity)
        {
            if (owner.BankOpen || currentShop == null)
                return;
            if (slotIndex < 0 || slotIndex >= model.Size)
                return;

            int sold = 0;
            quantity = Mathf.Max(1, quantity);
            for (int i = 0; i < quantity; i++)
            {
                var item = model.GetEntry(slotIndex).item;
                if (item == null)
                    break;

                if (currentShop.Sell(item, owner, slotIndex))
                    sold++;
                else
                    break;
            }

            if (sold > 0)
            {
                controller.DismissTooltip();
                controller.RefreshSlot(slotIndex);
            }
        }

        /// <summary>
        ///     Updates the active shop context so shift-click and tooltip flows behave correctly.
        /// </summary>
        public void SetShopContext(Shop shop)
        {
            currentShop = shop;
            RefreshControllerState();

            if (shop != null)
                RequestOpen();
        }

        private void HandleDropRequested(InventoryWindowController _, InventoryWindowController.DropRequestEvent evt)
        {
            DropItem(evt.SlotIndex, evt.Quantity);
        }

        private void HandleSplitPromptRequested(InventoryWindowController _, InventoryWindowController.StackSplitPromptEvent evt)
        {
            ShowStackSplitPrompt(evt.SlotIndex, evt.SplitType);
        }

        private void HandleSlotClicked(InventoryWindowController _, InventoryWindowController.SlotClickEvent evt)
        {
            controller.DismissContextMenus();

            int index = evt.SlotIndex;
            if (index < 0 || index >= model.Size)
                return;

            if (owner.BankOpen)
            {
                if (evt.Button == PointerEventData.InputButton.Left)
                    BankUI.Instance?.DepositFromInventory(index);
                else if (evt.Button == PointerEventData.InputButton.Right)
                    BankUI.Instance?.ShowDepositMenu(index, evt.PointerPosition);
                return;
            }

            if (HasActiveShop && evt.Button == PointerEventData.InputButton.Left)
            {
                if (evt.ShiftHeld)
                    ShowStackSplitPrompt(index, StackSplitType.Sell);
                else
                    SellItem(index, 1);
                return;
            }

            if (evt.Button == PointerEventData.InputButton.Left)
            {
                HandlePrimaryClick(index);
                return;
            }

            if (evt.Button == PointerEventData.InputButton.Right)
                HandleSecondaryClick(index, evt);
        }

        private void HandleDragDropRequested(InventoryWindowController _, InventoryWindowController.DragDropEvent evt)
        {
            if (evt.Target != controller)
                return;

            var sourceController = evt.Source;
            var sourceInventory = sourceController?.Owner;
            if (sourceInventory == null)
                return;

            if (sourceInventory != owner)
                HandleExternalDrag(sourceInventory, evt.SourceIndex, evt.TargetIndex);
            else
                HandleInternalDrag(evt.SourceIndex, evt.TargetIndex);
        }

        private void HandleDragCancelled(InventoryWindowController _)
        {
            // Reserved for future feedback hooks.
        }

        private void HandlePrimaryClick(int index)
        {
            var entry = model.GetEntry(index);
            // Prevent interacting with ore stacks that already reside inside the ore bag inventory UI.
            if (IsOreBagOwner && entry.item != null && OreBagService.Instance.IsOre(entry.item))
                return;

            if (!IsCompanionOwner && entry.item is OreBagItemData && OreBagService.Instance.TryOpenBagFromSlot(owner, index))
                return;

            if (entry.item != null && entry.item.healAmount > 0)
            {
                UseItem(index);
                return;
            }

            if (owner.selectedIndex < 0)
            {
                if (entry.item == null)
                    return;

                if (entry.item.equipmentSlot != EquipmentSlot.None && EquipItem(index))
                    return;

                owner.selectedIndex = index;
                controller.SetSelectedIndex(index);
                return;
            }

            if (owner.selectedIndex == index)
            {
                ClearSelection();
                return;
            }

            CombineSlots(owner.selectedIndex, index);
        }

        private void HandleSecondaryClick(int index, InventoryWindowController.SlotClickEvent evt)
        {
            var entry = model.GetEntry(index);

            // Block right-click menus for ores housed inside the ore bag so players cannot attempt disallowed actions.
            if (IsOreBagOwner && entry.item != null && OreBagService.Instance.IsOre(entry.item))
                return;

            if (evt.ShiftHeld)
            {
                ShowStackSplitPrompt(index, StackSplitType.Drop);
                return;
            }

            if (entry.item == null)
                return;

            contextMenuOptions.Clear();

            bool isPlayerInventory = !IsCompanionOwner;
            bool canDrop = CanDropItems && !entry.item.isUndroppable;
            bool hasInteractable = false;

            if (isPlayerInventory)
            {
                if (entry.item.equipmentSlot != EquipmentSlot.None && equipment != null)
                {
                    contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                        "Equip",
                        InventoryItemContextAction.Equip,
                        true));
                    hasInteractable = true;
                }

                var eater = owner.GetComponent<PlayerEat>();
                bool canEat = eater != null && entry.item.healAmount > 0;
                if (canEat)
                {
                    contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                        "Eat",
                        InventoryItemContextAction.Eat,
                        true));
                    hasInteractable = true;
                }

                contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                    "Use",
                    InventoryItemContextAction.Use,
                    true));
                hasInteractable = true;

                bool hasOreBag = OreBagService.Instance.HasBagInInventory();
                bool isOre = OreBagService.Instance.IsOre(entry.item);
                if (hasOreBag && isOre)
                {
                    contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                        "Add to Ore Bag",
                        InventoryItemContextAction.AddToOreBag,
                        true));
                    hasInteractable = true;
                }

                if (entry.item is OreBagItemData bagData && bagData.UpgradeTarget != null)
                {
                    contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                        "Upgrade",
                        InventoryItemContextAction.UpgradeOreBag,
                        true));
                    hasInteractable = true;
                }

                bool companionInventoryVisible = CompanionManager.IsInventoryVisible();
                var companionInventoryWrapper = CompanionManager.CompanionInventory;
                var companionInventoryComponent = companionInventoryWrapper?.InventoryComponent;
                if (companionInventoryVisible && companionInventoryComponent != null)
                {
                    contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                        "Transfer",
                        InventoryItemContextAction.Transfer,
                        true));
                    hasInteractable = true;
                }

                contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                    "Drop",
                    InventoryItemContextAction.Drop,
                    canDrop));
                hasInteractable |= canDrop;

                contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                    "Examine",
                    InventoryItemContextAction.Examine,
                    true));
                hasInteractable = true;
            }
            else
            {
                if (entry.item.equipmentSlot != EquipmentSlot.None && companionEquipment != null)
                {
                    contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                        "Equip",
                        InventoryItemContextAction.Equip,
                        true));
                    hasInteractable = true;
                }

                var playerInventory = CompanionManager.GetPlayerInventory();
                bool canResolvePlayerInventory = playerInventory != null;
                if (canResolvePlayerInventory)
                {
                    contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                        "Transfer",
                        InventoryItemContextAction.Transfer,
                        true));
                    hasInteractable = true;
                }

                contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                    "Use",
                    InventoryItemContextAction.Use,
                    true));
                hasInteractable = true;

                contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                    "Drop",
                    InventoryItemContextAction.Drop,
                    canDrop));
                hasInteractable |= canDrop;

                contextMenuOptions.Add(new InventoryItemContextMenu.Option(
                    "Examine",
                    InventoryItemContextAction.Examine,
                    true));
                hasInteractable = true;
            }

            if (contextMenuOptions.Count == 0 || !hasInteractable)
                return;

            controller.ShowItemContextMenu(index, evt.PointerPosition, contextMenuOptions);
        }

        private void HandleContextActionSelected(
            InventoryWindowController _,
            InventoryWindowController.ItemContextActionEvent evt)
        {
            controller.DismissContextMenus();

            int slotIndex = evt.SlotIndex;
            if (slotIndex < 0 || slotIndex >= model.Size)
                return;

            var entry = model.GetEntry(slotIndex);
            var item = entry.item;

            switch (evt.Action)
            {
                case InventoryItemContextAction.Equip:
                    if (item == null)
                        break;

                    if (IsCompanionOwner)
                        TryEquipCompanionItem(slotIndex);
                    else
                        EquipItem(slotIndex);
                    break;

                case InventoryItemContextAction.Eat:
                    if (!IsCompanionOwner)
                        UseItem(slotIndex);
                    break;

                case InventoryItemContextAction.Use:
                    if (item == null)
                        break;

                    bool handled = false;
                    if (!IsCompanionOwner)
                    {
                        var eater = owner.GetComponent<PlayerEat>();
                        if (eater != null && item.healAmount > 0)
                        {
                            SelectItemForUse(slotIndex);
                            handled = true;
                        }
                    }

                    if (!handled)
                    {
                        handled = UseItem(slotIndex);
                        if (!handled)
                            SelectItemForUse(slotIndex);
                    }
                    break;

                case InventoryItemContextAction.Drop:
                    if (item == null)
                        break;

                    if (item.isUndroppable)
                    {
                        PublishUndroppableItemMessage();
                        break;
                    }

                    if (!CanDropItems)
                        break;

                    if (entry.count > 1)
                        controller.ShowDropMenu(slotIndex, evt.PointerPosition);
                    else
                        DropItem(slotIndex, 1);
                    break;

                case InventoryItemContextAction.Transfer:
                    if (IsCompanionOwner)
                        TryTransferToPlayerInventory(slotIndex);
                    else
                        TryTransferToCompanionInventory(slotIndex);
                    break;

                case InventoryItemContextAction.Examine:
                    ExamineItem(slotIndex);
                    break;

                case InventoryItemContextAction.AddToOreBag:
                    if (!IsCompanionOwner)
                        TryDepositAllOreToBag();
                    break;

                case InventoryItemContextAction.UpgradeOreBag:
                    if (!IsCompanionOwner)
                        TryUpgradeOreBag(slotIndex);
                    break;
            }

            controller.DismissTooltip();
        }

        private void TryDepositAllOreToBag()
        {
            var service = OreBagService.Instance;
            if (service.TryDepositAllPlayerOre(owner, true, out int added, out _))
                controller.RefreshAllSlots();
        }

        private void TryUpgradeOreBag(int slotIndex)
        {
            var service = OreBagService.Instance;
            if (service.TryUpgradeBag(owner, slotIndex))
                controller.RefreshSlot(slotIndex);
        }

        private void SelectItemForUse(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= model.Size)
                return;

            var entry = model.GetEntry(slotIndex);
            if (entry.item == null)
                return;

            owner.selectedIndex = slotIndex;
            controller.SetSelectedIndex(slotIndex);
        }

        private bool TryEquipCompanionItem(int slotIndex)
        {
            if (!IsCompanionOwner || companionEquipment == null)
                return false;
            if (slotIndex < 0 || slotIndex >= model.Size)
                return false;

            var entry = model.GetEntry(slotIndex);
            if (entry.item == null || entry.item.equipmentSlot == EquipmentSlot.None)
                return false;

            var removedEntry = model.TakeEntry(slotIndex);
            var result = companionEquipment.TryEquipFromCompanionInventory(removedEntry, owner);

            if (result == CompanionEquipAttemptResult.NotHandled)
            {
                model.SetEntry(slotIndex, removedEntry);
                controller.RefreshSlot(slotIndex);
                return false;
            }

            controller.RefreshSlot(slotIndex);

            if (result != CompanionEquipAttemptResult.Equipped)
                return false;

            if (owner.selectedIndex == slotIndex)
                ClearSelection();

            return true;
        }

        private bool TryTransferToPlayerInventory(int slotIndex)
        {
            if (!IsCompanionOwner)
                return false;
            if (slotIndex < 0 || slotIndex >= model.Size)
                return false;

            var entry = model.GetEntry(slotIndex);
            if (entry.item == null)
                return false;

            var playerInventory = CompanionManager.GetPlayerInventory();
            if (playerInventory == null)
                return false;

            if (!playerInventory.CanAddItem(entry.item, entry.count))
            {
                PublishPlayerInventoryFullMessage();
                return false;
            }

            var removedEntry = model.TakeEntry(slotIndex);
            if (removedEntry.item == null)
                return false;

            if (!playerInventory.AddItem(removedEntry.item, removedEntry.count))
            {
                model.SetEntry(slotIndex, removedEntry);
                controller.RefreshSlot(slotIndex);
                PublishPlayerInventoryFullMessage();
                return false;
            }

            controller.RefreshSlot(slotIndex);
            if (owner.selectedIndex == slotIndex)
                ClearSelection();

            playerInventory.WindowController?.RefreshAllSlots();
            return true;
        }

        private bool TryTransferToCompanionInventory(int slotIndex)
        {
            if (IsCompanionOwner)
                return false;
            if (slotIndex < 0 || slotIndex >= model.Size)
                return false;

            var entry = model.GetEntry(slotIndex);
            if (entry.item == null)
                return false;

            var companionInventoryWrapper = CompanionManager.CompanionInventory;
            var companionInventoryComponent = companionInventoryWrapper?.InventoryComponent;
            if (companionInventoryComponent == null)
                return false;

            if (!companionInventoryComponent.CanAddItem(entry.item, entry.count))
            {
                PublishCompanionInventoryFullMessage();
                return false;
            }

            var removedEntry = model.TakeEntry(slotIndex);
            if (removedEntry.item == null)
                return false;

            if (!companionInventoryComponent.AddItem(removedEntry.item, removedEntry.count))
            {
                model.SetEntry(slotIndex, removedEntry);
                controller.RefreshSlot(slotIndex);
                PublishCompanionInventoryFullMessage();
                return false;
            }

            controller.RefreshSlot(slotIndex);
            if (owner.selectedIndex == slotIndex)
                ClearSelection();

            companionInventoryComponent.WindowController?.RefreshAllSlots();
            owner.WindowController?.RefreshAllSlots();
            return true;
        }

        private void ExamineItem(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= model.Size)
                return;

            var entry = model.GetEntry(slotIndex);
            var item = entry.item;
            if (item == null)
                return;

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string name = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            string description = item.description;
            string message = string.IsNullOrWhiteSpace(description)
                ? $"You examine the {name}."
                : $"{name}: {description.Trim()}";

            chat.PublishGameMessage(message);
        }

        private void CombineSlots(int sourceIndex, int targetIndex)
        {
            bool performed = false;
            bool keepSelection = false;
            int selectionIndex = sourceIndex;

            if (TryHandleFiremakingCombination(sourceIndex, targetIndex, out bool fireKeepSelection, out int fireSelection))
            {
                performed = true;
                keepSelection = fireKeepSelection;
                selectionIndex = fireSelection >= 0 ? fireSelection : sourceIndex;
            }
            else if (model.CombineItems(sourceIndex, targetIndex, out bool modelKeepSelection))
            {
                performed = true;
                keepSelection = modelKeepSelection;
                selectionIndex = sourceIndex;
            }

            if (!performed)
            {
                owner.selectedIndex = targetIndex;
                controller.SetSelectedIndex(targetIndex);
                return;
            }

            if (keepSelection)
            {
                owner.selectedIndex = selectionIndex;
                controller.SetSelectedIndex(selectionIndex);
            }
            else
            {
                ClearSelection();
            }

            controller.RefreshSlot(sourceIndex);
            controller.RefreshSlot(targetIndex);
            if (keepSelection && selectionIndex != sourceIndex && selectionIndex != targetIndex)
                controller.RefreshSlot(selectionIndex);
        }

        private bool TryHandleFiremakingCombination(int firstIndex, int secondIndex, out bool keepSelection, out int selectionIndex)
        {
            keepSelection = false;
            selectionIndex = -1;

            var skill = ResolveFiremakingSkill();
            if (skill == null)
                return false;

            var first = model.GetEntry(firstIndex);
            var second = model.GetEntry(secondIndex);
            if (first.item == null || second.item == null)
                return false;

            bool firstIsTinder = string.Equals(first.item.id, skill.TinderboxItemId, StringComparison.OrdinalIgnoreCase);
            bool secondIsTinder = string.Equals(second.item.id, skill.TinderboxItemId, StringComparison.OrdinalIgnoreCase);
            if (!firstIsTinder && !secondIsTinder)
                return false;

            bool firstIsLog = skill.GetDefinitionForItem(first.item.id) != null;
            bool secondIsLog = skill.GetDefinitionForItem(second.item.id) != null;
            if (!firstIsLog && !secondIsLog)
                return false;

            int logSlot = firstIsLog ? firstIndex : secondIndex;
            if (skill.BeginLightingFromInventory(logSlot, out string failure))
            {
                keepSelection = true;
                selectionIndex = logSlot;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(failure))
                ShowFailureMessage(failure);
            keepSelection = true;
            selectionIndex = logSlot;
            return true;
        }

        private void HandleExternalDrag(InventoryComponent sourceInventory, int sourceIndex, int targetIndex)
        {
            if (IsOreBagOwner)
            {
                if (sourceInventory != null)
                    OreBagService.Instance.TryDepositSlotFromPlayer(sourceInventory, sourceIndex, true, out _, out _);
                return;
            }

            if (targetIndex < 0 || targetIndex >= model.Size)
                return;

            var storage = ResolvePetStorage();
            if (storage != null &&
                (storage.definition?.id == "Heron" ||
                 storage.definition?.id == "Beaver" ||
                 storage.definition?.id == "Rock Golem" ||
                 storage.definition?.id == "Mr Frying Pan"))
            {
                var entry = sourceInventory.Model.GetEntry(sourceIndex);
                if (!storage.StoreItem(entry.item, entry.count))
                    return;

                sourceInventory.Model.ClearSlot(sourceIndex);
                sourceInventory.WindowController?.RefreshSlot(sourceIndex);
                return;
            }

            var destinationEntry = model.GetEntry(targetIndex);
            var movedEntry = sourceInventory.Model.TakeEntry(sourceIndex);

            if (!model.SetEntry(targetIndex, movedEntry))
            {
                sourceInventory.Model.SetEntry(sourceIndex, movedEntry);
                return;
            }

            if (!sourceInventory.Model.SetEntry(sourceIndex, destinationEntry))
            {
                model.SetEntry(targetIndex, destinationEntry);
                sourceInventory.Model.SetEntry(sourceIndex, movedEntry);
                return;
            }

            controller.RefreshSlot(targetIndex);
            sourceInventory.WindowController?.RefreshSlot(sourceIndex);
        }

        private void HandleInternalDrag(int sourceIndex, int targetIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= model.Size)
                return;
            if (targetIndex < 0 || targetIndex >= model.Size)
                return;

            if (targetIndex == sourceIndex)
            {
                controller.RefreshSlot(sourceIndex);
                return;
            }

            var destinationEntry = model.GetEntry(targetIndex);
            var draggedEntry = model.GetEntry(sourceIndex);

            if (!model.SetEntry(targetIndex, draggedEntry))
                return;

            if (!model.SetEntry(sourceIndex, destinationEntry))
            {
                model.TakeEntry(targetIndex);
                model.SetEntry(targetIndex, destinationEntry);
                model.SetEntry(sourceIndex, draggedEntry);
                return;
            }

            controller.RefreshSlot(targetIndex);
            controller.RefreshSlot(sourceIndex);
        }

        private void DropItem(int slotIndex, int quantity)
        {
            if (!CanDropItems)
                return;

            if (slotIndex < 0 || slotIndex >= model.Size)
                return;

            var entry = model.GetEntry(slotIndex);
            if (entry.item == null)
                return;

            if (entry.item.isUndroppable)
            {
                PublishUndroppableItemMessage();
                return;
            }

            int dropAmount = Mathf.Clamp(quantity, 1, entry.count);

            // Cache the item before removal so we can decide whether to spawn a pet or ground loot afterwards.
            var droppedItem = entry.item;
            var petDefinition = PetDropSystem.FindPetByItem(droppedItem);
            bool shouldSummonPet = dropAmount == 1 && petDefinition != null;

            if (petDefinition != null && PetDropSystem.IsActivePetDefinition(petDefinition))
            {
                string activeDisplayName = PetDropSystem.ActivePet != null &&
                    !string.IsNullOrEmpty(PetDropSystem.ActivePet.displayName)
                    ? PetDropSystem.ActivePet.displayName
                    : petDefinition.displayName;

                PublishActivePetDropBlockedMessage(activeDisplayName);
                controller.RefreshSlot(slotIndex);
                controller.DismissContextMenus();
                return;
            }

            if (petDefinition != null && petDefinition.spawnAsCompanion &&
                CompanionManager.IsActiveCompanionDefinition(petDefinition))
            {
                string companionDisplayName = CompanionManager.GetCompanionDisplayName();
                string displayName = !string.IsNullOrEmpty(companionDisplayName)
                    ? companionDisplayName
                    : petDefinition.displayName;
                PublishActivePetDropBlockedMessage(displayName);
                controller.RefreshSlot(slotIndex);
                controller.DismissContextMenus();
                return;
            }

            model.RemoveFromSlot(slotIndex, dropAmount);
            controller.RefreshSlot(slotIndex);

            bool petSummoned = false;
            if (shouldSummonPet)
            {
                // Invoke the central pet use flow so existing pets are returned to the inventory and the
                // newly dropped pet materialises next to the player.
                petSummoned = PetUseHandler.TryUse(droppedItem);
            }

            if (!petSummoned)
            {
                // Fall back to spawning the item on the ground when the drop was not a pet summon or the
                // summon failed (for example while merged into another pet).
                SpawnGroundItem(droppedItem, dropAmount);
            }

            if (owner.selectedIndex == slotIndex && model.GetEntry(slotIndex).item == null)
                ClearSelection();

            controller.DismissTooltip();
        }

        private void ShowStackSplitPrompt(int slotIndex, StackSplitType type)
        {
            if (slotIndex < 0 || slotIndex >= model.Size)
                return;

            var entry = model.GetEntry(slotIndex);
            if (entry.item == null || entry.count <= 1)
                return;

            if (type == StackSplitType.Drop && entry.item.isUndroppable)
            {
                PublishUndroppableItemMessage();
                return;
            }

            controller.DismissContextMenus();
            StackSplitDialog.Show(controller.UiRoot.transform, entry.count, amount =>
            {
                amount = Mathf.Clamp(amount, 1, entry.count);
                switch (type)
                {
                    case StackSplitType.Drop:
                        DropItem(slotIndex, amount);
                        break;
                    case StackSplitType.Sell:
                        SellItem(slotIndex, amount);
                        break;
                    case StackSplitType.Split:
                        model.SplitStack(slotIndex, amount);
                        break;
                }
            });
        }

        private void SpawnGroundItem(ItemData item, int amount)
        {
            if (item == null || amount <= 0)
                return;

            var spawner = ResolveGroundItemSpawner();
            if (spawner == null)
                return;

            Vector3 origin = owner.transform != null ? owner.transform.position : Vector3.zero;
            spawner.Spawn(item, amount, origin);
        }

        /// <summary>
        /// Publishes a Game-channel chat message explaining why a pet item could not be dropped.
        /// </summary>
        private static void PublishActivePetDropBlockedMessage(string petDisplayName)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            if (string.IsNullOrWhiteSpace(petDisplayName))
                petDisplayName = "pet";

            chat.PublishGameMessage($"You already have a \"{petDisplayName}\" spawned");
        }

        /// <summary>
        /// Publishes the standard feedback when an undroppable item is interacted with.
        /// </summary>
        private static void PublishUndroppableItemMessage()
        {
            var chat = ChatService.Instance;
            chat?.PublishGameMessage("This item is undroppable.");
        }

        private GroundItemSpawner ResolveGroundItemSpawner()
        {
            if (groundItemSpawner == null)
                groundItemSpawner = UnityEngine.Object.FindObjectOfType<GroundItemSpawner>(true);
            return groundItemSpawner;
        }

        private FiremakingSkill ResolveFiremakingSkill()
        {
            if (firemakingSkill == null)
                firemakingSkill = owner.GetComponent<FiremakingSkill>();
            return firemakingSkill;
        }

        private PetStorage ResolvePetStorage()
        {
            if (petStorage == null)
                owner.TryGetComponent(out petStorage);
            return petStorage;
        }

        private void UpdatePetStorageVisibility()
        {
            if (playerMover == null)
                return;

            var pet = PetDropSystem.ActivePetObject;
            var storage = pet != null ? pet.GetComponent<PetStorage>() : null;
            if (storage == null)
                return;

            if (IsLocalPetStorage(storage))
                return;

            if (!owner.BankOpen)
            {
                if (PetDropSystem.PetInventoryVisible)
                    storage.Open();
                else
                    storage.Close();
            }
            else
            {
                storage.Close();
            }
        }

        private void ClosePetStorage()
        {
            var pet = PetDropSystem.ActivePetObject;
            var storage = pet != null ? pet.GetComponent<PetStorage>() : null;
            if (storage == null)
                return;

            if (IsLocalPetStorage(storage))
                return;

            storage.Close();
        }

        /// <summary>
        ///     Determines whether the supplied pet storage component belongs to this inventory instance.
        ///     Pet storage inventories should not try to close themselves when propagating visibility changes,
        ///     otherwise recursive close calls will overflow the stack.
        /// </summary>
        private bool IsLocalPetStorage(PetStorage storage)
        {
            if (storage == null)
                return false;

            return owner != null && owner.TryGetComponent(out PetStorage localStorage) && ReferenceEquals(storage, localStorage);
        }

        private void ShowFailureMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Transform anchor = firemakingSkill != null ? firemakingSkill.transform : owner.transform;
            if (anchor == null)
                return;

            if (!GatheringFloatingTextService.TryShowAtAnchor(message, anchor))
                FloatingText.Show(message, anchor.position);
        }

        private void HandleQuestUiOpened(QuestUI quest)
        {
            questUi = quest ?? QuestUI.Instance;
            if (questUi != null && questUi.IsOpen && owner.IsOpen)
                RequestClose();
        }

        private void HandleQuestUiClosed(QuestUI quest)
        {
            questUi = quest ?? QuestUI.Instance;
        }

        private void EnsureQuestUiReference()
        {
            if (questUi != null)
                return;

            questUi = QuestUI.Instance;
            if (questUi == null)
            {
#if UNITY_2022_2_OR_NEWER
                questUi = UnityEngine.Object.FindFirstObjectByType<QuestUI>(FindObjectsInactive.Include);
#else
                questUi = UnityEngine.Object.FindObjectOfType<QuestUI>(true);
#endif
            }
        }

        /// <summary>
        ///     Emits the standard "inventory full" message from the companion when the player
        ///     attempts to transfer an item but the follower cannot accept it.
        /// </summary>
        private void PublishCompanionInventoryFullMessage()
        {
            var chat = ChatService.Instance;
            string line = CompanionPickupDialogueLibrary.GetRandomInventoryFullResponse(chat != null ? chat.ActiveUsername : string.Empty);
            if (string.IsNullOrEmpty(line))
                return;

            if (chat != null)
            {
                string companionName = CompanionManager.GetCompanionDisplayName();
                if (string.IsNullOrWhiteSpace(companionName))
                    companionName = "Companion";

                chat.PublishCompanionMessage(companionName, line);
            }
            else
            {
                ChatboxUI.PostSystemMessage(line);
            }
        }

        /// <summary>
        ///     Emits the game channel feedback used when the player cannot receive an item
        ///     from their companion because their own inventory is full.
        /// </summary>
        private static void PublishPlayerInventoryFullMessage()
        {
            var chat = ChatService.Instance;
            chat?.PublishGameMessage("My inventory is full.");
        }

        private void SubscribeToController()
        {
            if (controllerEventsSubscribed)
                return;

            controller.SlotClicked += HandleSlotClicked;
            controller.DropRequested += HandleDropRequested;
            controller.SplitPromptRequested += HandleSplitPromptRequested;
            controller.DragDropRequested += HandleDragDropRequested;
            controller.DragCancelled += HandleDragCancelled;
            controller.ContextActionSelected += HandleContextActionSelected;
            controllerEventsSubscribed = true;
        }

        private void UnsubscribeFromController()
        {
            if (!controllerEventsSubscribed)
                return;

            controller.SlotClicked -= HandleSlotClicked;
            controller.DropRequested -= HandleDropRequested;
            controller.SplitPromptRequested -= HandleSplitPromptRequested;
            controller.DragDropRequested -= HandleDragDropRequested;
            controller.DragCancelled -= HandleDragCancelled;
            controller.ContextActionSelected -= HandleContextActionSelected;
            controllerEventsSubscribed = false;
        }
    }
}
