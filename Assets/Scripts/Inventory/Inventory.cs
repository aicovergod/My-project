// Assets/Scripts/Inventory/Inventory.cs
using System;
using UnityEngine;
using Core.Save;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using ShopSystem;
using BankSystem;
using Player;
using Skills;
using Skills.Firemaking;
using Pets;
using Quests;
using UI;
using UI.Utilities;
using Books;
using Inventory.Core;
using Inventory.UI;
using Object = UnityEngine.Object;

namespace Inventory
{
    /// <summary>
    /// Indicates how a stack split action should be handled.
    /// </summary>
    public enum StackSplitType
    {
        Drop,
        Sell,
        Split
    }

    /// <summary>
    /// Runtime inventory UI generator (Screen Space Overlay). The UI is created at scene root,
    /// starts inactive, and shows always-visible slot squares. If a slotFrameSprite is provided, it is used
    /// as the slot frame (set to Sliced).
    /// </summary>
    [DisallowMultipleComponent]
    public class Inventory : MonoBehaviour, IUIWindow, ISaveable
    {
        [Header("Inventory")]
        [Tooltip("Maximum number of items the inventory can hold.")]
        public int size = 20;

        [Header("UI Layout")]
        [Tooltip("Slot size in UI pixels.")]
        public Vector2 slotSize = new Vector2(32, 32);
        [Tooltip("Spacing between slots in UI pixels.")]
        public Vector2 slotSpacing = new Vector2(4, 4);
        [Tooltip("Reference resolution for Canvas Scaler.")]
        public Vector2 referenceResolution = new Vector2(1024f, 768f);
        [Tooltip("Number of columns in the slot grid.")]
        public int columns = 2;
        [Tooltip("Reuse a shared UI root across multiple inventories.")]
        public bool useSharedUIRoot = true;

        [Header("Empty Slot Look")]
        [Tooltip("Optional: frame sprite (9-sliced) to draw for each slot.")]
        public Sprite slotFrameSprite;
        [Tooltip("Color/tint for empty slots if no frame sprite, or tint over the frame.")]
        public Color emptySlotColor = new Color(0f, 0f, 0f, 1f); // solid black

        [Header("Stack Count Colors")]
        [Tooltip("Color used for stack counts below 10,000.")]
        public Color stackColorDefault = Color.yellow;
        [Tooltip("Color used for stack counts of 10,000 or more.")]
        public Color stackColor10k = Color.white;
        [Tooltip("Color used for stack counts of 100,000 or more.")]
        public Color stackColor100k = Color.green;
        [Tooltip("Color used for stack counts of 10,000,000 or more.")]
        public Color stackColor10m = Color.cyan;
        [Tooltip("Color used for stack counts of 100,000,000 or more.")]
        public Color stackColor100m = Color.magenta;

        [Tooltip("Optional: custom font for stack count text. Uses LegacyRuntime if null.")]
        public Font stackCountFont;

        [Tooltip("Font size for stack count text.")]
        public int stackCountFontSize = 12;

        [Header("Window")]
        [Tooltip("Background color for the inventory window.")]
        public Color windowColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
        [Tooltip("Padding around the slot grid inside the window.")]
        public Vector2 windowPadding = new Vector2(8f, 8f);
        [Tooltip("Fixed width and height for the inventory window background.")]
        public Vector2 windowSize = new Vector2(83f, 375f);
        [Tooltip("Show a close button in the top-right corner.")]
        public bool showCloseButton;

        [Tooltip("Center the inventory window on screen instead of anchoring to the top-left.")]
        public bool centerOnScreen;

        [Tooltip("Anchored position of the inventory window.")]
        public Vector2 windowPosition = new Vector2(870f, -300f);

