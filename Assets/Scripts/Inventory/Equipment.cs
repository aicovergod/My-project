// Assets/Scripts/Inventory/Equipment.cs
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Core.Save;
using Skills;
using Beastmaster;
using Pets;
using Combat;
using Player;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Quests;
using UI;
using Object = UnityEngine.Object;

namespace Inventory
{
    /// <summary>
    /// Handles equipping items into fixed slots and displays a simple UI
    /// similar to the Old School RuneScape equipment interface. The UI is
    /// generated at runtime and toggled with the "E" key.
    /// </summary>
    [DisallowMultipleComponent]
    public class Equipment : MonoBehaviour, IUIWindow
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
        public Sprite charmSlotSprite;

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
        public Font maxHitHeaderFont;
        public Color maxHitHeaderColor = Color.white;
        public Font maxHitFont;
        public Color maxHitColor = Color.white;
        public Font defenceHeaderFont;
        public Color defenceHeaderColor = Color.white;
        public Font meleeDefFont;
        public Color meleeDefColor = Color.white;
        public Font rangeDefFont;
        public Color rangeDefColor = Color.white;
        public Font magicDefFont;
        public Color magicDefColor = Color.white;

        [Header("Pet Text Styles")]
        public Font petHeaderFont;
        public Color petHeaderColor = Color.white;
        public Font petAttackLevelFont;
        public Color petAttackLevelColor = Color.white;
        public Font petStrengthLevelFont;
        public Color petStrengthLevelColor = Color.white;
        public Font petAttackSpeedFont;
        public Color petAttackSpeedColor = Color.white;
        public Font petMaxHitFont;
        public Color petMaxHitColor = Color.white;

        [Header("Tooltip")]
        public Font tooltipFont;
        public Color tooltipColor = Color.white;

        private GameObject uiRoot;
        private GameObject tooltip;
        private Text tooltipText;
        private Image[] slotBackgroundImages;
        private Image[] slotItemImages;
        private Text[] slotCountTexts;
        private InventoryEntry[] equipped;
        private Inventory inventory;
        [SerializeField] private SkillManager skillManager;
        [SerializeField] private Transform floatingTextAnchor;
        private PlayerCombatLoadout combatLoadout;

        private Text attackBonusText;
        private Text strengthBonusText;
        private Text rangeBonusText;
        private Text magicBonusText;
        private Text maxHitText;
        private Text meleeDefenceBonusText;
        private Text rangedDefenceBonusText;
        private Text magicDefenceBonusText;
        private Text petHeaderText;
        private Text petAttackLevelText;
        private Text petStrengthLevelText;
        private Text petAttackSpeedText;
        private Text petMaxHitText;

        private GameObject playerBonusPanel;
        private GameObject petBonusPanel;

        private static Equipment instance;

        private bool lastMergeState;

        private Sprite emptySlotSprite;

        public int TotalAttackBonus { get; private set; }
        public int TotalDefenceBonus { get; private set; }

        public event Action<EquipmentSlot> OnEquipmentChanged;

        private static readonly System.Collections.Generic.Dictionary<int, EquipmentSlot> cellToSlot = new()
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

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            emptySlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Empty_Slot");
            charmSlotSprite = Resources.Load<Sprite>("Interfaces/Equipment/Charm_Slot");

            inventory = GetComponent<Inventory>();
            skillManager = skillManager != null ? skillManager : GetComponent<SkillManager>();
            combatLoadout = GetComponent<PlayerCombatLoadout>();
            if (floatingTextAnchor == null)
                floatingTextAnchor = transform.Find("FloatingTextAnchor");
            equipped = new InventoryEntry[Enum.GetValues(typeof(EquipmentSlot)).Length - 1];
            CreateUI();
            Load();
            lastMergeState = PetMergeController.Instance != null && PetMergeController.Instance.IsMerged;
            UpdateBonuses();
            if (uiRoot != null)
                uiRoot.SetActive(false);

            UIManager.Instance.RegisterWindow(this);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        public void ToggleUI()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            bool toggle = false;
#else
            bool toggle = false;
#endif
            if (toggle)
                ToggleUI();

