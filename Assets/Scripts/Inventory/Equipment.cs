// Assets/Scripts/Inventory/Equipment.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Core.Save;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Inventory
{
    /// <summary>
    /// Handles equipping items into fixed slots and displays a simple UI
    /// similar to the Old School RuneScape equipment interface. The UI is
    /// generated at runtime and toggled with the "E" key.
    /// </summary>
    [DisallowMultipleComponent]
    public class Equipment : MonoBehaviour
    {
        [Tooltip("Size of each slot in pixels.")]
        public Vector2 slotSize = new(32f, 32f);

        [Tooltip("Spacing between slots in pixels.")]
        public Vector2 slotSpacing = new(4f, 4f);

        [Tooltip("Reference resolution for the UI Canvas.")]
        public Vector2 referenceResolution = new(640f, 360f);

        [Tooltip("Optional frame sprite for slots (9 sliced).")]
        public Sprite slotFrameSprite;

        [Tooltip("Color for empty slots or tint for the frame.")]
        public Color emptySlotColor = new(0f, 0f, 0f, 1f);

        [Tooltip("Background color of the window.")]
        public Color windowColor = new(0.15f, 0.15f, 0.15f, 0.95f);

        [Header("Slot Sprites")]
        public Sprite ammoSlotSprite;
        public Sprite bodySlotSprite;
        public Sprite capeSlotSprite;
        public Sprite feetSlotSprite;
        public Sprite glovesSlotSprite;
        public Sprite headSlotSprite;
        public Sprite amuletSlotSprite;
        public Sprite legsSlotSprite;
        public Sprite ringSlotSprite;
        public Sprite shieldSlotSprite;
        public Sprite weaponSlotSprite;

        [Header("Bonus Text Styles")]
        public Font combatHeaderFont;
        public Color combatHeaderColor = Color.white;
        public Font attackFont;
        public Color attackColor = Color.white;
        public Font strengthFont;
        public Color strengthColor = Color.white;
        public Font rangeFont;
        public Color rangeColor = Color.white;
        public Font magicFont;
        public Color magicColor = Color.white;
        public Font defenceHeaderFont;
        public Color defenceHeaderColor = Color.white;
        public Font meleeDefFont;
        public Color meleeDefColor = Color.white;
        public Font rangeDefFont;
        public Color rangeDefColor = Color.white;
        public Font magicDefFont;
        public Color magicDefColor = Color.white;
        public Font bonusHeaderFont;
        public Color bonusHeaderColor = Color.white;
        public Font attackSpeedFont;
        public Color attackSpeedColor = Color.white;

        private GameObject uiRoot;
        private Image[] slotImages;
        private Text[] slotCountTexts;
        private InventoryEntry[] equipped;
        private Inventory inventory;

        private Text attackBonusText;
        private Text strengthBonusText;
        private Text rangeBonusText;
        private Text magicBonusText;
        private Text meleeDefenceBonusText;
        private Text rangedDefenceBonusText;
        private Text magicDefenceBonusText;
        private Text attackSpeedText;

        public int TotalAttackBonus { get; private set; }
        public int TotalDefenceBonus { get; private set; }

        private static readonly System.Collections.Generic.Dictionary<int, EquipmentSlot> cellToSlot = new()
        {
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

        private void Awake()
        {
            inventory = GetComponent<Inventory>();
            equipped = new InventoryEntry[Enum.GetValues(typeof(EquipmentSlot)).Length - 1];
            CreateUI();
            Load();
            UpdateBonuses();
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            bool toggle = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
            toggle |= Input.GetKeyDown(KeyCode.E);
#else
            bool toggle = Input.GetKeyDown(KeyCode.E);
#endif
            if (toggle && uiRoot != null)
            {
                bool opening = !uiRoot.activeSelf;
                if (opening)
                {
                    var minimap = World.Minimap.Instance;
                    minimap?.CloseExpanded();
                }
                uiRoot.SetActive(!uiRoot.activeSelf);
            }
        }

        private int SlotIndex(EquipmentSlot slot) => (int)slot - 1;

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        public void CloseUI()
        {
            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        public InventoryEntry GetEquipped(EquipmentSlot slot)
        {
            int index = SlotIndex(slot);
            if (index < 0 || index >= equipped.Length)
                return default;
            return equipped[index];
        }

        /// <summary>
        /// Equip an entry into its designated slot. Returns true on success.
        /// </summary>
        public bool Equip(InventoryEntry entry)
        {
            var slot = entry.item != null ? entry.item.equipmentSlot : EquipmentSlot.None;
            if (slot == EquipmentSlot.None)
                return false;

            int index = SlotIndex(slot);
            if (index < 0 || index >= equipped.Length)
                return false;

            var current = equipped[index];
            if (current.item != null)
            {
                if (inventory != null && !inventory.AddItem(current.item, current.count))
                    return false;
            }

            equipped[index] = entry;
            UpdateSlotVisual(slot);
            UpdateBonuses();
            Save();
            return true;
        }

        /// <summary>
        /// Unequip the item in the given slot back into the inventory.
        /// </summary>
        public void Unequip(EquipmentSlot slot)
        {
            int index = SlotIndex(slot);
            if (index < 0 || index >= equipped.Length)
                return;

            var entry = equipped[index];
            if (entry.item == null)
                return;

            if (inventory != null && inventory.AddItem(entry.item, entry.count))
            {
                equipped[index].item = null;
                equipped[index].count = 0;
                UpdateSlotVisual(slot);
                UpdateBonuses();
                Save();
            }
        }

        private void UpdateSlotVisual(EquipmentSlot slot)
        {
            int index = SlotIndex(slot);
            if (index < 0 || index >= slotImages.Length)
                return;
            var img = slotImages[index];
            var text = slotCountTexts[index];
            var entry = equipped[index];
            if (entry.item != null)
            {
                img.sprite = entry.item.icon ? entry.item.icon : GetSlotSprite(slot);
                img.color = Color.white;
                img.type = Image.Type.Simple;
                text.text = entry.item.stackable && entry.count > 1 ? entry.count.ToString() : string.Empty;
            }
            else
            {
                img.sprite = GetSlotSprite(slot);
                img.type = img.sprite == slotFrameSprite && slotFrameSprite != null ? Image.Type.Sliced : Image.Type.Simple;
                img.color = emptySlotColor;
                text.text = string.Empty;
            }
        }

        private void UpdateBonuses()
        {
            int attack = 0, strength = 0, range = 0, magic = 0;
            int meleeDef = 0, rangeDef = 0, magicDef = 0;
            int attackSpeedTicks = 4;

            foreach (var entry in equipped)
            {
                if (entry.item == null)
                    continue;

                var stats = entry.item.combat;
                attack += stats.Attack;
                strength += stats.Strength;
                range += stats.Range;
                magic += stats.Magic;
                meleeDef += stats.MeleeDefence;
                rangeDef += stats.RangeDefence;
                magicDef += stats.MagicDefence;
                if (entry.item.equipmentSlot == EquipmentSlot.Weapon && stats.AttackSpeedTicks > 0)
                    attackSpeedTicks = stats.AttackSpeedTicks;
            }

            if (attackBonusText != null) attackBonusText.text = $"Attack = {attack}";
            if (strengthBonusText != null) strengthBonusText.text = $"Strength = {strength}";
            if (rangeBonusText != null) rangeBonusText.text = $"Range = {range}";
            if (magicBonusText != null) magicBonusText.text = $"Magic = {magic}";
            if (meleeDefenceBonusText != null) meleeDefenceBonusText.text = $"Melee = {meleeDef}";
            if (rangedDefenceBonusText != null) rangedDefenceBonusText.text = $"Range = {rangeDef}";
            if (magicDefenceBonusText != null) magicDefenceBonusText.text = $"Magic = {magicDef}";
            if (attackSpeedText != null) attackSpeedText.text = $"Attack = {attackSpeedTicks}";

            TotalAttackBonus = attack;
            TotalDefenceBonus = meleeDef + rangeDef + magicDef;
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

        private const string SaveKey = "EquipmentData";

        public void Save()
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

        public void Load()
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
                    equipped[i].item = ItemDatabase.GetItem(slot.id);
                    equipped[i].count = slot.count;
                }
                else
                {
                    equipped[i].item = null;
                    equipped[i].count = 0;
                }
                UpdateSlotVisual((EquipmentSlot)(i + 1));
            }

            UpdateBonuses();
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        private Sprite GetSlotSprite(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Arrow: return ammoSlotSprite;
                case EquipmentSlot.Body: return bodySlotSprite;
                case EquipmentSlot.Cape: return capeSlotSprite;
                case EquipmentSlot.Boots: return feetSlotSprite;
                case EquipmentSlot.Gloves: return glovesSlotSprite;
                case EquipmentSlot.Head: return headSlotSprite;
                case EquipmentSlot.Amulet: return amuletSlotSprite;
                case EquipmentSlot.Legs: return legsSlotSprite;
                case EquipmentSlot.Ring: return ringSlotSprite;
                case EquipmentSlot.Shield: return shieldSlotSprite;
                case EquipmentSlot.Weapon: return weaponSlotSprite;
                default: return slotFrameSprite;
            }
        }

        private void CreateUI()
        {
            uiRoot = new GameObject("EquipmentUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            uiRoot.transform.SetParent(null, false);
            DontDestroyOnLoad(uiRoot);

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) uiRoot.layer = uiLayer;

            var canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;

            var scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 0f;

            GameObject window = new GameObject("Window", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(uiRoot.transform, false);

            var windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.anchoredPosition = Vector2.zero;

            // Size to fit 3 columns x 5 rows
            var contentSize = new Vector2(slotSize.x * 3 + slotSpacing.x * 2,
                slotSize.y * 5 + slotSpacing.y * 4);
            float bonusWidth = 120f;
            windowRect.sizeDelta = new Vector2(contentSize.x + bonusWidth, contentSize.y) + new Vector2(16f, 16f);

            var windowImg = window.GetComponent<Image>();
            windowImg.color = windowColor;

            GameObject panel = new GameObject("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
            panel.transform.SetParent(window.transform, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(8f, 0f);
            rect.sizeDelta = contentSize;

            var grid = panel.GetComponent<GridLayoutGroup>();
            grid.cellSize = slotSize;
            grid.spacing = slotSpacing;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            slotImages = new Image[equipped.Length];
            slotCountTexts = new Text[equipped.Length];

            Font defaultFont = null;
            try
            {
                defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                try { defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch (ArgumentException) { }
            }

            for (int i = 0; i < 15; i++)
            {
                GameObject cell = new GameObject($"Cell{i}", typeof(RectTransform));
                cell.transform.SetParent(panel.transform, false);

                if (cellToSlot.TryGetValue(i, out var slot))
                {
                    var img = cell.AddComponent<Image>();
                    img.sprite = GetSlotSprite(slot);
                    if (img.sprite == slotFrameSprite && slotFrameSprite != null)
                        img.type = Image.Type.Sliced;
                    img.color = emptySlotColor;

                    GameObject countGO = new GameObject("Count", typeof(Text));
                    countGO.transform.SetParent(cell.transform, false);
                    var countText = countGO.GetComponent<Text>();
                    if (defaultFont != null)
                        countText.font = defaultFont;
                    countText.alignment = TextAnchor.LowerRight;
                    countText.raycastTarget = false;
                    countText.color = Color.white;
                    countText.text = string.Empty;
                    var countRect = countGO.GetComponent<RectTransform>();
                    countRect.anchorMin = Vector2.zero;
                    countRect.anchorMax = Vector2.one;
                    countRect.offsetMin = Vector2.zero;
                    countRect.offsetMax = Vector2.zero;

                    var slotComponent = cell.AddComponent<EquipmentSlotUI>();
                    slotComponent.equipment = this;
                    slotComponent.slot = slot;

                    slotImages[SlotIndex(slot)] = img;
                    slotCountTexts[SlotIndex(slot)] = countText;
                }
                else
                {
                    var placeholder = cell.AddComponent<Image>();
                    placeholder.color = Color.clear;
                }
            }

            GameObject bonusPanel = new GameObject("Bonuses", typeof(RectTransform));
            bonusPanel.transform.SetParent(window.transform, false);
            var bonusRect = bonusPanel.GetComponent<RectTransform>();
            bonusRect.anchorMin = new Vector2(1f, 0.5f);
            bonusRect.anchorMax = new Vector2(1f, 0.5f);
            bonusRect.pivot = new Vector2(1f, 0.5f);
            bonusRect.anchoredPosition = new Vector2(-8f, 0f);
            bonusRect.sizeDelta = new Vector2(bonusWidth, contentSize.y);

            float lineHeight = 14f;
            Text CreateText(string name, string txt, float y, Font font, Color color)
            {
                GameObject go = new GameObject(name, typeof(Text));
                go.transform.SetParent(bonusPanel.transform, false);
                var t = go.GetComponent<Text>();
                t.font = font != null ? font : defaultFont;
                t.alignment = TextAnchor.UpperCenter;
                t.raycastTarget = false;
                t.color = color;
                t.text = txt;
                var rt = t.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, y);
                rt.sizeDelta = new Vector2(bonusWidth, lineHeight);
                return t;
            }

            CreateText("CombatHeader", "Combat:", 0f, combatHeaderFont, combatHeaderColor);
            attackBonusText = CreateText("Attack", "Attack = 0", -lineHeight, attackFont, attackColor);
            strengthBonusText = CreateText("Strength", "Strength = 0", -2f * lineHeight, strengthFont, strengthColor);
            rangeBonusText = CreateText("Range", "Range = 0", -3f * lineHeight, rangeFont, rangeColor);
            magicBonusText = CreateText("Magic", "Magic = 0", -4f * lineHeight, magicFont, magicColor);
            CreateText("DefenceHeader", "Defence:", -5f * lineHeight, defenceHeaderFont, defenceHeaderColor);
            meleeDefenceBonusText = CreateText("MeleeDef", "Melee = 0", -6f * lineHeight, meleeDefFont, meleeDefColor);
            rangedDefenceBonusText = CreateText("RangeDef", "Range = 0", -7f * lineHeight, rangeDefFont, rangeDefColor);
            magicDefenceBonusText = CreateText("MagicDef", "Magic = 0", -8f * lineHeight, magicDefFont, magicDefColor);
            CreateText("BonusHeader", "Bonuses:", -9f * lineHeight, bonusHeaderFont, bonusHeaderColor);
            attackSpeedText = CreateText("AttackSpeed", "Attack = 0", -10f * lineHeight, attackSpeedFont, attackSpeedColor);
        }
    }
}