        [Header("Tooltip")]
        [Tooltip("Optional: custom font for the tooltip item name. Uses LegacyRuntime if null.")]
        public Font tooltipNameFont;
        [Tooltip("Color for the tooltip item name text.")]
        public Color tooltipNameColor = Color.white;
        [Tooltip("Optional: custom font for the tooltip description. Uses LegacyRuntime if null.")]
        public Font tooltipDescriptionFont;
        [Tooltip("Color for the tooltip description text.")]
        public Color tooltipDescriptionColor = new Color(184/255f, 134/255f, 11/255f, 1f);

        [Header("Save")]
        [Tooltip("Save key used for persistence.")]
        public string saveKey = "InventoryData";

        [Header("Combination")]
        [Tooltip("Database of valid item combinations.")]
        public ItemCombinationDatabase combinationDatabase;

        private InventoryModel model;
        public int selectedIndex = -1;
        private InventoryWindowController windowController;

        // Active shop context when interacting with a shop
        private Shop currentShop;

        private PlayerMover playerMover;
        private Equipment equipment;
        private FiremakingSkill firemakingSkill;
        private PetStorage petStorage;

        // Cached quest UI reference to avoid per-frame lookups.
        private QuestUI questUi;

        // Cached default font to avoid repeated builtin lookups that may throw
        private Font defaultFont;

        public event Action OnInventoryChanged;

        public bool IsOpen => windowController != null && windowController.UiRoot != null && windowController.UiRoot.activeSelf;

        private bool bankOpen;
        public bool BankOpen
        {
            get => bankOpen;
            set
            {
                bankOpen = value;
                if (windowController != null)
                    windowController.IsBankOpen = value;
            }
        }
        public bool InShop => currentShop != null;

        private bool CanDropItems => playerMover == null || playerMover.CanDrop;

        private InventoryModel Model
        {
            get
            {
                EnsureModelInitialized();
                return model;
            }
        }

        /// <summary>
        /// Ensures the backing <see cref="InventoryModel"/> exists and matches the configured size.
        /// </summary>
        private void EnsureModelInitialized()
        {
            size = Mathf.Max(1, size);

            if (model == null)
                model = new InventoryModel(size, EvaluateCanStore, combinationDatabase);

            model.CanStoreRule = EvaluateCanStore;
            model.SetCombinationDatabase(combinationDatabase);
            if (model.Size != size)
                model.Resize(size);

            model.InventoryChanged -= OnModelInventoryChanged;
            model.InventoryChanged += OnModelInventoryChanged;
            model.SlotChanged -= OnModelSlotChanged;
            model.SlotChanged += OnModelSlotChanged;
        }

        /// <summary>
        /// Propagates model-level inventory changes to persistence and observers.
        /// </summary>
        /// <param name="persist">True to write the inventory state before notifying listeners.</param>
        private void OnModelInventoryChanged(bool persist)
        {
            NotifyInventoryChanged(persist);
        }

        /// <summary>
        /// Refreshes UI when a slot changes within the backing model.
        /// </summary>
        private void OnModelSlotChanged(int index, InventoryEntry entry)
        {
            windowController?.RefreshSlot(index);
        }

        /// <summary>
        /// Evaluates whether an item can be stored, deferring to the active pet storage component when present.
        /// </summary>
        private bool EvaluateCanStore(ItemData item)
        {
            var storage = GetPetStorage();
            return storage == null || storage.CanStore(item);
        }

        /// <summary>
        /// Returns and caches the PetStorage component when attached.
        /// </summary>
        private PetStorage GetPetStorage()
        {
            if (petStorage == null)
                TryGetComponent(out petStorage);
            return petStorage;
        }

        /// <summary>
        /// Saves the inventory when requested and informs listeners that the
        /// inventory contents changed.
        /// </summary>
        /// <param name="persist">True to write the inventory state to disk before notifying listeners.</param>
        private void NotifyInventoryChanged(bool persist = true)
        {
            if (persist)
                Save();
            OnInventoryChanged?.Invoke();
        }

