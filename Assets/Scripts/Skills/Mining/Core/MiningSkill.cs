using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Util;
using Skills.Mining;
using Skills;
using Skills.Common;
using Pets;
using Quests;
using BankSystem;
using Core.Save;
using Skills.Outfits;
using Random = UnityEngine.Random;
using UI;

namespace Skills.Mining
{
    /// <summary>
    /// Handles XP, level, and mining tick logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningSkill : TickedSkillBehaviour
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Equipment equipment;
        [SerializeField] private Transform floatingTextAnchor;

        private MineableRock currentRock;
        private PickaxeDefinition currentPickaxe;
        private int swingProgress;

        private SkillManager skills;
        private Dictionary<string, ItemData> oreItems;
        private int questOreCount;
        private SkillingOutfitProgress miningOutfit;

        public event System.Action<MineableRock> OnStartMining;
        public event System.Action OnStopMining;
        public event System.Action<string, int> OnOreGained;
        public event System.Action<int> OnLevelUp;

        public int Level => skills != null ? skills.GetLevel(SkillType.Mining) : 1;
        public float Xp => skills != null ? skills.GetXp(SkillType.Mining) : 0f;
        public bool IsMining => currentRock != null;
        public MineableRock CurrentRock => currentRock;
        public PickaxeDefinition CurrentPickaxe => currentPickaxe;
        public int CurrentSwingSpeedTicks => currentPickaxe?.SwingSpeedTicks ?? 0;
        public float SwingProgressNormalized
            => currentPickaxe == null || currentPickaxe.SwingSpeedTicks <= 1
                ? 0f
                : (float)swingProgress / (currentPickaxe.SwingSpeedTicks - 1);

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            skills = GetComponent<SkillManager>();
            PreloadOreItems();
            miningOutfit = new SkillingOutfitProgress(new[]
            {
                "Mining Helmet",
                "Mining Jacket",
                "Mining Pants",
                "Mining Boots",
                "Mining Gloves"
            }, "MiningOutfitOwned");
        }

        private void OnDestroy()
        {
            SaveManager.Unregister(miningOutfit);
        }

        /// <summary>
        ///     Enables ticker logging so we can trace subscription timing during debugging sessions.
        /// </summary>
        protected override bool LogTickerSubscription => true;

        protected override void HandleTick()
        {
            if (!IsMining)
                return;

            // Stop immediately if the current rock has already been depleted
            if (currentRock == null || currentRock.IsDepleted)
            {
                StopMining();
                return;
            }

            swingProgress++;
            Debug.Log($"Mining tick: {swingProgress}/{currentPickaxe.SwingSpeedTicks}");
            if (swingProgress >= currentPickaxe.SwingSpeedTicks)
            {
                swingProgress = 0;
                AttemptMine();
            }
        }

        private void AttemptMine()
        {
            float baseChance = 0.35f;
            float penalty = 0.0025f * Mathf.Max(currentRock.RockDef.Ore.LevelRequirement - 1, 0);
            int level = skills.GetLevel(SkillType.Mining);
            float chance = baseChance + (level * 0.005f) + currentPickaxe.MiningRollBonus - penalty;
            chance = Mathf.Clamp(chance, 0.05f, 0.90f);

            if (Random.value <= chance)
            {
                OreDefinition ore = currentRock.MineOre();
                if (ore != null)
                {
                    oreItems.TryGetValue(ore.Id, out var item);
                    int amount = PetDropSystem.ActivePet?.id == "Rock Golem" ? 2 : 1;
                    if (amount > 1)
                        BeastmasterXp.TryGrantFromPetAssist(ore.XpPerOre * (amount - 1));
                    bool added = true;
                    var petStorage = PetDropSystem.ActivePet?.id == "Rock Golem" && PetDropSystem.ActivePetObject != null
                        ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                        : null;

                    Transform anchorTransform = floatingTextAnchor != null
                        ? floatingTextAnchor
                        : transform;
                    Vector3 anchorPos = anchorTransform.position;

                    for (int i = 0; i < amount; i++)
                    {
                        bool stepAdded = false;
                        if (item != null && inventory != null)
                            stepAdded = inventory.AddItem(item, 1);
                        if (!stepAdded && petStorage != null)
                            stepAdded = petStorage.StoreItem(item, 1);
                        if (!stepAdded)
                        {
                            added = false;
                            break;
                        }
                    }

                    if (!added)
                    {
                        FloatingText.Show("Your inventory is full", anchorPos);
                        StopMining();
                        return;
                    }

                    float xpBonus = 0f;
                    if (equipment != null)
                    {
                        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                        {
                            if (slot == EquipmentSlot.None)
                                continue;
                            var entry = equipment.GetEquipped(slot);
                            if (entry.item != null)
                                xpBonus += entry.item.miningXpBonusMultiplier;
                        }
                    }
                    int xpGain = Mathf.RoundToInt(ore.XpPerOre * amount * (1f + xpBonus));
                    int oldLevel = skills.GetLevel(SkillType.Mining);
                    int newLevel = skills.AddXP(SkillType.Mining, xpGain);
                    FloatingText.Show($"+{amount} {ore.DisplayName}", anchorPos);
                    StartCoroutine(ShowXpGainDelayed(xpGain, anchorTransform));
                    OnOreGained?.Invoke(ore.Id, amount);

                    if (ore.PetDropChance > 0)
                        PetDropSystem.TryRollPet("mining", currentRock.transform.position, skills, ore.PetDropChance, out _);

                    if (QuestManager.Instance != null && QuestManager.Instance.IsQuestActive("ToolsOfSurvival"))
                    {
                        var quest = QuestManager.Instance.GetQuest("ToolsOfSurvival");
                        var step = quest?.Steps.Find(s => s.StepID == "MineOres");
                        if (step != null && !step.IsComplete)
                        {
                            questOreCount += amount;
                            if (questOreCount >= 3)
                                QuestManager.Instance.UpdateStep("ToolsOfSurvival", "MineOres");
                        }
                    }

                    TryAwardMiningOutfitPiece();

                    if (newLevel > oldLevel)
                    {
                        FloatingText.Show($"Mining level {newLevel}", anchorPos);
                        OnLevelUp?.Invoke(newLevel);
                    }
                }

                if (currentRock.IsDepleted)
                    StopMining();
            }
            else
            {
                Debug.Log($"Failed to mine {currentRock.name}");
            }
        }

        private IEnumerator ShowXpGainDelayed(int xpGain, Transform anchor)
        {
            yield return new WaitForSeconds(Ticker.TickDuration * 5f);
            if (anchor != null)
            {
                FloatingText.Show($"+{xpGain} XP", anchor.position);
            }
        }

        public void StartMining(MineableRock rock, PickaxeDefinition pickaxe)
        {
            if (rock == null || pickaxe == null)
                return;

            currentRock = rock;
            currentPickaxe = pickaxe;
            swingProgress = 0;
            Debug.Log($"Started mining {rock.name}");
            OnStartMining?.Invoke(rock);
        }

        public void StopMining()
        {
            if (!IsMining)
                return;

            Debug.Log("Stopped mining");
            currentRock = null;
            currentPickaxe = null;
            swingProgress = 0;
            OnStopMining?.Invoke();
        }

        public bool CanAddOre(OreDefinition ore)
        {
            if (inventory == null || ore == null)
                return true;
            if (oreItems == null)
                PreloadOreItems();
            if (!oreItems.TryGetValue(ore.Id, out var item) || item == null)
                return true;

            int amount = PetDropSystem.ActivePet?.id == "Rock Golem" ? 2 : 1;
            if (inventory.CanAddItem(item, amount))
                return true;

            if (amount > 1)
            {
                var petStorage = PetDropSystem.ActivePetObject != null
                    ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                    : null;
                var petInv = petStorage != null
                    ? petStorage.GetComponent<Inventory.Inventory>()
                    : null;

                if (inventory.CanAddItem(item, 1) && petInv != null && petInv.CanAddItem(item, 1))
                    return true;

                if (petInv != null && petInv.CanAddItem(item, amount))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Debug helper to directly set the mining level via the SkillManager.
        /// </summary>
        public void DebugSetLevel(int newLevel)
        {
            skills?.DebugSetLevel(SkillType.Mining, Mathf.Clamp(newLevel, 1, 99));
            OnLevelUp?.Invoke(Level);
        }

        private void TryAwardMiningOutfitPiece()
        {
            int roll = UnityEngine.Random.Range(0, 2500);
            if (SkillingOutfitProgress.DebugChance)
                Debug.Log($"[Mining] Skilling outfit roll: {roll} (chance 1 in 2500)");
            if (roll != 0)
                return;

            var missing = new List<string>();
            foreach (var id in miningOutfit.allPieceIds)
            {
                if (!miningOutfit.owned.Contains(id))
                    missing.Add(id);
            }
            if (missing.Count == 0)
                return;

            string chosen = missing[UnityEngine.Random.Range(0, missing.Count)];
            var item = ItemDatabase.GetItem(chosen);
            bool added = inventory != null && item != null && inventory.AddItem(item);
            if (!added)
            {
                BankUI.Instance?.AddItemToBank(item);
                PetToastUI.Show("A piece of mining outfit has been added to your bank");
            }
            else
            {
                PetToastUI.Show("You've received a piece of mining outfit");
            }
            miningOutfit.owned.Add(chosen);
        }

        private void PreloadOreItems()
        {
            oreItems = new Dictionary<string, ItemData>();
            var items = Resources.LoadAll<ItemData>("Item");
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.id))
                    oreItems[item.id] = item;
            }
        }
    }
}
