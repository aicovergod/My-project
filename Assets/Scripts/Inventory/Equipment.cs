// Assets/Scripts/Inventory/Equipment.cs
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Core.Save;
using Skills;
using Beastmaster;
using Pets;
using Combat;
using Player;
using Quests;
using UI;
using Object = UnityEngine.Object;

namespace Inventory
{
    /// <summary>
    /// Handles equipping items into fixed slots and displays a simple UI
    /// similar to the Old School RuneScape equipment interface. The UI is
    /// generated at runtime and opened through the standard interface tab
    /// buttons instead of a dedicated hotkey.
    /// </summary>
    [DisallowMultipleComponent]
    public class Equipment : MonoBehaviour, IUIWindow
    {
        [Tooltip("Size of each slot in pixels.")]
        public Vector2 slotSize = new(32f, 32f);

        [Tooltip("Spacing between slots in pixels.")]
        public Vector2 slotSpacing = new(4f, 4f);

        /// <summary>
        /// UI scale factor applied across the generated equipment window so the
        /// interface can be enlarged without manually updating every metric.
        /// </summary>
        private const float uiScale = 2f;

        /// <summary>
        /// Cached slot size after scaling. Stored so tooltip placement can
        /// reference the enlarged slot bounds without recalculating them.
        /// </summary>
        private Vector2 scaledSlotSize;

        [Tooltip("Reference resolution for the UI Canvas.")]
        public Vector2 referenceResolution = new(1024f, 768f);

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
        public Font tooltipNameFont;
        public Color tooltipNameColor = Color.white;
        public Font tooltipBonusFont;
        public Color tooltipBonusColor = Color.white;

        private GameObject uiRoot;
        private GameObject tooltip;
        private Text tooltipNameText;
        private Text tooltipBonusText;
        private Image[] slotBackgroundImages;
        private Image[] slotItemImages;
        private Text[] slotCountTexts;
        private InventoryEntry[] equipped;
        private Inventory inventory;
        [SerializeField] private SkillManager skillManager;
        [SerializeField] private Transform floatingTextAnchor;
        private PlayerCombatLoadout combatLoadout;

        private Text attackBonusText;
        private Text meleeBonusText;
        private Text rangeAccuracyBonusText;
        private Text rangeStrengthBonusText;
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
        private Color ammoDefaultColor = Color.white;

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