        public void CloseUI()
        {
            // Prevent the player inventory from closing while trading or banking to avoid inconsistent states.
            if (BankOpen || InShop)
                return;
            windowController?.Hide();
            if (playerMover != null)
            {
                var pet = PetDropSystem.ActivePetObject;
                var storage = pet != null ? pet.GetComponent<PetStorage>() : null;
                storage?.Close();
            }
        }

        public void Close()
        {
            CloseUI();
        }

        public void OpenUI()
        {
            if (!BankOpen && !InShop && useSharedUIRoot)
            {
                var uiManager = UIManager.Instance;
                if (uiManager != null && !uiManager.TryOpenWindow(this))
                    return;
            }
            InterfaceTabMutexUtility.CloseAllTabWindowsExcept(this);
            windowController?.Show();
            if (playerMover != null)
            {
                var pet = PetDropSystem.ActivePetObject;
                var storage = pet != null ? pet.GetComponent<PetStorage>() : null;
                if (!BankOpen)
                {
                    if (PetDropSystem.PetInventoryVisible)
                        storage?.Open();
                    else
                        storage?.Close();
                }
                else
                    storage?.Close();
            }
        }

        public InventoryEntry GetSlot(int index)
        {
            return Model.GetEntry(index);
        }

        public void ClearSlot(int index)
        {
            Model.ClearSlot(index);
        }

        /// <summary>
        /// Equip the item in the given inventory slot if possible.
        /// </summary>
        public bool EquipItem(int index)
        {
            if (equipment == null)
                return false;
            if (index < 0 || index >= Model.Size)
                return false;
            var entry = Model.GetEntry(index);
            if (entry.item == null || entry.item.equipmentSlot == EquipmentSlot.None)
                return false;

            // Temporarily free the slot before attempting to equip.
            Model.RemoveFromSlot(index, entry.count);

            // Try to equip the item.
            if (equipment.Equip(entry))
            {
                return true;
            }

            // Equipping failed. Restore the original item.
            Model.ReplaceItem(index, null, entry.item, entry.count);
            return false;
        }

        /// <summary>
        /// Use the item in the given slot if it supports usage.
        /// Opens books or consumes food items.
        /// </summary>
        public bool UseItem(int index)
        {
            if (index < 0 || index >= Model.Size)
                return false;

            var entry = Model.GetEntry(index);
            var item = entry.item;

            if (item is BookItemData bookItem && bookItem.book != null)
            {
                BookUI.Instance.Open(bookItem.book);
                return true;
            }

            var eater = GetComponent<PlayerEat>();
            if (eater != null && item != null && item.healAmount > 0)
            {
                if (eater.Eat(item))
                {
                    if (!string.IsNullOrEmpty(item.replacementItemId))
                    {
                        var next = ItemDatabase.GetItem(item.replacementItemId);
                        if (!Model.ReplaceItem(index, item, next, next != null ? 1 : 0))
                            Model.RemoveFromSlot(index, entry.count);
                    }
                    else
                    {
                        Model.RemoveFromSlot(index, 1);
                    }
                    ItemUseResolver.NotifyItemUsed(gameObject, item, ItemUseType.Consumed);
                    return true;
                }
            }

            return false;
        }

        private void OnEnable()
        {
            EnsureInitialized();
            SaveManager.Register(this);
            QuestUI.QuestUIOpened += OnQuestUiOpened;
            QuestUI.QuestUIClosed += OnQuestUiClosed;
            questUi = QuestUI.Instance;
        }

        private void OnDisable()
        {
            SaveManager.Unregister(this);
            QuestUI.QuestUIOpened -= OnQuestUiOpened;
            QuestUI.QuestUIClosed -= OnQuestUiClosed;
        }

