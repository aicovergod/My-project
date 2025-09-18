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
            int requiredLevel = currentRock != null && currentRock.RockDef != null && currentRock.RockDef.Ore != null
                ? currentRock.RockDef.Ore.LevelRequirement
                : 1;
            float chance = GatheringRewardContextBuilder.CalculateSuccessChance(new GatheringRewardContextBuilder.SuccessChanceArgs
            {
                PlayerLevel = skills.GetLevel(SkillType.Mining),
                RequiredLevel = requiredLevel,
                ToolBonus = currentPickaxe != null ? currentPickaxe.MiningRollBonus : 0f
            });

            if (Random.value <= chance)
            {
                OreDefinition ore = currentRock.MineOre();
                if (ore != null)
                {
                    var item = GatheringInventoryHelper.GetItemData(ore.Id, ref oreItems);
                    bool rockGolemActive = PetDropSystem.ActivePet?.id == "Rock Golem";
                    int amount = rockGolemActive ? 2 : 1;
                    // Cache the storage component from the active Rock Golem pet so the reward processor can
                    // route overflow ore into the pet's inventory when it grants the bonus resource.
                    PetStorage petStorage = null;
                    if (rockGolemActive && PetDropSystem.ActivePetObject != null)
                        petStorage = PetDropSystem.ActivePetObject.GetComponent<PetStorage>();
                    string oreName = item != null ? item.itemName : ore.DisplayName;

                    var context = GatheringRewardContextBuilder.BuildContext(new GatheringRewardContextBuilder.ContextArgs
                    {
                        Runner = this,
                        Skills = skills,
                        SkillType = SkillType.Mining,
                        Inventory = inventory,
                        PetStorage = petStorage,
                        Item = item,
                        RewardDisplayName = oreName,
                        Quantity = amount,
                        XpPerItem = ore.XpPerOre,
                        PetAssistExtraQuantity = amount - 1,
                        FloatingTextAnchor = floatingTextAnchor,
                        FallbackAnchor = transform,
                        Equipment = equipment,
                        EquipmentXpBonusEvaluator = data => data != null ? data.miningXpBonusMultiplier : 0f,
                        RewardMessageFormatter = qty => $"+{qty} {ore.DisplayName}",
                        OnItemsGranted = result => OnOreGained?.Invoke(ore.Id, result.QuantityAwarded),
                        OnSuccess = result =>
                        {
                            int? petChance = ore != null ? ore.PetDropChance : (int?)null;
                            SkillingPetRewarder.TryRollPet(
                                "mining",
                                skills,
                                currentRock != null ? currentRock.transform : transform,
                                petChance);

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
                        OnFailure = _ => StopMining(),
                        LevelUpFloatingTextFormatter = result => $"Mining level {result.NewLevel}",
                        OnLevelUp = level => OnLevelUp?.Invoke(level)
                    });

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
            if (ore == null)
                return true;

            return GatheringInventoryHelper.CanAcceptGatheredItem(
                inventory,
                ore.Id,
                "Rock Golem",
                ref oreItems,
                out _);
        }

        /// <summary>
        /// Debug helper to directly set the mining level via the SkillManager.
        /// </summary>
        public void DebugSetLevel(int newLevel)
        {
            skills?.DebugSetLevel(SkillType.Mining, Mathf.Clamp(newLevel, 1, 99));
            OnLevelUp?.Invoke(Level);
        }

        private bool TryAwardMiningOutfitPiece()
        {
            return SkillingOutfitRewarder.TryAwardPiece(
                miningOutfit,
                inventory,
                BankUI.Instance,
                Random.Range,
                "Mining",
                "You've received a piece of mining outfit",
                "A piece of mining outfit has been added to your bank");
        }

        private void PreloadOreItems()
        {
            GatheringInventoryHelper.EnsureItemCache(ref oreItems);
        }
    }
}