            bool merged = PetMergeController.Instance != null && PetMergeController.Instance.IsMerged;
            if (merged != lastMergeState)
            {
                lastMergeState = merged;
                UpdateBonuses();
            }
        }

        private int SlotIndex(EquipmentSlot slot) => (int)slot - 1;

        public bool IsOpen => uiRoot != null && uiRoot.activeSelf;

        public void Open()
        {
            var quest = Object.FindObjectOfType<QuestUI>();
            if (quest != null && quest.IsOpen)
                return;

            if (uiRoot != null)
            {
                UIManager.Instance.OpenWindow(this);
                var minimap = World.Minimap.Instance;
                minimap?.CloseExpanded();
                uiRoot.SetActive(true);
            }
        }

        public void Close()
        {
            if (uiRoot != null)
            {
                uiRoot.SetActive(false);
                HideTooltip();
            }
        }

        public void CloseUI() => Close();

        public InventoryEntry GetEquipped(EquipmentSlot slot)
        {
            int index = SlotIndex(slot);
            if (index < 0 || index >= equipped.Length)
                return default;
            return equipped[index];
        }

        public void ShowTooltip(EquipmentSlot slot, RectTransform slotRect)
        {
            var entry = GetEquipped(slot);
            var item = entry.item;
            if (item == null || tooltip == null || tooltipText == null)
            {
                HideTooltip();
                return;
            }

            string name = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
            tooltipText.text = name;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

            Vector3 pos = slotRect.position + new Vector3(slotSize.x, 0f, 0f);
            Vector3[] corners = new Vector3[4];
            tooltipRect.GetWorldCorners(corners);
            float width = corners[2].x - corners[0].x;
            float height = corners[2].y - corners[0].y;
            pos.x = Mathf.Min(pos.x, Screen.width - width);
            pos.y = Mathf.Max(pos.y, height);
            tooltipRect.position = pos;

            tooltip.SetActive(true);
        }

        public void HideTooltip()
        {
            if (tooltip != null)
                tooltip.SetActive(false);
        }

        /// <summary>
        /// Equip an entry into its designated slot. Returns true on success.
        /// </summary>
        public bool Equip(InventoryEntry entry)
        {
            var slot = entry.item != null ? entry.item.equipmentSlot : EquipmentSlot.None;
            if (slot == EquipmentSlot.None)
                return false;

            if (entry.item != null && skillManager != null && entry.item.skillRequirements != null)
            {
                foreach (var req in entry.item.skillRequirements)
                {
                    if (skillManager.GetLevel(req.skill) < req.level)
                    {
                        Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
                        FloatingText.Show($"You need {req.level} {req.skill} to wield this", anchor.position);
                        return false;
                    }
                }
            }

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
            OnEquipmentChanged?.Invoke(slot);
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
                OnEquipmentChanged?.Invoke(slot);
            }
        }

        private void UpdateSlotVisual(EquipmentSlot slot)
        {
            int index = SlotIndex(slot);
            if (index < 0 || index >= slotBackgroundImages.Length)
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
                    itemImg.sprite = entry.item.icon;
                    itemImg.color = entry.item.icon != null ? Color.white : Color.clear;
                }
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
                text.text = string.Empty;
            }
        }

        private void UpdatePlayerBonuses()
        {
            int attack = 0, strength = 0, range = 0, magic = 0;
            int meleeDef = 0, rangeDef = 0, magicDef = 0;

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
            }

            if (attackBonusText != null) attackBonusText.text = $"Attack = {attack}";
            if (strengthBonusText != null) strengthBonusText.text = $"Strength = {strength}";
            if (rangeBonusText != null) rangeBonusText.text = $"Range = {range}";
            if (magicBonusText != null) magicBonusText.text = $"Magic = {magic}";

            int strengthLevel = skillManager != null ? skillManager.GetLevel(SkillType.Strength) : 1;
            CombatStyle style = combatLoadout != null ? combatLoadout.Style : CombatStyle.Accurate;
            int effStr = CombatMath.GetEffectiveStrength(strengthLevel, style);
            int maxHit = CombatMath.GetMaxHit(effStr, strength);
            if (maxHitText != null) maxHitText.text = $"Total = {maxHit}";

            if (meleeDefenceBonusText != null) meleeDefenceBonusText.text = $"Melee = {meleeDef}";
            if (rangedDefenceBonusText != null) rangedDefenceBonusText.text = $"Range = {rangeDef}";
            if (magicDefenceBonusText != null) magicDefenceBonusText.text = $"Magic = {magicDef}";

            TotalAttackBonus = attack;
            TotalDefenceBonus = meleeDef + rangeDef + magicDef;
        }

        private void UpdatePetBonuses()
        {
            var def = PetDropSystem.ActivePet;
            var combat = PetDropSystem.ActivePetCombat;
            int bmLevel = skillManager != null ? skillManager.GetLevel(SkillType.Beastmaster) : 1;
            float mult = 1f;
            if (combat != null && combat.TryGetComponent<PetExperience>(out var exp))
                mult = PetExperience.GetStatMultiplier(exp.Level);

            int attackLevel = def != null ? Mathf.RoundToInt(def.petAttackLevel * mult) : 0;
            int strengthLevel = def != null ? Mathf.RoundToInt(def.petStrengthLevel * mult) : 0;
            int accuracyBonus = def != null ? Mathf.RoundToInt(def.accuracyBonus * mult) : 0;
            int damageBonus = def != null ? Mathf.RoundToInt(def.damageBonus * mult) : 0;

            if (def != null && def.attackLevelPerBeastmasterLevel != 0f)
                attackLevel = Mathf.RoundToInt(attackLevel * (1f + def.attackLevelPerBeastmasterLevel * bmLevel));
            if (def != null && def.strengthLevelPerBeastmasterLevel != 0f)
                strengthLevel = Mathf.RoundToInt(strengthLevel * (1f + def.strengthLevelPerBeastmasterLevel * bmLevel));

            int attackSpeed = def != null ? def.attackSpeedTicks : 0;
            float speed = attackSpeed * 0.6f;
            int effStr = CombatMath.GetEffectiveStrength(strengthLevel, CombatStyle.Accurate);
            int maxHit = CombatMath.GetMaxHit(effStr, damageBonus);
            if (def != null && def.maxHitPerBeastmasterLevel != 0f)
                maxHit = Mathf.RoundToInt(maxHit * (1f + def.maxHitPerBeastmasterLevel * bmLevel));

            if (petHeaderText != null)
                petHeaderText.text = def != null ? $"{def.displayName}:" : "Pet:";
            if (petAttackLevelText != null)
                petAttackLevelText.text = $"Attack Level = {attackLevel} - Attack = {accuracyBonus}";
            if (petStrengthLevelText != null)
                petStrengthLevelText.text = $"Strength Level = {strengthLevel} - Strength = {damageBonus}";
            if (petAttackSpeedText != null)
                petAttackSpeedText.text = $"Attack Speed = {attackSpeed} - Speed = {speed:F1}";
            if (petMaxHitText != null)
                petMaxHitText.text = $"Max Hit = {maxHit}";

            TotalAttackBonus = 0;
            TotalDefenceBonus = 0;
        }

        private void UpdateBonuses()
        {
            bool merged = PetMergeController.Instance != null && PetMergeController.Instance.IsMerged;
            if (playerBonusPanel != null)
                playerBonusPanel.SetActive(!merged);
            if (petBonusPanel != null)
                petBonusPanel.SetActive(merged);
            if (merged)
                UpdatePetBonuses();
            else
                UpdatePlayerBonuses();
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
                case EquipmentSlot.Charm: return charmSlotSprite;
                default: return slotFrameSprite;
            }
        }

        private void CreateUI()
        {
            var existing = GameObject.Find("EquipmentUI");
            if (existing != null)
                Destroy(existing);

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

            slotBackgroundImages = new Image[equipped.Length];
            slotItemImages = new Image[equipped.Length];
            slotCountTexts = new Text[equipped.Length];

            Font defaultFont = null;
            try
            {
                defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                defaultFont = null;
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

                    GameObject itemGO = new GameObject("Item", typeof(Image));
                    itemGO.transform.SetParent(cell.transform, false);
                    var itemImg = itemGO.GetComponent<Image>();
                    itemImg.raycastTarget = false;
                    var itemRect = itemGO.GetComponent<RectTransform>();
                    itemRect.anchorMin = Vector2.zero;
                    itemRect.anchorMax = Vector2.one;
                    itemRect.offsetMin = Vector2.zero;
                    itemRect.offsetMax = Vector2.zero;
                    itemImg.color = Color.clear;

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

                    slotBackgroundImages[SlotIndex(slot)] = img;
                    slotItemImages[SlotIndex(slot)] = itemImg;
                    slotCountTexts[SlotIndex(slot)] = countText;
                }
                else
                {
                    var placeholder = cell.AddComponent<Image>();
                    placeholder.color = Color.clear;
                }
            }

            playerBonusPanel = new GameObject("Bonuses", typeof(RectTransform));
            playerBonusPanel.transform.SetParent(window.transform, false);
            var bonusRect = playerBonusPanel.GetComponent<RectTransform>();
            bonusRect.anchorMin = new Vector2(1f, 0.5f);
            bonusRect.anchorMax = new Vector2(1f, 0.5f);
            bonusRect.pivot = new Vector2(1f, 0.5f);
            bonusRect.anchoredPosition = new Vector2(-8f, 0f);
            bonusRect.sizeDelta = new Vector2(bonusWidth, contentSize.y);

            petBonusPanel = new GameObject("PetBonuses", typeof(RectTransform));
            petBonusPanel.transform.SetParent(window.transform, false);
            var petBonusRect = petBonusPanel.GetComponent<RectTransform>();
            petBonusRect.anchorMin = new Vector2(1f, 0.5f);
            petBonusRect.anchorMax = new Vector2(1f, 0.5f);
            petBonusRect.pivot = new Vector2(1f, 0.5f);
            petBonusRect.anchoredPosition = new Vector2(-8f, 0f);
            petBonusRect.sizeDelta = new Vector2(bonusWidth, contentSize.y);
            petBonusPanel.SetActive(false);

            float lineHeight = 14f;
            Text CreateText(Transform parent, string name, string txt, float y, Font font, Color color)
            {
                GameObject go = new GameObject(name, typeof(Text));
                go.transform.SetParent(parent, false);
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

            CreateText(playerBonusPanel.transform, "CombatHeader", "Combat:", 0f, combatHeaderFont, combatHeaderColor);
            attackBonusText = CreateText(playerBonusPanel.transform, "Attack", "Attack = 0", -lineHeight, attackFont, attackColor);
            strengthBonusText = CreateText(playerBonusPanel.transform, "Strength", "Strength = 0", -2f * lineHeight, strengthFont, strengthColor);
            rangeBonusText = CreateText(playerBonusPanel.transform, "Range", "Range = 0", -3f * lineHeight, rangeFont, rangeColor);
            magicBonusText = CreateText(playerBonusPanel.transform, "Magic", "Magic = 0", -4f * lineHeight, magicFont, magicColor);
            CreateText(playerBonusPanel.transform, "MaxHitHeader", "Max Hit:", -5f * lineHeight, maxHitHeaderFont, maxHitHeaderColor);
            maxHitText = CreateText(playerBonusPanel.transform, "MaxHit", "Total = 0", -6f * lineHeight, maxHitFont, maxHitColor);
            CreateText(playerBonusPanel.transform, "DefenceHeader", "Defence:", -7f * lineHeight, defenceHeaderFont, defenceHeaderColor);
            meleeDefenceBonusText = CreateText(playerBonusPanel.transform, "MeleeDef", "Melee = 0", -8f * lineHeight, meleeDefFont, meleeDefColor);
            rangedDefenceBonusText = CreateText(playerBonusPanel.transform, "RangeDef", "Range = 0", -9f * lineHeight, rangeDefFont, rangeDefColor);
            magicDefenceBonusText = CreateText(playerBonusPanel.transform, "MagicDef", "Magic = 0", -10f * lineHeight, magicDefFont, magicDefColor);

            petHeaderText = CreateText(petBonusPanel.transform, "PetHeader", "Pet:", 0f, petHeaderFont, petHeaderColor);
            petAttackLevelText = CreateText(petBonusPanel.transform, "PetAttackLevel", "Attack Level = 0 - Attack = 0", -lineHeight, petAttackLevelFont, petAttackLevelColor);
            petStrengthLevelText = CreateText(petBonusPanel.transform, "PetStrengthLevel", "Strength Level = 0 - Strength = 0", -2f * lineHeight, petStrengthLevelFont, petStrengthLevelColor);
            petAttackSpeedText = CreateText(petBonusPanel.transform, "PetAttackSpeed", "Attack Speed = 0 - Speed = 0", -3f * lineHeight, petAttackSpeedFont, petAttackSpeedColor);
            petMaxHitText = CreateText(petBonusPanel.transform, "PetMaxHit", "Max Hit = 0", -4f * lineHeight, petMaxHitFont, petMaxHitColor);

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
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 2f;

            var fitter = tooltip.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGO = new GameObject("Name", typeof(Text));
            textGO.transform.SetParent(tooltip.transform, false);
            tooltipText = textGO.GetComponent<Text>();
            tooltipText.font = tooltipFont != null ? tooltipFont : defaultFont;
            tooltipText.alignment = TextAnchor.UpperLeft;
            tooltipText.color = tooltipColor;
            tooltipText.raycastTarget = false;
            tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipText.verticalOverflow = VerticalWrapMode.Overflow;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            tooltipRect.pivot = new Vector2(0f, 1f);

            tooltip.SetActive(false);
        }
    }
}

