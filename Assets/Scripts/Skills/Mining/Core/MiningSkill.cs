using System;
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
            if (floatingTextAnchor == null)
            {
                // Cache the floating text anchor automatically so popups appear at head height.
                floatingTextAnchor = transform.Find("FloatingTextAnchor");
            }
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
                    bool rockGolemActive = PetDropSystem.ActivePet?.id == "Rock Golem";
                    int amount = rockGolemActive ? 2 : 1;
                    // Cache the storage component from the active Rock Golem pet so the reward processor can
                    // route overflow ore into the pet's inventory when it grants the bonus resource.
                    PetStorage petStorage = null;
                    if (rockGolemActive && PetDropSystem.ActivePetObject != null)
                        petStorage = PetDropSystem.ActivePetObject.GetComponent<PetStorage>();
                    string oreName = item != null ? item.itemName : ore.DisplayName;

                    var context = new GatheringRewardContext
                    {
                        runner = this,
                        skills = skills,
                        skillType = SkillType.Mining,
                        inventory = inventory,
                        petStorage = petStorage,
                        item = item,
                        rewardDisplayName = oreName,
                        quantity = amount,
                        xpPerItem = ore.XpPerOre,
                        petAssistExtraQuantity = Mathf.Max(0, amount - 1),
                        floatingTextAnchor = floatingTextAnchor,
                        fallbackAnchor = transform,
                        equipment = equipment,
                        equipmentXpBonusEvaluator = data => data != null ? data.miningXpBonusMultiplier : 0f,
                        showItemFloatingText = true,
                        showXpPopup = true,
                        xpPopupDelayTicks = 5f,
                        rewardMessageFormatter = qty => $"+{qty} {ore.DisplayName}",
                        onItemsGranted = result => OnOreGained?.Invoke(ore.Id, result.QuantityAwarded),
                        onXpApplied = result =>
                        {
                            if (result.LeveledUp && result.Anchor != null)
                            {
                                FloatingText.Show(
                                    $"Mining level {result.NewLevel}",
                                    result.Anchor,
                                    null,
                                    GatheringRewardProcessor.DefaultFloatingTextSize);
                                OnLevelUp?.Invoke(result.NewLevel);
                            }
                        },
                        onSuccess = result =>
                        {
                            if (ore.PetDropChance > 0)
                                PetDropSystem.TryRollPet("mining", currentRock.transform.position, skills, ore.PetDropChance, out _);

                            if (QuestManager.Instance != null && QuestManager.Instance.IsQuestActive("ToolsOfSurvival"))
                            {
                                var quest = QuestManager.Instance.GetQuest("ToolsOfSurvival");
                                var step = quest?.Steps.Find(s => s.StepID == "MineOres");
                                if (step != null && !step.IsComplete)
                                {
                                    questOreCount += result.QuantityAwarded;
                                    if (questOreCount >= 3)
                                        QuestManager.Instance.UpdateStep("ToolsOfSurvival", "MineOres");
                                }
                            }

                            TryAwardMiningOutfitPiece();
                        },
                        onFailure = _ => StopMining()
                    };

                    var rewardResult = GatheringRewardProcessor.Process(context);
                    if (!rewardResult.Success)
                        return;
                }

                if (currentRock.IsDepleted)
                    StopMining();
            }
            else
            {
                Debug.Log($"Failed to mine {currentRock.name}");
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