        private void Update()
        {
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

            Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;

            if (entry.item != null && skillManager != null && entry.item.skillRequirements != null)
            {
                foreach (var req in entry.item.skillRequirements)
                {
                    if (skillManager.GetLevel(req.skill) < req.level)
                    {
                        FloatingText.Show($"You need {req.level} {req.skill} to wield this", anchor.position);
                        return false;
                    }
                }
            }

            int index = SlotIndex(slot);
            if (index < 0 || index >= equipped.Length)
                return false;

            // Handle mutually exclusive weapon/shield pairing before mutating any slots.
            if (slot == EquipmentSlot.Weapon && entry.item != null && entry.item.isTwoHanded)
            {
                int shieldIndex = SlotIndex(EquipmentSlot.Shield);
                if (shieldIndex >= 0 && shieldIndex < equipped.Length)
                {
                    var shieldEntry = equipped[shieldIndex];
                    if (shieldEntry.item != null)
                    {
                        if (inventory == null || !inventory.CanAddItem(shieldEntry.item, shieldEntry.count))
                        {
                            FloatingText.Show("Your inventory is full", anchor.position);
                            return false;
                        }

                        if (!inventory.AddItem(shieldEntry.item, shieldEntry.count))
                        {
                            FloatingText.Show("Your inventory is full", anchor.position);
                            return false;
                        }

                        equipped[shieldIndex] = default;
                        UpdateSlotVisual(EquipmentSlot.Shield);
                        UpdateBonuses();
                        OnEquipmentChanged?.Invoke(EquipmentSlot.Shield);
                        ItemUseResolver.NotifyItemUsed(gameObject, shieldEntry.item, ItemUseType.Unequipped);
                    }
                }
            }
            else if (slot == EquipmentSlot.Shield)
            {
                int weaponIndex = SlotIndex(EquipmentSlot.Weapon);
                if (weaponIndex >= 0 && weaponIndex < equipped.Length)
                {
                    var weaponEntry = equipped[weaponIndex];
                    if (weaponEntry.item != null && weaponEntry.item.isTwoHanded)
                    {
                        if (inventory == null || !inventory.CanAddItem(weaponEntry.item, weaponEntry.count))
                        {
                            FloatingText.Show("Your inventory is full", anchor.position);
                            return false;
                        }

                        if (!inventory.AddItem(weaponEntry.item, weaponEntry.count))
                        {
                            FloatingText.Show("Your inventory is full", anchor.position);
                            return false;
                        }

                        equipped[weaponIndex] = default;
                        UpdateSlotVisual(EquipmentSlot.Weapon);
                        UpdateBonuses();
                        OnEquipmentChanged?.Invoke(EquipmentSlot.Weapon);
                        ItemUseResolver.NotifyItemUsed(gameObject, weaponEntry.item, ItemUseType.Unequipped);
                    }
                }
            }

            var current = equipped[index];

            // Merge stackable equipment when the same item is already equipped instead of swapping it back
            // into the inventory. This mirrors OSRS behaviour where throwing weapons, arrows, and similar
            // stackables add to the equipped stack when re-equipped from the inventory.
            if (current.item != null && entry.item != null && current.item == entry.item && current.item.stackable)
            {
                int maxStack = current.item.MaxStack;
                int availableSpace = Mathf.Max(0, maxStack - current.count);
                if (availableSpace <= 0)
                {
                    // The equipped stack is already capped. Notify the player and abort so the original
                    // inventory slot restoration logic can place the stack back where it came from.
                    FloatingText.Show("You cannot equip any more of that.", anchor.position);
                    return false;
                }

                int transferAmount = Mathf.Min(entry.count, availableSpace);
                if (transferAmount <= 0)
                {
                    FloatingText.Show("You cannot equip any more of that.", anchor.position);
                    return false;
                }

                int overflow = entry.count - transferAmount;
                if (overflow > 0)
                {
                    // Return the overflow to the inventory. If the bag is full we abort so nothing is lost
                    // and the calling inventory logic restores the original stack.
                    if (inventory == null || !inventory.AddItem(entry.item, overflow))
                    {
                        FloatingText.Show("Your inventory is full", anchor.position);
                        return false;
                    }
                }

                current.count += transferAmount;
                equipped[index] = current;
                UpdateSlotVisual(slot);
                UpdateBonuses();
                Save();
                OnEquipmentChanged?.Invoke(slot);
                ItemUseResolver.NotifyItemUsed(gameObject, entry.item, ItemUseType.Equipped);
                return true;
            }

            if (current.item != null)
            {
                if (inventory != null && !inventory.AddItem(current.item, current.count))
                {
                    FloatingText.Show("Your inventory is full", anchor.position);
                    return false;
                }
                ItemUseResolver.NotifyItemUsed(gameObject, current.item, ItemUseType.Unequipped);
            }

            equipped[index] = entry;
            UpdateSlotVisual(slot);
            UpdateBonuses();
            Save();
            OnEquipmentChanged?.Invoke(slot);
            ItemUseResolver.NotifyItemUsed(gameObject, entry.item, ItemUseType.Equipped);
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
                ItemUseResolver.NotifyItemUsed(gameObject, entry.item, ItemUseType.Unequipped);
            }
        }

        /// <summary>
        /// Consumes a quantity from the specified equipped stack. Returns false when insufficient ammo is available.
        /// </summary>
        /// <param name="slot">Equipment slot to consume from.</param>
        /// <param name="amount">Number of items to remove.</param>
        /// <param name="remaining">Outputs the stack size after consumption.</param>
        public bool ConsumeEquipped(EquipmentSlot slot, int amount, out int remaining)
        {
            remaining = 0;
            int index = SlotIndex(slot);
            if (amount <= 0 || index < 0 || index >= equipped.Length)
                return false;

            var entry = equipped[index];
            if (entry.item == null || entry.count < amount)
                return false;

            entry.count -= amount;
            if (entry.count <= 0)
            {
                entry.item = null;
                entry.count = 0;
            }

            equipped[index] = entry;
            UpdateSlotVisual(slot);
            OnEquipmentChanged?.Invoke(slot);
            remaining = entry.count;
            return true;
        }

