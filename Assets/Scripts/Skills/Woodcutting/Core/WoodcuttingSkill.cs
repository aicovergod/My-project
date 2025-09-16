using System;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Util;
using UI;
using Skills;
using Skills.Common;
using Pets;
using Quests;
using BankSystem;
using Core.Save;
using Skills.Outfits;
using Random = UnityEngine.Random;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Handles XP, level, and woodcutting tick logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodcuttingSkill : TickedSkillBehaviour
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Equipment equipment;
        [SerializeField] private Transform floatingTextAnchor;

        private TreeNode currentTree;
        private AxeDefinition currentAxe;
        private int chopProgress;
        private int currentIntervalTicks;

        private SkillManager skills;

        private Dictionary<string, ItemData> logItems;
        private int questLogCount;
        private SkillingOutfitProgress woodcuttingOutfit;

        public event System.Action<TreeNode> OnStartChopping;
        public event System.Action OnStopChopping;
        public event System.Action<string, int> OnLogGained;
        public event System.Action<int> OnLevelUp;

        public int Level => skills != null ? skills.GetLevel(SkillType.Woodcutting) : 1;
        public float Xp => skills != null ? skills.GetXp(SkillType.Woodcutting) : 0f;
        public bool IsChopping => currentTree != null;
        public TreeNode CurrentTree => currentTree;
        public int CurrentChopIntervalTicks => currentIntervalTicks;
        public AxeDefinition CurrentAxe => currentAxe;
        public float ChopProgressNormalized
            => currentIntervalTicks <= 1 ? 0f : (float)chopProgress / (currentIntervalTicks - 1);

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            if (floatingTextAnchor == null)
            {
                // Auto-cache the floating text anchor if it wasn't assigned in the inspector.
                floatingTextAnchor = transform.Find("FloatingTextAnchor");
            }
            skills = GetComponent<SkillManager>();
            PreloadLogItems();
            woodcuttingOutfit = new SkillingOutfitProgress(new[]
            {
                "Lumberjack Helmet",
                "Lumberjack Shirt",
                "Lumberjack Pants",
                "Lumberjack Boots",
                "Lumberjack Gloves"
            }, "WoodcuttingOutfitOwned");
        }

        private void OnDestroy()
        {
            SaveManager.Unregister(woodcuttingOutfit);
        }

        /// <summary>
        ///     Enables ticker logging so we can trace subscription timing during debugging sessions.
        /// </summary>
        protected override bool LogTickerSubscription => true;

        protected override void HandleTick()
        {
            if (!IsChopping)
                return;

            if (currentTree == null || currentTree.IsDepleted)
            {
                StopChopping();
                return;
            }

            chopProgress++;
            if (chopProgress >= currentIntervalTicks)
            {
                chopProgress = 0;
                AttemptChop();
            }
        }

        private void AttemptChop()
        {
            float baseChance = 0.35f;
            float penalty = 0.0025f * Mathf.Max(currentTree.def.RequiredWoodcuttingLevel - 1, 0);
            int level = skills.GetLevel(SkillType.Woodcutting);
            float chance = baseChance + (level * 0.005f) + currentAxe.Power * 0.01f - penalty;
            chance = Mathf.Clamp(chance, 0.05f, 0.90f);

            if (Random.value <= chance)
            {
                string logId = currentTree.def.LogItemId;
                logItems.TryGetValue(logId, out var item);
                int amount = PetDropSystem.ActivePet?.id == "Beaver" ? 2 : 1;
                var petStorage = PetDropSystem.ActivePet?.id == "Beaver" && PetDropSystem.ActivePetObject != null
                    ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                    : null;
                string logName = item != null ? item.itemName : currentTree.def.DisplayName;

                var context = new GatheringRewardContext
                {
                    runner = this,
                    skills = skills,
                    skillType = SkillType.Woodcutting,
                    inventory = inventory,
                    petStorage = petStorage,
                    item = item,
                    rewardDisplayName = logName,
                    quantity = amount,
                    xpPerItem = currentTree.def.XpPerLog,
                    petAssistExtraQuantity = Mathf.Max(0, amount - 1),
                    floatingTextAnchor = floatingTextAnchor,
                    fallbackAnchor = transform,
                    equipment = equipment,
                    equipmentXpBonusEvaluator = data => data != null ? data.woodcuttingXpBonusMultiplier : 0f,
                    showItemFloatingText = true,
                    showXpPopup = true,
                    xpPopupDelayTicks = 5f,
                    rewardMessageFormatter = qty => $"+{qty} {logName}",
                    onItemsGranted = result => OnLogGained?.Invoke(logId, result.QuantityAwarded),
                    onXpApplied = result =>
                    {
                        if (result.LeveledUp && result.Anchor != null)
                        {
                            FloatingText.Show(
                                $"Woodcutting level {result.NewLevel}",
                                result.Anchor,
                                null,
                                GatheringRewardProcessor.DefaultFloatingTextSize);
                            OnLevelUp?.Invoke(result.NewLevel);
                        }
                    },
                    onSuccess = result =>
                    {
                        if (currentTree.def.PetDropChance > 0)
                            PetDropSystem.TryRollPet("woodcutting", currentTree.transform.position, skills, currentTree.def.PetDropChance, out _);

                        if (QuestManager.Instance != null && QuestManager.Instance.IsQuestActive("ToolsOfSurvival"))
                        {
                            var quest = QuestManager.Instance.GetQuest("ToolsOfSurvival");
                            var step = quest?.Steps.Find(s => s.StepID == "ChopLogs");
                            if (step != null && !step.IsComplete)
                            {
                                questLogCount += result.QuantityAwarded;
                                if (questLogCount >= 3)
                                    QuestManager.Instance.UpdateStep("ToolsOfSurvival", "ChopLogs");
                            }
                        }

                        TryAwardWoodcuttingOutfitPiece();
                    },
                    onFailure = _ => StopChopping()
                };

                var rewardResult = GatheringRewardProcessor.Process(context);
                if (!rewardResult.Success)
                    return;

                currentTree.OnLogChopped();
                if (currentTree.IsDepleted)
                    StopChopping();
            }
            else
            {
                Debug.Log($"Failed to chop {currentTree.name}");
            }
        }

        public void StartChopping(TreeNode tree, AxeDefinition axe)
        {
            if (tree == null || axe == null)
                return;

            currentTree = tree;
            currentAxe = axe;
            chopProgress = 0;
            currentIntervalTicks = Mathf.Max(1, Mathf.RoundToInt(tree.def.ChopIntervalTicks / Mathf.Max(0.01f, axe.SwingSpeedMultiplier)));
            Debug.Log($"Started chopping {tree.name}");
            currentTree.IsBusy = true;
            OnStartChopping?.Invoke(tree);
        }

        public void StopChopping()
        {
            if (!IsChopping)
                return;

            Debug.Log("Stopped chopping");
            if (currentTree != null)
                currentTree.IsBusy = false;
            currentTree = null;
            currentAxe = null;
            chopProgress = 0;
            currentIntervalTicks = 0;
            OnStopChopping?.Invoke();
        }

        public bool CanAddLog(TreeDefinition tree)
        {
            if (inventory == null || tree == null)
                return true;
            if (logItems == null)
                PreloadLogItems();
            if (!logItems.TryGetValue(tree.LogItemId, out var item) || item == null)
                return true;

            int amount = PetDropSystem.ActivePet?.id == "Beaver" ? 2 : 1;
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
        /// Debug helper to directly set the woodcutting level via the SkillManager.
        /// </summary>
        public void DebugSetLevel(int newLevel)
        {
            skills?.DebugSetLevel(SkillType.Woodcutting, Mathf.Clamp(newLevel, 1, 99));
            OnLevelUp?.Invoke(Level);
        }

        private void TryAwardWoodcuttingOutfitPiece()
        {
            int roll = UnityEngine.Random.Range(0, 2500);
            if (SkillingOutfitProgress.DebugChance)
                Debug.Log($"[Woodcutting] Skilling outfit roll: {roll} (chance 1 in 2500)");
            if (roll != 0)
                return;

            var missing = new List<string>();
            foreach (var id in woodcuttingOutfit.allPieceIds)
            {
                if (!woodcuttingOutfit.owned.Contains(id))
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
                PetToastUI.Show("A piece of woodcutting outfit has been added to your bank");
            }
            else
            {
                PetToastUI.Show("You've received a piece of woodcutting outfit");
            }
            woodcuttingOutfit.owned.Add(chosen);
        }

        private void PreloadLogItems()
        {
            logItems = new Dictionary<string, ItemData>();
            var items = Resources.LoadAll<ItemData>("Item");
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.id))
                    logItems[item.id] = item;
            }
        }
    }
}