        /// <summary>
        /// Ensures runtime data structures exist before the inventory participates in save/load.
        /// </summary>
        private void EnsureInitialized()
        {
            size = Mathf.Max(1, size);

            if (defaultFont == null)
                defaultFont = LegacyFontProvider.GetLegacyFont();

            if (stackCountFont == null)
            {
                stackCountFont = Resources.Load<Font>("ThaleahFat_TTF") ??
                                 Resources.Load<Font>("ThaleahFAT_TTF") ??
                                 defaultFont;
            }

            EnsureModelInitialized();

            if (EventSystem.current == null)
                EnsureEventSystem();

            if (windowController == null)
            {
                windowController = new InventoryWindowController(Model, BuildWindowConfig());
                windowController.SlotClicked += OnSlotClicked;
                windowController.DropRequested += OnDropRequested;
                windowController.SplitRequested += OnSplitRequested;
                windowController.DragDropRequested += OnDragDropRequested;
                windowController.DragCancelled += OnDragCancelled;
                windowController.CloseRequested += OnWindowCloseRequested;
            }

            windowController.Owner = this;

            windowController.IsBankOpen = BankOpen;
            windowController.InShop = InShop;
            windowController.CanDropItems = CanDropItems;
            windowController.CurrentShop = currentShop;
            windowController.SetSelectedIndex(selectedIndex);
            windowController.RefreshAllSlots();
            windowController.Hide();

            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
        }

        /// <summary>
        /// Rebuilds the inventory UI on a dedicated canvas when shared canvas usage is disabled.
        /// </summary>
        public void ForceDedicatedUiRoot()
        {
            // Bail out immediately if this inventory still expects to use the shared canvas.
            if (useSharedUIRoot)
                return;

            // Ensure backing arrays/fonts are prepared before we touch the UI.
            EnsureInitialized();

            bool wasOpen = IsOpen;

            windowController?.ForceDedicatedCanvas();
            windowController?.SetSelectedIndex(selectedIndex);
            windowController?.RefreshAllSlots();

            if (wasOpen)
                windowController?.Show();
            else
                windowController?.Hide();
        }

        /// <summary>
        /// Builds the configuration payload used when instantiating the window controller.
        /// </summary>
        private InventoryWindowController.WindowConfig BuildWindowConfig()
        {
            return new InventoryWindowController.WindowConfig(
                slotSize,
                slotSpacing,
                windowPadding,
                windowSize,
                referenceResolution,
                windowPosition,
                windowColor,
                emptySlotColor,
                stackColorDefault,
                stackColor10k,
                stackColor100k,
                stackColor10m,
                stackColor100m,
                tooltipNameColor,
                tooltipDescriptionColor,
                defaultFont,
                stackCountFont,
                tooltipNameFont,
                tooltipDescriptionFont,
                slotFrameSprite,
                showCloseButton,
                centerOnScreen,
                useSharedUIRoot,
                columns,
                stackCountFontSize);
        }

        private void OnWindowCloseRequested(InventoryWindowController controller)
        {
            if (controller != windowController)
                return;

            CloseUI();
        }

        private void OnSlotClicked(InventoryWindowController controller, InventoryWindowController.SlotClickEvent evt)
        {
            if (controller != windowController)
                return;

            int index = evt.SlotIndex;

            if (BankOpen)
            {
                if (evt.Button == PointerEventData.InputButton.Left)
                    BankSystem.BankUI.Instance?.DepositFromInventory(index);
                else if (evt.Button == PointerEventData.InputButton.Right)
                    BankSystem.BankUI.Instance?.ShowDepositMenu(index, evt.PointerPosition);
                return;
            }

            if (InShop && evt.Button == PointerEventData.InputButton.Left)
            {
                SellItem(index, 1);
                return;
            }

            if (evt.Button != PointerEventData.InputButton.Left)
                return;

            var entry = Model.GetEntry(index);

            if (entry.item != null && entry.item.healAmount > 0)
            {
                UseItem(index);
                return;
            }

            if (selectedIndex < 0)
            {
                if (entry.item != null)
                {
                    if (entry.item.equipmentSlot != EquipmentSlot.None)
                    {
                        if (EquipItem(index))
                            return;
                    }

                    selectedIndex = index;
                    windowController?.SetSelectedIndex(selectedIndex);
                }
            }
            else if (selectedIndex == index)
            {
                ClearSelection();
            }
            else
            {
                int previouslySelected = selectedIndex;
                bool keepSelection;
                CombineItems(previouslySelected, index, out keepSelection);
                int newSelection = selectedIndex;

                if (!keepSelection)
                {
                    ClearSelection();
                }
                else
                {
                    selectedIndex = newSelection;
                    windowController?.SetSelectedIndex(selectedIndex);
                }

                windowController?.RefreshSlot(previouslySelected);
                windowController?.RefreshSlot(index);
                if (keepSelection && newSelection != previouslySelected && newSelection != index)
                    windowController?.RefreshSlot(newSelection);
            }
        }