        /// <summary>
        /// Overrides the ammo slot stack text so gameplay systems can display warnings or custom counts.
        /// Passing <c>null</c> restores the default behaviour that mirrors the equipped stack size.
        /// </summary>
        /// <param name="message">Custom message to display. Null to revert to default count display.</param>
        /// <param name="color">Optional text colour override.</param>
        public void OverrideAmmoLabel(string message, Color? color = null)
        {
            int index = SlotIndex(EquipmentSlot.Arrow);
            if (slotCountTexts == null || index < 0 || index >= slotCountTexts.Length)
                return;

            var label = slotCountTexts[index];
            if (label == null)
                return;

            if (message == null)
            {
                var entry = GetEquipped(EquipmentSlot.Arrow);
                if (entry.item != null && entry.item.stackable && entry.count > 1)
                    label.text = entry.count.ToString();
                else
                    label.text = string.Empty;
                label.color = ammoDefaultColor;
                return;
            }

            label.text = message;
            label.color = color ?? ammoDefaultColor;
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
            int attack = 0;
            int meleeStrength = 0;
            int rangeAccuracy = 0;
            int rangeStrength = 0;
            int magic = 0;
            int meleeDef = 0, rangeDef = 0, magicDef = 0;

            CombatStyle style = combatLoadout != null ? combatLoadout.Style : CombatStyle.Accurate;

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
            // Subtract the OSRS constant + base level to isolate the style-only contribution for UI messaging.
            int rangedStyleBonus = CombatMath.GetEffectiveRangedStrength(rangedLevel, style) - (rangedLevel + 8);
            if (rangeStrengthBonusText != null)
            {
                string suffix = rangedStyleBonus > 0 ? $" (+{rangedStyleBonus} style)" : string.Empty;
                rangeStrengthBonusText.text = $"Ranged = {rangeStrength}{suffix}";
            }

            if (magicBonusText != null) magicBonusText.text = $"Magic = {magic}";

            int strengthLevel = skillManager != null ? skillManager.GetLevel(SkillType.Strength) : 1;
            int effStr = CombatMath.GetEffectiveStrength(strengthLevel, style);
            int maxHit = CombatMath.GetMaxHit(effStr, meleeStrength);
            if (maxHitText != null) maxHitText.text = $"Total = {maxHit}";

            int defenceLevel = skillManager != null ? skillManager.GetLevel(SkillType.Defence) : 1;
            // Defence effective levels follow the same pattern; isolating the style bonus clarifies the hidden boost.
            int defenceStyleBonus = CombatMath.GetEffectiveDefence(defenceLevel, style) - (defenceLevel + 8);
            string defenceSuffix = defenceStyleBonus > 0 ? $" (+{defenceStyleBonus} style)" : string.Empty;

            if (meleeDefenceBonusText != null) meleeDefenceBonusText.text = $"Melee = {meleeDef}{defenceSuffix}";
            if (rangedDefenceBonusText != null) rangedDefenceBonusText.text = $"Range = {rangeDef}{defenceSuffix}";
            if (magicDefenceBonusText != null) magicDefenceBonusText.text = $"Magic = {magicDef}{defenceSuffix}";

            TotalAttackBonus = attack;
            TotalDefenceBonus = meleeDef + rangeDef + magicDef;
        }

