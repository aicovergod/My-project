// Assets/Scripts/Inventory/Inventory.cs
using System;
using UnityEngine;
using Core.Save;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using ShopSystem;
using Player;
using Skills.Firemaking;
using Pets;
using Quests;
using UI;
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
        private InventoryInteractionHandler interactionHandler;

        // Tracks the configuration currently bound to the runtime window so hot reloads
        // can refresh layout without instantiating a new controller.
        private InventoryWindowController.WindowConfig? lastAppliedConfig;
        private int lastAppliedModelSize = -1;

        private bool modelEventsSubscribed;
        private bool controllerEventsSubscribed;

        // Prevents persistence writes when true so bootstrap sequences can
        // reconfigure the inventory without clobbering saved data.
        private bool suppressPersistenceNotifications;

        private PlayerMover playerMover;
        private Equipment equipment;
        private FiremakingSkill firemakingSkill;
        private PetStorage petStorage;
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
                interactionHandler?.RefreshControllerState();
            }
        }
        public bool InShop => interactionHandler != null && interactionHandler.HasActiveShop;

        internal InventoryModel Model
        {
            get
            {
                EnsureModelInitialized();
                SubscribeModelEvents();
                return model;
            }
        }

        internal InventoryWindowController WindowController => windowController;

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
        }

        /// <summary>
        /// Propagates model-level inventory changes to persistence and observers.
        /// </summary>
        /// <param name="persist">True to write the inventory state before notifying listeners.</param>
        private void OnModelInventoryChanged(bool persist)
        {
            bool shouldPersist = persist && !suppressPersistenceNotifications;
            NotifyInventoryChanged(shouldPersist);
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
            interactionHandler?.RequestClose();
        }

        public void Close()
        {
            CloseUI();
        }

        public void OpenUI()
        {
            interactionHandler?.RequestOpen();
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
        /// Removes all items from the inventory, persisting the change when any slots
        /// transition from occupied to empty.
        /// </summary>
        /// <returns><c>true</c> when one or more slots were cleared; otherwise <c>false</c>.</returns>
        public bool ClearAllSlots()
        {
            EnsureModelInitialized();
            return Model.ClearAllSlots();
        }

        /// <summary>
        /// Removes all items from the inventory without triggering persistence writes.
        /// This is used by bootstrap flows that need to reset layout/state before the
        /// inventory has restored its saved data.
        /// </summary>
        /// <returns><c>true</c> when one or more slots were cleared; otherwise <c>false</c>.</returns>
        public bool ClearAllSlotsWithoutPersistence()
        {
            EnsureModelInitialized();

            bool previousState = suppressPersistenceNotifications;
            suppressPersistenceNotifications = true;
            try
            {
                return Model.ClearAllSlots();
            }
            finally
            {
                suppressPersistenceNotifications = previousState;
            }
        }

        /// <summary>
        /// Equip the item in the given inventory slot if possible.
        /// </summary>
        public bool EquipItem(int index)
        {
            return interactionHandler != null && interactionHandler.EquipItem(index);
        }

        /// <summary>
        /// Use the item in the given slot if it supports usage.
        /// Opens books or consumes food items.
        /// </summary>
        public bool UseItem(int index)
        {
            return interactionHandler != null && interactionHandler.UseItem(index);
        }

        /// <summary>
        /// Clears the current slot selection and hides the highlight.
        /// </summary>
        public void ClearSelection()
        {
            interactionHandler?.ClearSelection();
        }

        /// <summary>
        /// Displays the shared inventory tooltip for the supplied item at the provided anchor.
        /// Used by external systems such as the bank so all UI surfaces share presentation.
        /// </summary>
        /// <param name="item">Item to display within the tooltip.</param>
        /// <param name="slotRect">RectTransform used to anchor the tooltip next to the hovered slot.</param>
        public void ShowTooltip(ItemData item, RectTransform slotRect)
        {
            if (item == null || slotRect == null)
                return;

            // Ensure the controller exists and immediately reopen modal windows so bank/shop
            // hover requests can surface tooltip data without collapsing the helper canvas.
            EnsureInitialized(allowModalReopen: true);
            windowController?.ShowTooltipForItem(item, slotRect);
        }

        /// <summary>
        /// Hides the currently visible tooltip if one is active.
        /// </summary>
        public void HideTooltip()
        {
            if (windowController == null)
                return;

            windowController.DismissTooltip();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            SubscribeModelEvents();
            SubscribeControllerEvents();
            SaveManager.Register(this);
            interactionHandler?.OnEnable();
        }

        private void OnDisable()
        {
            SaveManager.Unregister(this);
            interactionHandler?.OnDisable();
            UnsubscribeControllerEvents();
            UnsubscribeModelEvents();
        }

        /// <summary>
        /// Ensures runtime data structures exist before the inventory participates in save/load.
        /// </summary>
        /// <param name="allowModalReopen">
        /// True to reopen the inventory window immediately when modal flows such as the bank or
        /// shop are active so tooltip-only callers do not collapse the hover helper.
        /// </param>
        private void EnsureInitialized(bool allowModalReopen = false)
        {
            size = Mathf.Max(1, size);

            bool wasOpen = IsOpen;

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

            var config = BuildWindowConfig();

            if (windowController == null)
            {
                windowController = new InventoryWindowController(Model, config);
                controllerEventsSubscribed = false;
                lastAppliedConfig = config;
                lastAppliedModelSize = model != null ? model.Size : size;
            }
            else
            {
                ApplyWindowConfigIfNeeded(config);
            }

            windowController.Owner = this;

            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            if (firemakingSkill == null)
                firemakingSkill = GetComponent<FiremakingSkill>();
            if (questUi == null)
                questUi = QuestUI.Instance;
            petStorage = GetPetStorage();

            if (interactionHandler == null)
                interactionHandler = new InventoryInteractionHandler(this, Model, windowController);

            interactionHandler.UpdateServiceContext(new InventoryInteractionHandler.ServiceContext
            {
                Equipment = equipment,
                PlayerMover = playerMover,
                FiremakingSkill = firemakingSkill,
                QuestUi = questUi,
                PetStorage = petStorage
            });

            interactionHandler.RefreshControllerState();
            windowController.SetSelectedIndex(selectedIndex);
            windowController.RefreshAllSlots();
            bool shouldReopenForModal = allowModalReopen &&
                                        (BankOpen || (interactionHandler != null && interactionHandler.HasActiveShop));

            if (wasOpen || shouldReopenForModal)
                interactionHandler?.RequestOpen();
            else
                windowController.Hide();
            SubscribeControllerEvents();
            SubscribeModelEvents();
        }

        /// <summary>
        /// Rebuilds the inventory UI on a dedicated canvas when shared canvas usage is disabled.
        /// </summary>
        public void ForceDedicatedUiRoot()
        {
            // Bail out immediately if this inventory still expects to use the shared canvas.
            if (useSharedUIRoot)
                return;

            bool wasOpen = IsOpen;

            // Ensure backing arrays/fonts are prepared before we touch the UI.
            EnsureInitialized();

            ApplyWindowConfigIfNeeded(BuildWindowConfig());

            windowController?.ForceDedicatedCanvas();
            windowController?.SetSelectedIndex(selectedIndex);
            windowController?.RefreshAllSlots();

            if (wasOpen)
                interactionHandler?.RequestOpen();
            else
                windowController?.Hide();
        }

        /// <summary>
        /// Reapplies layout configuration so runtime property edits immediately rebuild the UI.
        /// </summary>
        public void RefreshWindowLayout()
        {
            EnsureInitialized();
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

        /// <summary>
        /// Applies the supplied configuration when the cached layout no longer matches runtime settings.
        /// </summary>
        private void ApplyWindowConfigIfNeeded(InventoryWindowController.WindowConfig config, bool force = false)
        {
            if (windowController == null)
                return;

            bool configChanged = !lastAppliedConfig.HasValue || !lastAppliedConfig.Value.Equals(config);
            bool sizeChanged = model != null && model.Size != lastAppliedModelSize;
            bool uiRootMissing = windowController.UiRoot == null;

            if (force || configChanged || sizeChanged || uiRootMissing)
            {
                bool reopen = IsOpen;
                windowController.ApplyConfig(config);
                lastAppliedConfig = config;
                lastAppliedModelSize = model != null ? model.Size : size;

                if (reopen)
                    interactionHandler?.RequestOpen();
            }
            else if (!force)
            {
                lastAppliedModelSize = model != null ? model.Size : size;
            }
        }

        private void OnWindowCloseRequested(InventoryWindowController controller)
        {
            if (controller != windowController)
                return;

            CloseUI();
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
            interactionHandler?.SellItem(slotIndex, quantity);
        }

        /// <summary>
        /// Sets the active shop context used for selling and tooltip information.
        /// </summary>
        public void SetShopContext(Shop shop)
        {
            interactionHandler?.SetShopContext(shop);
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
            SubscribeModelEvents();
            var data = SaveManager.Load<InventoryModel.InventorySaveData>(saveKey);
            Model.RestoreState(data);
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private void OnDestroy()
        {
            UnsubscribeControllerEvents();
            UnsubscribeModelEvents();
            interactionHandler?.Dispose();
        }

        private void Update()
        {
            interactionHandler?.Tick();
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

        private void SubscribeModelEvents()
        {
            if (model == null || modelEventsSubscribed)
                return;

            model.InventoryChanged += OnModelInventoryChanged;
            model.SlotChanged += OnModelSlotChanged;
            modelEventsSubscribed = true;
        }

        private void UnsubscribeModelEvents()
        {
            if (model == null || !modelEventsSubscribed)
                return;

            model.InventoryChanged -= OnModelInventoryChanged;
            model.SlotChanged -= OnModelSlotChanged;
            modelEventsSubscribed = false;
        }

        private void SubscribeControllerEvents()
        {
            if (windowController == null || controllerEventsSubscribed)
                return;

            windowController.CloseRequested += OnWindowCloseRequested;
            controllerEventsSubscribed = true;
        }

        private void UnsubscribeControllerEvents()
        {
            if (windowController == null || !controllerEventsSubscribed)
                return;

            windowController.CloseRequested -= OnWindowCloseRequested;
            controllerEventsSubscribed = false;
        }

        /// <summary>
        /// Determines whether the inventory can accommodate the specified item and quantity.
        /// Delegates to the underlying <see cref="InventoryModel"/> so stacking rules remain centralised.
        /// </summary>
        public bool CanAddItem(ItemData item, int quantity = 1)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            return model.CanAddItem(item, quantity);
        }

        /// <summary>
        /// Attempts to add an item to the inventory, stacking where possible.
        /// Returns true when the entire quantity was stored.
        /// </summary>
        public bool AddItem(ItemData item, int quantity = 1)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            return model.AddItem(item, quantity);
        }

        /// <summary>
        /// Removes up to <paramref name="count"/> of the requested item from the inventory.
        /// </summary>
        public bool RemoveItem(ItemData item, int count)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            return model.RemoveItem(item, count);
        }

        /// <summary>
        /// Removes a single instance of the item with the given identifier.
        /// </summary>
        public bool RemoveItem(string id)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            return model.RemoveItem(id);
        }

        /// <summary>
        /// Returns how many of the specified item are stored across all slots.
        /// </summary>
        public int GetItemCount(ItemData item)
        {
            EnsureModelInitialized();
            return model.GetItemCount(item);
        }

        /// <summary>
        /// Returns true when an item with the provided identifier exists in any slot.
        /// </summary>
        public bool HasItem(string id)
        {
            EnsureModelInitialized();
            return model.HasItem(id);
        }

        /// <summary>
        /// Removes a quantity directly from the slot without performing additional searches.
        /// </summary>
        public void RemoveFromSlot(int slotIndex, int quantity)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            model.RemoveFromSlot(slotIndex, quantity);
        }

        /// <summary>
        /// Splits a stack, moving <paramref name="quantity"/> items to a new slot when space is available.
        /// </summary>
        public void SplitStack(int slotIndex, int quantity)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            model.SplitStack(slotIndex, quantity);
        }

        /// <summary>
        /// Removes and returns the entry stored at the provided index.
        /// </summary>
        public InventoryEntry TakeEntry(int slotIndex)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            return model.TakeEntry(slotIndex);
        }

        /// <summary>
        /// Replaces the contents of a slot with the supplied entry when storage rules allow.
        /// </summary>
        public bool SetEntry(int slotIndex, InventoryEntry entry)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            return model.SetEntry(slotIndex, entry);
        }

        /// <summary>
        /// Swaps an item in place when the current slot matches <paramref name="oldItem"/>.
        /// </summary>
        public bool ReplaceItem(int slotIndex, ItemData oldItem, ItemData newItem, int newCount)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            return model.ReplaceItem(slotIndex, oldItem, newItem, newCount);
        }

        /// <summary>
        /// Attempts to combine two slots using the configured combination database.
        /// </summary>
        public bool CombineItems(int srcIndex, int dstIndex, out bool keepSelection)
        {
            EnsureModelInitialized();
            SubscribeModelEvents();
            return model.CombineItems(srcIndex, dstIndex, out keepSelection);
        }
    }
}