        private void OnDropRequested(InventoryWindowController controller, InventoryWindowController.DropRequestEvent evt)
        {
            if (controller != windowController)
                return;

            DropItem(evt.SlotIndex, evt.Quantity);
        }

        private void OnSplitRequested(InventoryWindowController controller, InventoryWindowController.StackSplitEvent evt)
        {
            if (controller != windowController)
                return;

            switch (evt.SplitType)
            {
                case StackSplitType.Drop:
                    DropItem(evt.SlotIndex, evt.Quantity);
                    break;
                case StackSplitType.Sell:
                    if (currentShop != null)
                        SellItem(evt.SlotIndex, evt.Quantity);
                    else
                        SplitStack(evt.SlotIndex, evt.Quantity);
                    break;
                case StackSplitType.Split:
                    SplitStack(evt.SlotIndex, evt.Quantity);
                    break;
            }
        }

        private void OnDragDropRequested(InventoryWindowController controller, InventoryWindowController.DragDropEvent evt)
        {
            if (evt.Target != windowController)
                return;

            var sourceController = evt.Source;
            var sourceInventory = sourceController?.Owner;
            int sourceIndex = evt.SourceIndex;
            int targetIndex = evt.TargetIndex;

            if (sourceController == null || sourceInventory == null)
                return;

            if (sourceInventory != this)
                HandleExternalDrag(sourceInventory, sourceIndex, targetIndex);
            else
                HandleInternalDrag(sourceIndex, targetIndex);
        }

        private void OnDragCancelled(InventoryWindowController controller)
        {
            // Intentionally left empty. The callback remains for future analytics or feedback hooks.
        }

        private void HandleExternalDrag(Inventory sourceInventory, int sourceIndex, int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= Model.Size)
                return;

            var petStorage = GetComponent<PetStorage>();
            if (petStorage != null &&
                (petStorage.definition?.id == "Heron" ||
                 petStorage.definition?.id == "Beaver" ||
                 petStorage.definition?.id == "Rock Golem" ||
                 petStorage.definition?.id == "Mr Frying Pan"))
            {
                var entry = sourceInventory.Model.GetEntry(sourceIndex);
                if (!petStorage.StoreItem(entry.item, entry.count))
                    return;

                sourceInventory.Model.ClearSlot(sourceIndex);
                sourceInventory.windowController?.RefreshSlot(sourceIndex);
                return;
            }

            var destinationEntry = Model.GetEntry(targetIndex);
            var movedEntry = sourceInventory.Model.TakeEntry(sourceIndex);

            if (!Model.SetEntry(targetIndex, movedEntry))
            {
                sourceInventory.Model.SetEntry(sourceIndex, movedEntry);
                return;
            }

            if (!sourceInventory.Model.SetEntry(sourceIndex, destinationEntry))
            {
                Model.SetEntry(targetIndex, destinationEntry);
                sourceInventory.Model.SetEntry(sourceIndex, movedEntry);
                return;
            }

            windowController?.RefreshSlot(targetIndex);
            sourceInventory.windowController?.RefreshSlot(sourceIndex);
        }