        private void ResetPlayerBonusTexts()
        {
            if (attackBonusText != null) attackBonusText.text = "Attack = 0";
            if (rangeAccuracyBonusText != null) rangeAccuracyBonusText.text = "Ranged = 0";
            if (meleeBonusText != null) meleeBonusText.text = "Melee = 0";
            if (rangeStrengthBonusText != null) rangeStrengthBonusText.text = "Ranged = 0";
            if (magicBonusText != null) magicBonusText.text = "Magic = 0";
            if (meleeDefenceBonusText != null) meleeDefenceBonusText.text = "Melee = 0";
            if (rangedDefenceBonusText != null) rangedDefenceBonusText.text = "Range = 0";
            if (magicDefenceBonusText != null) magicDefenceBonusText.text = "Magic = 0";
            if (maxHitText != null) maxHitText.text = "Total = 0";
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
            {
                ResetPlayerBonusTexts();
                UpdatePetBonuses();
            }
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
                    // Attempt to resolve the saved item id; if it no longer exists we clear
                    // the slot so stale counts are not preserved between loads.
                    var resolvedItem = ItemDatabase.GetItem(slot.id);
                    equipped[i].item = resolvedItem;
                    if (resolvedItem != null)
                    {
                        equipped[i].count = slot.count;
                    }
                    else
                    {
                        equipped[i].count = 0;
                        Debug.LogWarning($"Equipment.Load: Missing item '{slot.id}' for slot {(EquipmentSlot)(i + 1)}. Slot has been cleared to prevent invalid state.");
                    }
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

            // Apply scaling to the slot metrics so the UI can be enlarged consistently.
            Vector2 scaledSlotSpacing = slotSpacing * uiScale;
            scaledSlotSize = slotSize * uiScale;
            var contentSize = new Vector2(scaledSlotSize.x * 3f + scaledSlotSpacing.x * 2f,
                scaledSlotSize.y * 5f + scaledSlotSpacing.y * 4f);
            float scaledBonusWidth = 120f * uiScale;
            Vector2 scaledWindowPadding = new Vector2(16f * uiScale, 16f * uiScale);
            float scaledPanelOffset = 8f * uiScale;
            windowRect.sizeDelta = new Vector2(contentSize.x + scaledBonusWidth, contentSize.y) + scaledWindowPadding;

            var windowImg = window.GetComponent<Image>();
            windowImg.color = windowColor;

            GameObject panel = new GameObject("Slots", typeof(RectTransform), typeof(GridLayoutGroup));
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

            Font defaultFont = LegacyFontProvider.GetLegacyFont();
            float scaledLineHeight = 14f * uiScale;
            int scaledFontSize = Mathf.RoundToInt(scaledLineHeight);

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
            bonusRect.anchoredPosition = new Vector2(-scaledPanelOffset, 0f);
            bonusRect.sizeDelta = new Vector2(scaledBonusWidth, contentSize.y);

            petBonusPanel = new GameObject("PetBonuses", typeof(RectTransform));
            petBonusPanel.transform.SetParent(window.transform, false);
            var petBonusRect = petBonusPanel.GetComponent<RectTransform>();
            petBonusRect.anchorMin = new Vector2(1f, 0.5f);
            petBonusRect.anchorMax = new Vector2(1f, 0.5f);
            petBonusRect.pivot = new Vector2(1f, 0.5f);
            petBonusRect.anchoredPosition = new Vector2(-scaledPanelOffset, 0f);
            petBonusRect.sizeDelta = new Vector2(scaledBonusWidth, contentSize.y);
            petBonusPanel.SetActive(false);

            Text CreateText(Transform parent, string name, string txt, float y, Font font, Color color)
            {
                GameObject go = new GameObject(name, typeof(Text));
                go.transform.SetParent(parent, false);
                var t = go.GetComponent<Text>();
                t.font = font != null ? font : defaultFont;
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

            CreateText(playerBonusPanel.transform, "CombatHeader", "Combat:", 0f, combatHeaderFont, combatHeaderColor);
            attackBonusText = CreateText(playerBonusPanel.transform, "Attack", "Attack = 0", -scaledLineHeight, attackFont, attackColor);
            rangeAccuracyBonusText = CreateText(playerBonusPanel.transform, "RangeAccuracy", "Ranged = 0", -2f * scaledLineHeight, rangeFont, rangeColor);

            float magicLineY = -3f * scaledLineHeight;
            magicBonusText = CreateText(playerBonusPanel.transform, "Magic", "Magic = 0", magicLineY, magicFont, magicColor);

            float defenceHeaderLineY = magicLineY - scaledLineHeight;
            CreateText(playerBonusPanel.transform, "DefenceHeader", "Defence:", defenceHeaderLineY, defenceHeaderFont, defenceHeaderColor);
            meleeDefenceBonusText = CreateText(playerBonusPanel.transform, "MeleeDef", "Melee = 0", defenceHeaderLineY - scaledLineHeight, meleeDefFont, meleeDefColor);
            rangedDefenceBonusText = CreateText(playerBonusPanel.transform, "RangeDef", "Range = 0", defenceHeaderLineY - 2f * scaledLineHeight, rangeDefFont, rangeDefColor);
            magicDefenceBonusText = CreateText(playerBonusPanel.transform, "MagicDef", "Magic = 0", defenceHeaderLineY - 3f * scaledLineHeight, magicDefFont, magicDefColor);

            float bonusesHeaderLineY = defenceHeaderLineY - 4f * scaledLineHeight;
            CreateText(playerBonusPanel.transform, "BonusesHeader", "Bonuses:", bonusesHeaderLineY, combatHeaderFont, combatHeaderColor);
            meleeBonusText = CreateText(playerBonusPanel.transform, "Melee", "Melee = 0", bonusesHeaderLineY - scaledLineHeight, strengthFont, strengthColor);
            rangeStrengthBonusText = CreateText(playerBonusPanel.transform, "RangeStrength", "Ranged = 0", bonusesHeaderLineY - 2f * scaledLineHeight, rangeFont, rangeColor);

            float maxHitHeaderLineY = bonusesHeaderLineY - 3f * scaledLineHeight;
            CreateText(playerBonusPanel.transform, "MaxHitHeader", "Max Hit:", maxHitHeaderLineY, maxHitHeaderFont, maxHitHeaderColor);
            maxHitText = CreateText(playerBonusPanel.transform, "MaxHit", "Total = 0", maxHitHeaderLineY - scaledLineHeight, maxHitFont, maxHitColor);

            petHeaderText = CreateText(petBonusPanel.transform, "PetHeader", "Pet:", 0f, petHeaderFont, petHeaderColor);
            petAttackLevelText = CreateText(petBonusPanel.transform, "PetAttackLevel", "Attack Level = 0 - Attack = 0", -scaledLineHeight, petAttackLevelFont, petAttackLevelColor);
            petStrengthLevelText = CreateText(petBonusPanel.transform, "PetStrengthLevel", "Strength Level = 0 - Strength = 0", -2f * scaledLineHeight, petStrengthLevelFont, petStrengthLevelColor);
            petAttackSpeedText = CreateText(petBonusPanel.transform, "PetAttackSpeed", "Attack Speed = 0 - Speed = 0", -3f * scaledLineHeight, petAttackSpeedFont, petAttackSpeedColor);
            petMaxHitText = CreateText(petBonusPanel.transform, "PetMaxHit", "Max Hit = 0", -4f * scaledLineHeight, petMaxHitFont, petMaxHitColor);

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
            int tooltipPadding = Mathf.RoundToInt(4f * uiScale);
            layout.padding = new RectOffset(tooltipPadding, tooltipPadding, tooltipPadding, tooltipPadding);
            layout.spacing = 2f * uiScale;

            var fitter = tooltip.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var nameGO = new GameObject("Name", typeof(Text));
            nameGO.transform.SetParent(tooltip.transform, false);
            tooltipNameText = nameGO.GetComponent<Text>();
            tooltipNameText.font = tooltipNameFont != null ? tooltipNameFont : defaultFont;
            tooltipNameText.fontSize = scaledFontSize;
            tooltipNameText.alignment = TextAnchor.UpperLeft;
            tooltipNameText.color = tooltipNameColor;
            tooltipNameText.raycastTarget = false;
            tooltipNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipNameText.verticalOverflow = VerticalWrapMode.Overflow;

            var bonusGO = new GameObject("Bonus", typeof(Text));
            bonusGO.transform.SetParent(tooltip.transform, false);
            tooltipBonusText = bonusGO.GetComponent<Text>();
            tooltipBonusText.font = tooltipBonusFont != null ? tooltipBonusFont : defaultFont;
            tooltipBonusText.fontSize = scaledFontSize;
            tooltipBonusText.alignment = TextAnchor.UpperLeft;
            tooltipBonusText.color = tooltipBonusColor;
            tooltipBonusText.raycastTarget = false;
            tooltipBonusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipBonusText.verticalOverflow = VerticalWrapMode.Overflow;

            var tooltipRect = tooltip.GetComponent<RectTransform>();
            tooltipRect.pivot = new Vector2(0f, 1f);

            tooltip.SetActive(false);
        }
    }
}

