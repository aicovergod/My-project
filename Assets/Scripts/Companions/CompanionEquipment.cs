using System;
using System.Collections.Generic;
using System.Text;
using Combat;
using Core.Save;
using Inventory;
using Skills;
using UI;
using UI.Chat;
using UI.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Companions
{
    /// <summary>
    /// Builds and manages a companion-specific equipment window that mirrors the player's OSRS-style layout.
    /// Handles UI lifecycle integration, persistence, and routing equip requests originating from the player inventory.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionEquipment : MonoBehaviour, IUIWindow
    {
        private const string SaveKey = "CompanionEquipmentData";
        private const float UiScale = 2f;

        /// <summary>Number of usable equipment slots (excludes <see cref="EquipmentSlot.None"/>).</summary>
        private static readonly int EquipmentSlotCount = Enum.GetValues(typeof(EquipmentSlot)).Length - 1;

        /// <summary>Mapping from grid indices to slot definitions so the generated UI mirrors the player layout.</summary>
        private static readonly Dictionary<int, EquipmentSlot> CellToSlot = new()
        {
            {0, EquipmentSlot.Charm},
            {1, EquipmentSlot.Head},
            {3, EquipmentSlot.Cape},
            {4, EquipmentSlot.Amulet},
            {5, EquipmentSlot.Arrow},
            {6, EquipmentSlot.Weapon},
            {7, EquipmentSlot.Body},
            {8, EquipmentSlot.Shield},
            {10, EquipmentSlot.Legs},
            {12, EquipmentSlot.Gloves},
            {13, EquipmentSlot.Boots},
            {14, EquipmentSlot.Ring}
        };

        private readonly InventoryEntry[] equipped = new InventoryEntry[EquipmentSlotCount];

        private CompanionInventory companionInventory;
        private SkillManager skillManager;

        private GameObject uiRoot;
        private GameObject tooltip;
        private Text tooltipNameText;
        private Text tooltipBonusText;

        private Image[] slotBackgroundImages;
        private Image[] slotItemImages;
        private Text[] slotCountTexts;
        private RectTransform[] slotRects;

        private Text attackBonusText;
        private Text meleeBonusText;
        private Text rangeAccuracyBonusText;
        private Text rangeStrengthBonusText;
        private Text magicBonusText;
        private Text meleeDefenceBonusText;
        private Text rangedDefenceBonusText;
        private Text magicDefenceBonusText;
        private Text maxHitText;

        private Vector2 scaledSlotSize;

        private Sprite slotFrameSprite;
        private Sprite ammoSlotSprite;
        private Sprite bodySlotSprite;
        private Sprite capeSlotSprite;
        private Sprite feetSlotSprite;
        private Sprite glovesSlotSprite;
        private Sprite headSlotSprite;
        private Sprite amuletSlotSprite;
        private Sprite legsSlotSprite;
        private Sprite ringSlotSprite;
        private Sprite shieldSlotSprite;
        private Sprite weaponSlotSprite;
        private Sprite charmSlotSprite;
        private Sprite emptySlotSprite;

        private Color windowColor = new(0.15f, 0.15f, 0.15f, 0.95f);
        private Color emptySlotColor = Color.black;
        private Color ammoDefaultColor = Color.white;

        private Vector2 slotSize = new(32f, 32f);
        private Vector2 slotSpacing = new(4f, 4f);
        private Vector2 referenceResolution = new(1024f, 768f);

        private bool registeredWithUiManager;
        private bool initialised;
        private bool isOpen;

        private Transform floatingTextAnchor;

        /// <summary>Raised whenever the window visibility toggles so menus and HUDs can refresh their labels.</summary>
        public event Action<bool> VisibilityChanged;

        /// <summary>Exposes whether the window is currently visible.</summary>
        public bool IsOpen => isOpen;

        private void Awake()
        {
            floatingTextAnchor = transform;
        }

        private void Update()
        {
            if (!initialised)
                return;

            if (!registeredWithUiManager)
                TryRegisterWithUiManager();
        }

        private void OnDestroy()
        {
            ForceClosed();

            if (registeredWithUiManager)
            {
                var uiManager = UIManager.Instance;
                if (uiManager != null)
                    uiManager.UnregisterWindow(this);
                registeredWithUiManager = false;
            }

            VisibilityChanged = null;
        }

        /// <summary>
        /// Configures the equipment system, cloning UI styling from the player window and loading persisted state.
        /// </summary>
        /// <param name="companionInventory">Inventory wrapper used for overflow when the player bag is full.</param>
        /// <param name="skills">Skill manager used to validate equip requirements and compute bonus text.</param>
        public void Initialise(CompanionInventory companionInventory, SkillManager skills)
        {
            this.companionInventory = companionInventory;
            skillManager = skills;

            if (initialised)
            {
                UpdateBonuses();
                return;
            }

            CopyStylingFromPlayerEquipment();
            BuildInterface();
            Load();
            UpdateBonuses();

            if (uiRoot != null)
                uiRoot.SetActive(false);

            TryRegisterWithUiManager();
            initialised = true;

            if (CompanionManager.EnableDebugLogging)
                Debug.Log("[Companion Equipment] Initialised equipment UI and loaded saved state.", this);
        }

        /// <summary>
        /// Toggles the equipment window. Returns true when the window ends up open.
        /// </summary>
        public bool ToggleEquipment()
        {
            if (!initialised || uiRoot == null)
                return false;

            if (isOpen)
            {
                ForceClosed();
                return false;
            }

            var uiManager = UIManager.Instance;
            if (uiManager != null && !uiManager.TryOpenWindow(this))
                return false;

            InterfaceTabMutexUtility.CloseAllTabWindowsExcept(this);
            uiRoot.SetActive(true);
            isOpen = true;
            VisibilityChanged?.Invoke(true);

            if (CompanionManager.EnableDebugLogging)
                Debug.Log("[Companion Equipment] Equipment window opened.", this);

            return true;
        }

        /// <summary>
        /// Closes the equipment window without toggling state, raising <see cref="VisibilityChanged"/> when applicable.
        /// </summary>
        public void ForceClosed()
        {
            if (uiRoot != null)
            {
                uiRoot.SetActive(false);
                HideTooltip();
            }

            if (!isOpen)
                return;

            isOpen = false;
            VisibilityChanged?.Invoke(false);

            if (CompanionManager.EnableDebugLogging)
                Debug.Log("[Companion Equipment] Equipment window closed.", this);
        }

        /// <inheritdoc />
        public void Close()
        {
            ForceClosed();
        }

        /// <summary>
        /// Retrieves the entry equipped in the requested slot.
        /// </summary>
        public InventoryEntry GetEquipped(EquipmentSlot slot)
        {
            int index = SlotIndex(slot);
            if (index < 0 || index >= equipped.Length)
                return default;
            return equipped[index];
        }

        /// <summary>
        /// Attempts to equip the provided entry using the player inventory as the primary overflow destination.
        /// </summary>
        /// <param name="entry">Inventory entry removed from the player bag.</param>
        /// <param name="playerInventory">Player inventory that supplied the item.</param>
        /// <returns>True when the equip succeeds.</returns>
        public bool TryEquipFromPlayerInventory(InventoryEntry entry, Inventory.Inventory playerInventory)
        {
            if (!initialised || entry.item == null)
                return false;

            if (!IsOpen)
                return false;

            var item = entry.item;
            var slot = item.equipmentSlot;
            if (slot == EquipmentSlot.None)
                return false;

            if (!ValidateSkillRequirements(item, playerInventory, entry))
                return false;

            int index = SlotIndex(slot);
            if (index < 0 || index >= equipped.Length)
            {
                RestoreEntryToInventory(playerInventory, entry);
                return false;
            }

            if (!HandleMutuallyExclusiveSlots(slot, playerInventory, entry))
            {
                RestoreEntryToInventory(playerInventory, entry);
                return false;
            }

            var current = equipped[index];
            if (current.item != null && current.item == item && item.stackable)
            {
                if (!MergeStackableEquip(entry, playerInventory, slot, index))
                {
                    RestoreEntryToInventory(playerInventory, entry);
                    return false;
                }

                return true;
            }

            if (current.item != null)
            {
                if (!TryReturnEntry(current, playerInventory, out _))
                {
                    ShowInventoryFullFloatingText();
                    RestoreEntryToInventory(playerInventory, entry);
                    return false;
                }

                ItemUseResolver.NotifyItemUsed(gameObject, current.item, ItemUseType.Unequipped);
            }

            equipped[index] = entry;
            UpdateSlotVisual(slot);
            UpdateBonuses();
            Save();
            ItemUseResolver.NotifyItemUsed(gameObject, item, ItemUseType.Equipped);

            if (CompanionManager.EnableDebugLogging)
            {
                Debug.Log($"[Companion Equipment] Equipped {item.itemName} into {slot}.", this);
            }

            return true;
        }

        /// <summary>
        /// Unequips the requested slot into the provided inventory, falling back to the companion bag when needed.
        /// </summary>
        public void UnequipToInventory(EquipmentSlot slot, Inventory.Inventory destinationInventory)
        {
            int index = SlotIndex(slot);
            if (index < 0 || index >= equipped.Length)
                return;

            var entry = equipped[index];
            if (entry.item == null)
                return;

            if (!TryReturnEntry(entry, destinationInventory, out _))
            {
                ShowInventoryFullFloatingText();
                return;
            }

            equipped[index] = default;
            UpdateSlotVisual(slot);
            UpdateBonuses();
            Save();
            ItemUseResolver.NotifyItemUsed(gameObject, entry.item, ItemUseType.Unequipped);

            if (CompanionManager.EnableDebugLogging)
            {
                Debug.Log($"[Companion Equipment] Unequipped {entry.item.itemName} from {slot}.", this);
            }
        }

        /// <summary>
        /// Displays the tooltip for the specified slot when the pointer hovers over it.
        /// </summary>
        private void ShowTooltip(EquipmentSlot slot, RectTransform slotRect)
        {
            var entry = GetEquipped(slot);
            var item = entry.item;
            if (item == null || tooltip == null || tooltipNameText == null || tooltipBonusText == null)
            {
                HideTooltip();
                return;
            }

            string name = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            tooltipNameText.text = name;
            var sb = new StringBuilder();

            void AppendBonus(float value, string label)
            {
                if (Mathf.Abs(value) > Mathf.Epsilon)
                {
                    if (sb.Length > 0)
                        sb.AppendLine();
                    sb.Append($"+{value * 100f:0.##}% {label}");
                }
            }

            AppendBonus(item.bycatchChanceBonus, "Bycatch Chance");
            AppendBonus(item.fishingXpBonusMultiplier, "Fishing XP");
            AppendBonus(item.woodcuttingXpBonusMultiplier, "Woodcutting XP");
            AppendBonus(item.miningXpBonusMultiplier, "Mining XP");
            AppendBonus(item.cookingXpBonusMultiplier, "Cooking XP");
            AppendBonus(item.firemakingXpBonusMultiplier, "Firemaking XP");

            var tooltipLines = item.EquipmentTooltipLines;
            if (tooltipLines != null && tooltipLines.Count > 0)
            {
                for (int i = 0; i < tooltipLines.Count; i++)
                {
                    string line = tooltipLines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(line.Trim());
                }
            }

            tooltipBonusText.text = sb.ToString();

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

            float tooltipOffsetX = scaledSlotSize.x > 0f ? scaledSlotSize.x : slotSize.x;
            Vector3 pos = slotRect.position + new Vector3(tooltipOffsetX, 0f, 0f);
            Vector3[] corners = new Vector3[4];
            tooltipRect.GetWorldCorners(corners);
            float width = corners[2].x - corners[0].x;
            float height = corners[2].y - corners[0].y;
            pos.x = Mathf.Min(pos.x, Screen.width - width);
            pos.y = Mathf.Max(pos.y, height);
            tooltipRect.position = pos;

            tooltip.SetActive(true);
        }

        /// <summary>
        /// Hides the tooltip when the pointer exits a slot or the window closes.
        /// </summary>
        private void HideTooltip()
        {
            if (tooltip != null)
                tooltip.SetActive(false);
        }

        private void CopyStylingFromPlayerEquipment()
        {
            var playerEquipment = FindObjectOfType<Inventory.Equipment>();
            if (playerEquipment == null)
            {
                emptySlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Empty_Slot");
                charmSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Charm_Slot");
                ammoSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Ammo_Slot");
                bodySlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Body_Slot");
                capeSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Cape_Slot");
                feetSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Feet_Slot");
                glovesSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Gloves_Slot");
                headSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Head_Slot");
                amuletSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Amulet_Slot");
                legsSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Legs_Slot");
                ringSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Ring_Slot");
                shieldSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Shield_Slot");
                weaponSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Weapon_Slot");
                return;
            }

            slotSize = playerEquipment.slotSize;
            slotSpacing = playerEquipment.slotSpacing;
            referenceResolution = playerEquipment.referenceResolution;
            slotFrameSprite = playerEquipment.slotFrameSprite;
            emptySlotColor = playerEquipment.emptySlotColor;
            windowColor = playerEquipment.windowColor;
            ammoSlotSprite = playerEquipment.ammoSlotSprite;
            bodySlotSprite = playerEquipment.bodySlotSprite;
            capeSlotSprite = playerEquipment.capeSlotSprite;
            feetSlotSprite = playerEquipment.feetSlotSprite;
            glovesSlotSprite = playerEquipment.glovesSlotSprite;
            headSlotSprite = playerEquipment.headSlotSprite;
            amuletSlotSprite = playerEquipment.amuletSlotSprite;
            legsSlotSprite = playerEquipment.legsSlotSprite;
            ringSlotSprite = playerEquipment.ringSlotSprite;
            shieldSlotSprite = playerEquipment.shieldSlotSprite;
            weaponSlotSprite = playerEquipment.weaponSlotSprite;
            charmSlotSprite = playerEquipment.charmSlotSprite;
            emptySlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Empty_Slot");
        }

        private void BuildInterface()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            var overlay = OverlayCanvasFactory.CreateOverlayCanvas(
                "CompanionEquipmentUI",
                referenceResolution,
                dontDestroyOnLoad: true,
                matchWidthOrHeight: 0f,
                explicitLayer: uiLayer >= 0 ? uiLayer : (int?)null,
                assignToUiLayer: uiLayer < 0);

            uiRoot = overlay.Root;

            GameObject window = new("Window", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(uiRoot.transform, false);

            var windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.anchoredPosition = Vector2.zero;

            Vector2 scaledSlotSpacing = slotSpacing * UiScale;
            scaledSlotSize = slotSize * UiScale;
            var contentSize = new Vector2(scaledSlotSize.x * 3f + scaledSlotSpacing.x * 2f,
                scaledSlotSize.y * 5f + scaledSlotSpacing.y * 4f);
            float scaledBonusWidth = 120f * UiScale;
            Vector2 scaledWindowPadding = new(16f * UiScale, 16f * UiScale);
            float scaledPanelOffset = 8f * UiScale;
            windowRect.sizeDelta = new Vector2(contentSize.x + scaledBonusWidth, contentSize.y) + scaledWindowPadding;

            var windowImg = window.GetComponent<Image>();
            windowImg.color = windowColor;

            GameObject panel = new("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
            panel.transform.SetParent(window.transform, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(scaledPanelOffset, 0f);
            rect.sizeDelta = contentSize;

            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = scaledSlotSize;
            grid.spacing = scaledSlotSpacing;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            slotBackgroundImages = new Image[equipped.Length];
            slotItemImages = new Image[equipped.Length];
            slotCountTexts = new Text[equipped.Length];
            slotRects = new RectTransform[equipped.Length];

            Font defaultFont = LegacyFontProvider.GetLegacyFont();
            float scaledLineHeight = 14f * UiScale;
            int scaledFontSize = Mathf.RoundToInt(scaledLineHeight);

            for (int i = 0; i < 15; i++)
            {
                GameObject cell = new($"Cell{i}", typeof(RectTransform));
                cell.transform.SetParent(panel.transform, false);

                if (CellToSlot.TryGetValue(i, out var slot))
                {
                    var img = cell.AddComponent<Image>();
                    img.sprite = GetSlotSprite(slot);
                    if (img.sprite == slotFrameSprite && slotFrameSprite != null)
                        img.type = Image.Type.Sliced;
                    img.color = emptySlotColor;

                    GameObject itemGO = new("Item", typeof(Image));
                    itemGO.transform.SetParent(cell.transform, false);
                    var itemImg = itemGO.GetComponent<Image>();
                    itemImg.raycastTarget = false;
                    var itemRect = itemGO.GetComponent<RectTransform>();
                    itemRect.anchorMin = Vector2.zero;
                    itemRect.anchorMax = Vector2.one;
                    itemRect.offsetMin = Vector2.zero;
                    itemRect.offsetMax = Vector2.zero;
                    itemImg.color = Color.clear;

                    GameObject countGO = new("Count", typeof(Text));
                    countGO.transform.SetParent(cell.transform, false);
                    var countText = countGO.GetComponent<Text>();
                    if (defaultFont != null)
                        countText.font = defaultFont;
                    countText.fontSize = scaledFontSize;
                    countText.alignment = TextAnchor.LowerRight;
                    countText.raycastTarget = false;
                    countText.color = Color.white;
                    countText.text = string.Empty;
                    if (slot == EquipmentSlot.Arrow)
                        ammoDefaultColor = countText.color;
                    var countRect = countGO.GetComponent<RectTransform>();
                    countRect.anchorMin = Vector2.zero;
                    countRect.anchorMax = Vector2.one;
                    countRect.offsetMin = Vector2.zero;
                    countRect.offsetMax = Vector2.zero;

                    slotBackgroundImages[SlotIndex(slot)] = img;
                    slotItemImages[SlotIndex(slot)] = itemImg;
                    slotCountTexts[SlotIndex(slot)] = countText;
                    slotRects[SlotIndex(slot)] = cell.GetComponent<RectTransform>();

                    RegisterSlotEvents(cell, slot, slotRects[SlotIndex(slot)]);
                }
                else
                {
                    var placeholder = cell.AddComponent<Image>();
                    placeholder.color = Color.clear;
                }
            }

            GameObject bonusPanel = new("Bonuses", typeof(RectTransform));
            bonusPanel.transform.SetParent(window.transform, false);
            var bonusRect = bonusPanel.GetComponent<RectTransform>();
            bonusRect.anchorMin = new Vector2(1f, 0.5f);
            bonusRect.anchorMax = new Vector2(1f, 0.5f);
            bonusRect.pivot = new Vector2(1f, 0.5f);
            bonusRect.anchoredPosition = new Vector2(-scaledPanelOffset, 0f);
            bonusRect.sizeDelta = new Vector2(scaledBonusWidth, contentSize.y);

            Text CreateText(Transform parent, string name, string txt, float y, Color color)
            {
                GameObject go = new(name, typeof(Text));
                go.transform.SetParent(parent, false);
                var t = go.GetComponent<Text>();
                t.font = defaultFont;
                t.fontSize = scaledFontSize;
                t.alignment = TextAnchor.UpperCenter;
                t.raycastTarget = false;
                t.color = color;
                t.text = txt;
                var rt = t.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, y);
                rt.sizeDelta = new Vector2(scaledBonusWidth, scaledLineHeight);
                return t;
            }

            float line = 0f;
            CreateText(bonusPanel.transform, "CombatHeader", "Combat:", line, Color.white);
            attackBonusText = CreateText(bonusPanel.transform, "Attack", "Attack = 0", line -= scaledLineHeight, Color.white);
            rangeAccuracyBonusText = CreateText(bonusPanel.transform, "RangeAccuracy", "Ranged = 0", line -= scaledLineHeight, Color.white);
            magicBonusText = CreateText(bonusPanel.transform, "Magic", "Magic = 0", line -= scaledLineHeight, Color.white);
            CreateText(bonusPanel.transform, "DefenceHeader", "Defence:", line -= scaledLineHeight, Color.white);
            meleeDefenceBonusText = CreateText(bonusPanel.transform, "MeleeDef", "Melee = 0", line -= scaledLineHeight, Color.white);
            rangedDefenceBonusText = CreateText(bonusPanel.transform, "RangeDef", "Range = 0", line -= scaledLineHeight, Color.white);
            magicDefenceBonusText = CreateText(bonusPanel.transform, "MagicDef", "Magic = 0", line -= scaledLineHeight, Color.white);
            CreateText(bonusPanel.transform, "BonusesHeader", "Bonuses:", line -= scaledLineHeight * 2f, Color.white);
            meleeBonusText = CreateText(bonusPanel.transform, "Melee", "Melee = 0", line -= scaledLineHeight, Color.white);
            rangeStrengthBonusText = CreateText(bonusPanel.transform, "RangeStrength", "Ranged = 0", line -= scaledLineHeight, Color.white);
            CreateText(bonusPanel.transform, "MaxHitHeader", "Max Hit:", line -= scaledLineHeight * 2f, Color.white);
            maxHitText = CreateText(bonusPanel.transform, "MaxHit", "Total = 0", line -= scaledLineHeight, Color.white);

            tooltip = new GameObject("Tooltip", typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            tooltip.transform.SetParent(uiRoot.transform, false);

            var tooltipCanvas = tooltip.AddComponent<Canvas>();
            tooltipCanvas.overrideSorting = true;
            tooltipCanvas.sortingOrder = 1000;
            tooltip.AddComponent<GraphicRaycaster>();

            var bg = tooltip.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.75f);
            bg.raycastTarget = false;

            var layout = tooltip.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            int tooltipPadding = Mathf.RoundToInt(4f * UiScale);
            layout.padding = new RectOffset(tooltipPadding, tooltipPadding, tooltipPadding, tooltipPadding);
            layout.spacing = 2f * UiScale;

            var fitter = tooltip.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var nameGO = new GameObject("Name", typeof(Text));
            nameGO.transform.SetParent(tooltip.transform, false);
            tooltipNameText = nameGO.GetComponent<Text>();
            tooltipNameText.font = defaultFont;
            tooltipNameText.fontSize = scaledFontSize;
            tooltipNameText.alignment = TextAnchor.UpperLeft;
            tooltipNameText.color = Color.white;
            tooltipNameText.raycastTarget = false;
            tooltipNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipNameText.verticalOverflow = VerticalWrapMode.Overflow;

            var bonusGO = new GameObject("Bonus", typeof(Text));
            bonusGO.transform.SetParent(tooltip.transform, false);
            tooltipBonusText = bonusGO.GetComponent<Text>();
            tooltipBonusText.font = defaultFont;
            tooltipBonusText.fontSize = scaledFontSize;
            tooltipBonusText.alignment = TextAnchor.UpperLeft;
            tooltipBonusText.color = Color.white;
            tooltipBonusText.raycastTarget = false;
            tooltipBonusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipBonusText.verticalOverflow = VerticalWrapMode.Overflow;

            tooltip.SetActive(false);
        }

        private void RegisterSlotEvents(GameObject cell, EquipmentSlot slot, RectTransform rect)
        {
            var trigger = cell.AddComponent<EventTrigger>();
            trigger.triggers ??= new List<EventTrigger.Entry>();

            void AddEntry(EventTriggerType type, Action<BaseEventData> callback)
            {
                var entry = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(evt => callback(evt));
                trigger.triggers.Add(entry);
            }

            AddEntry(EventTriggerType.PointerEnter, data =>
            {
                ShowTooltip(slot, rect);
            });

            AddEntry(EventTriggerType.PointerExit, data =>
            {
                HideTooltip();
            });

            AddEntry(EventTriggerType.PointerClick, data =>
            {
                if (data is PointerEventData pointer && pointer.button == PointerEventData.InputButton.Left)
                {
                    UnequipToInventory(slot, companionInventory != null ? companionInventory.InventoryComponent : null);
                    HideTooltip();
                }
            });
        }

        private bool ValidateSkillRequirements(ItemData item, Inventory.Inventory playerInventory, InventoryEntry entry)
        {
            if (item.skillRequirements == null || item.skillRequirements.Length == 0)
                return true;

            if (skillManager == null)
                return true;

            List<SkillRequirement> unmet = null;
            for (int i = 0; i < item.skillRequirements.Length; i++)
            {
                var requirement = item.skillRequirements[i];
                int level = skillManager.GetLevel(requirement.skill);
                if (level < requirement.level)
                {
                    unmet ??= new List<SkillRequirement>();
                    unmet.Add(requirement);
                }
            }

            if (unmet == null || unmet.Count == 0)
                return true;

            RestoreEntryToInventory(playerInventory, entry);

            var chat = ChatService.Instance;
            if (chat != null)
            {
                string companionName = CompanionManager.GetCompanionDisplayName();
                string message = unmet.Count == 1
                    ? $"{companionName}:I don't have the required skill to equip that"
                    : $"{companionName}:I don't have the required skills to equip that";
                chat.PublishCompanionMessage(companionName, message);
            }

            if (CompanionManager.EnableDebugLogging)
            {
                Debug.LogWarning($"[Companion Equipment] Failed to equip {item.itemName} because skill requirements were unmet.", this);
            }

            return false;
        }

        private bool HandleMutuallyExclusiveSlots(EquipmentSlot slot, Inventory.Inventory playerInventory, InventoryEntry incoming)
        {
            if (slot == EquipmentSlot.Weapon && incoming.item != null && incoming.item.isTwoHanded)
            {
                int index = SlotIndex(EquipmentSlot.Shield);
                if (index >= 0 && index < equipped.Length)
                {
                    var shieldEntry = equipped[index];
                    if (shieldEntry.item != null)
                    {
                        if (!TryReturnEntry(shieldEntry, playerInventory, out _))
                        {
                            ShowInventoryFullFloatingText();
                            return false;
                        }

                        equipped[index] = default;
                        UpdateSlotVisual(EquipmentSlot.Shield);
                        ItemUseResolver.NotifyItemUsed(gameObject, shieldEntry.item, ItemUseType.Unequipped);
                    }
                }
            }
            else if (slot == EquipmentSlot.Shield)
            {
                int index = SlotIndex(EquipmentSlot.Weapon);
                if (index >= 0 && index < equipped.Length)
                {
                    var weaponEntry = equipped[index];
                    if (weaponEntry.item != null && weaponEntry.item.isTwoHanded)
                    {
                        if (!TryReturnEntry(weaponEntry, playerInventory, out _))
                        {
                            ShowInventoryFullFloatingText();
                            return false;
                        }

                        equipped[index] = default;
                        UpdateSlotVisual(EquipmentSlot.Weapon);
                        ItemUseResolver.NotifyItemUsed(gameObject, weaponEntry.item, ItemUseType.Unequipped);
                    }
                }
            }

            return true;
        }

        private bool MergeStackableEquip(InventoryEntry entry, Inventory.Inventory playerInventory, EquipmentSlot slot, int index)
        {
            var current = equipped[index];
            int maxStack = current.item.MaxStack;
            int availableSpace = Mathf.Max(0, maxStack - current.count);
            if (availableSpace <= 0)
            {
                ShowInventoryFullFloatingText("You cannot equip any more of that.");
                return false;
            }

            int transferAmount = Mathf.Min(entry.count, availableSpace);
            if (transferAmount <= 0)
            {
                ShowInventoryFullFloatingText("You cannot equip any more of that.");
                return false;
            }

            int overflow = entry.count - transferAmount;
            if (overflow > 0)
            {
                var overflowEntry = new InventoryEntry { item = entry.item, count = overflow };
                if (!TryReturnEntry(overflowEntry, playerInventory, out _))
                {
                    ShowInventoryFullFloatingText();
                    return false;
                }
            }

            current.count += transferAmount;
            equipped[index] = current;
            UpdateSlotVisual(slot);
            UpdateBonuses();
            Save();
            ItemUseResolver.NotifyItemUsed(gameObject, entry.item, ItemUseType.Equipped);
            return true;
        }

        private bool TryReturnEntry(InventoryEntry entry, Inventory.Inventory primary, out bool storedInPrimary)
        {
            storedInPrimary = false;
            if (entry.item == null || entry.count <= 0)
                return true;

            if (primary != null)
            {
                if (primary.CanAddItem(entry.item, entry.count) && primary.AddItem(entry.item, entry.count))
                {
                    storedInPrimary = true;
                    return true;
                }
            }

            var companionBag = companionInventory != null ? companionInventory.InventoryComponent : null;
            if (companionBag != null)
            {
                if (companionBag.CanAddItem(entry.item, entry.count) && companionBag.AddItem(entry.item, entry.count))
                    return true;
            }

            return false;
        }

        private void RestoreEntryToInventory(Inventory.Inventory playerInventory, InventoryEntry entry)
        {
            if (entry.item == null || entry.count <= 0)
                return;

            if (playerInventory != null)
                playerInventory.AddItem(entry.item, entry.count);
        }

        private void UpdateSlotVisual(EquipmentSlot slot)
        {
            int index = SlotIndex(slot);
            if (slotBackgroundImages == null || index < 0 || index >= slotBackgroundImages.Length)
                return;

            var bg = slotBackgroundImages[index];
            var itemImg = slotItemImages[index];
            var text = slotCountTexts[index];
            var entry = equipped[index];
            if (entry.item != null)
            {
                if (bg != null)
                {
                    bg.sprite = emptySlotSprite != null ? emptySlotSprite : GetSlotSprite(slot);
                    bg.type = Image.Type.Simple;
                    bg.color = Color.white;
                }
                if (itemImg != null)
                {
                    Sprite icon = entry.item.GetIconForCount(entry.count);
                    itemImg.sprite = icon;
                    itemImg.color = icon != null ? Color.white : Color.clear;
                }
                if (text != null)
                    text.text = entry.item.stackable && entry.count > 1 ? entry.count.ToString() : string.Empty;
            }
            else
            {
                if (bg != null)
                {
                    bg.sprite = GetSlotSprite(slot);
                    bg.type = bg.sprite == slotFrameSprite && slotFrameSprite != null ? Image.Type.Sliced : Image.Type.Simple;
                    bg.color = emptySlotColor;
                }
                if (itemImg != null)
                {
                    itemImg.sprite = null;
                    itemImg.color = Color.clear;
                }
                if (text != null)
                    text.text = string.Empty;
            }
        }

        private void UpdateBonuses()
        {
            int attack = 0;
            int meleeStrength = 0;
            int rangeAccuracy = 0;
            int rangeStrength = 0;
            int magic = 0;
            int meleeDef = 0, rangeDef = 0, magicDef = 0;

            foreach (var entry in equipped)
            {
                if (entry.item == null)
                    continue;

                var stats = entry.item.combat;
                attack += stats.Attack;
                meleeStrength += stats.Strength;
                rangeAccuracy += stats.Range;
                rangeStrength += stats.RangeStrength;
                magic += stats.Magic;
                meleeDef += stats.MeleeDefence;
                rangeDef += stats.RangeDefence;
                magicDef += stats.MagicDefence;
            }

            if (attackBonusText != null) attackBonusText.text = $"Attack = {attack}";
            if (rangeAccuracyBonusText != null) rangeAccuracyBonusText.text = $"Ranged = {rangeAccuracy}";
            if (meleeBonusText != null) meleeBonusText.text = $"Melee = {meleeStrength}";

            int rangedLevel = skillManager != null ? skillManager.GetLevel(SkillType.Ranged) : 1;
            int rangedStyleBonus = CombatMath.GetEffectiveRangedStrength(rangedLevel, CombatStyle.Accurate) - (rangedLevel + 8);
            if (rangeStrengthBonusText != null)
            {
                string suffix = rangedStyleBonus > 0 ? $" (+{rangedStyleBonus} style)" : string.Empty;
                rangeStrengthBonusText.text = $"Ranged = {rangeStrength}{suffix}";
            }

            if (magicBonusText != null) magicBonusText.text = $"Magic = {magic}";

            int strengthLevel = skillManager != null ? skillManager.GetLevel(SkillType.Strength) : 1;
            int effStr = CombatMath.GetEffectiveStrength(strengthLevel, CombatStyle.Accurate);
            int maxHit = CombatMath.GetMaxHit(effStr, meleeStrength);
            if (maxHitText != null) maxHitText.text = $"Total = {maxHit}";

            int defenceLevel = skillManager != null ? skillManager.GetLevel(SkillType.Defence) : 1;
            int defenceStyleBonus = CombatMath.GetEffectiveDefence(defenceLevel, CombatStyle.Accurate) - (defenceLevel + 8);
            string defenceSuffix = defenceStyleBonus > 0 ? $" (+{defenceStyleBonus} style)" : string.Empty;

            if (meleeDefenceBonusText != null) meleeDefenceBonusText.text = $"Melee = {meleeDef}{defenceSuffix}";
            if (rangedDefenceBonusText != null) rangedDefenceBonusText.text = $"Range = {rangeDef}{defenceSuffix}";
            if (magicDefenceBonusText != null) magicDefenceBonusText.text = $"Magic = {magicDef}{defenceSuffix}";
        }

        private void ShowInventoryFullFloatingText(string message = "Your inventory is full")
        {
            var anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
            FloatingText.Show(message, anchor.position);
        }

        private int SlotIndex(EquipmentSlot slot) => (int)slot - 1;

        private Sprite GetSlotSprite(EquipmentSlot slot)
        {
            return slot switch
            {
                EquipmentSlot.Arrow => ammoSlotSprite,
                EquipmentSlot.Body => bodySlotSprite,
                EquipmentSlot.Cape => capeSlotSprite,
                EquipmentSlot.Boots => feetSlotSprite,
                EquipmentSlot.Gloves => glovesSlotSprite,
                EquipmentSlot.Head => headSlotSprite,
                EquipmentSlot.Amulet => amuletSlotSprite,
                EquipmentSlot.Legs => legsSlotSprite,
                EquipmentSlot.Ring => ringSlotSprite,
                EquipmentSlot.Shield => shieldSlotSprite,
                EquipmentSlot.Weapon => weaponSlotSprite,
                EquipmentSlot.Charm => charmSlotSprite,
                _ => slotFrameSprite
            };
        }

        private void TryRegisterWithUiManager()
        {
            if (registeredWithUiManager)
                return;

            var uiManager = UIManager.Instance;
            if (uiManager == null)
                return;

            uiManager.RegisterWindow(this);
            registeredWithUiManager = true;
        }

        private void Save()
        {
            var data = new EquipmentSaveData
            {
                slots = new SlotData[equipped.Length]
            };

            for (int i = 0; i < equipped.Length; i++)
            {
                var entry = equipped[i];
                data.slots[i] = new SlotData
                {
                    id = entry.item != null ? entry.item.id : string.Empty,
                    count = entry.item != null ? entry.count : 0
                };
            }

            SaveManager.Save(SaveKey, data);
        }

        private void Load()
        {
            var data = SaveManager.Load<EquipmentSaveData>(SaveKey);
            if (data?.slots == null)
                return;

            int len = Mathf.Min(equipped.Length, data.slots.Length);
            for (int i = 0; i < len; i++)
            {
                var slot = data.slots[i];
                if (!string.IsNullOrEmpty(slot.id))
                {
                    var resolvedItem = ItemDatabase.GetItem(slot.id);
                    equipped[i].item = resolvedItem;
                    equipped[i].count = resolvedItem != null ? slot.count : 0;
                    if (resolvedItem == null && CompanionManager.EnableDebugLogging)
                    {
                        Debug.LogWarning($"[Companion Equipment] Saved item '{slot.id}' missing from database. Clearing slot {(EquipmentSlot)(i + 1)}.");
                    }
                }
                else
                {
                    equipped[i].item = null;
                    equipped[i].count = 0;
                }

                UpdateSlotVisual((EquipmentSlot)(i + 1));
            }
        }

        [Serializable]
        private class EquipmentSaveData
        {
            public SlotData[] slots;
        }

        [Serializable]
        private class SlotData
        {
            public string id;
            public int count;
        }
    }
}