        private void HandleInternalDrag(int sourceIndex, int targetIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= Model.Size)
                return;
            if (targetIndex < 0 || targetIndex >= Model.Size)
                return;

            if (targetIndex != sourceIndex)
            {
                var destinationEntry = Model.GetEntry(targetIndex);
                var draggedEntry = Model.GetEntry(sourceIndex);

                if (!Model.SetEntry(targetIndex, draggedEntry))
                    return;

                if (!Model.SetEntry(sourceIndex, destinationEntry))
                {
                    Model.TakeEntry(targetIndex);
                    Model.SetEntry(targetIndex, destinationEntry);
                    Model.SetEntry(sourceIndex, draggedEntry);
                    return;
                }

                windowController?.RefreshSlot(targetIndex);
            }

            windowController?.RefreshSlot(sourceIndex);
        }

        private void Start()
        {
            EnsureInitialized();
            UIManager.Instance.RegisterWindow(this);
        }

        /// <summary>
        /// Attempts to sell a quantity of the item at the given slot index to
        /// the current shop.
        /// </summary>
        public void SellItem(int slotIndex, int quantity = 1)
        {
            if (BankOpen || currentShop == null)
                return;
            if (slotIndex < 0 || slotIndex >= Model.Size)
                return;
            int sold = 0;
            for (int i = 0; i < quantity; i++)
            {
                var item = Model.GetEntry(slotIndex).item;
                if (item == null)
                    break;

                if (currentShop.Sell(item, this, slotIndex))
                    sold++;
                else
                    break;
            }

            if (sold > 0)
            {
                windowController?.DismissTooltip();
            }
        }

        /// <summary>
        /// Sets the active shop context used for selling and tooltip information.
        /// </summary>
        public void SetShopContext(Shop shop)
        {
            currentShop = shop;
            if (windowController != null)
            {
                windowController.CurrentShop = shop;
                windowController.InShop = shop != null;
                if (shop != null)
                    windowController.Show();
            }
        }

        public void Save()
        {
            EnsureModelInitialized();
            var data = Model.CaptureState();
            SaveManager.Save(saveKey, data);
        }

        public void Load()
        {
            EnsureModelInitialized();
            var data = SaveManager.Load<InventoryModel.InventorySaveData>(saveKey);
            Model.RestoreState(data);
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private void Update()
        {
            if (playerMover == null)
                return;

            EnsureQuestUiReference();

            if (windowController != null)
                windowController.CanDropItems = CanDropItems;

            if (questUi != null && questUi.IsOpen)
            {
                if (IsOpen)
                    CloseUI();
                return;
            }
            if (currentShop != null)
            {
                if (!IsOpen)
                    OpenUI();
                return;
            }
            if (BankOpen)
            {
                if (!IsOpen)
                    OpenUI();
                return;
            }
        }

        /// <summary>
        /// Ensures the cached quest UI reference stays valid without per-frame allocations.
        /// </summary>
        private void EnsureQuestUiReference()
        {
            if (questUi == null)
                questUi = QuestUI.Instance;
        }

        /// <summary>
        /// Handles quest UI open events so the inventory can immediately react.
        /// </summary>
        private void OnQuestUiOpened(QuestUI quest)
        {
            questUi = quest ?? QuestUI.Instance;

            if (questUi != null && questUi.IsOpen && IsOpen)
                CloseUI();
        }

        /// <summary>
        /// Keeps the cached quest UI reference in sync when the quest window closes or is destroyed.
        /// </summary>
        private void OnQuestUiClosed(QuestUI quest)
        {
            if (quest == null)
            {
                questUi = null;
                return;
            }

            questUi = quest;
        }

        /// <summary>
        /// Ensure an EventSystem exists for uGUI with the Input System module.
        /// </summary>
        private static void EnsureEventSystem()
        {
            var existing = Object.FindObjectOfType<EventSystem>();
            if (existing != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(null, false);
        }
    }
}
