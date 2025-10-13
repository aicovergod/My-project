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
using Skills.Outfits;
using Random = UnityEngine.Random;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Handles XP, level, and woodcutting tick logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodcuttingSkill : DebuggableTickedSkillBehaviour
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Equipment equipment;
        [SerializeField] private Transform floatingTextAnchor;
        [SerializeField, Tooltip("ScriptableObject containing the Lumberjack outfit configuration.")]
        private SkillingOutfitDefinition woodcuttingOutfitDefinition;

        private const string WoodcuttingOutfitResourcePath = "Skills/Outfits/WoodcuttingOutfitDefinition";

        private TreeNode currentTree;
        private AxeDefinition currentAxe;
        private readonly TickProgressTracker chopProgressTracker = new TickProgressTracker();

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
        public int CurrentChopIntervalTicks => chopProgressTracker.RequiredTicks;
        public AxeDefinition CurrentAxe => currentAxe;
        public float ChopProgressNormalized
        {
            get
            {
                int required = chopProgressTracker.RequiredTicks;
                if (required <= 1)
                    return 0f;

                return Mathf.Clamp01((float)chopProgressTracker.ProgressTicks / (required - 1));
            }
        }

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            skills = GetComponent<SkillManager>();
            chopProgressTracker.TickAdvanced += HandleChopProgressAdvanced;
            PreloadLogItems();
            woodcuttingOutfit = SkillingOutfitInitializer.InitializeOutfitProgress(
                ref woodcuttingOutfitDefinition,
                WoodcuttingOutfitResourcePath,
                nameof(WoodcuttingSkill),
                this);
        }

        private void OnDestroy()
        {
            SkillingOutfitProgress.Unregister(woodcuttingOutfit);
            woodcuttingOutfit = null;
        }

        protected override void HandleTick()
        {
            if (!IsChopping)
                return;

            if (currentTree == null || currentTree.IsDepleted)
            {
                StopChopping();
                return;
            }

            if (chopProgressTracker.Advance())
            {
                AttemptChop();
                if (IsChopping)
                    chopProgressTracker.Reset(chopProgressTracker.RequiredTicks);
            }
        }

        private void AttemptChop()
        {
            int requiredLevel = currentTree != null && currentTree.def != null
                ? currentTree.def.RequiredWoodcuttingLevel
                : 1;
            float chance = GatheringRewardContextBuilder.CalculateSuccessChance(new GatheringRewardContextBuilder.SuccessChanceArgs
            {
                PlayerLevel = skills.GetLevel(SkillType.Woodcutting),
                RequiredLevel = requiredLevel,
                ToolBonus = currentAxe != null ? currentAxe.Power * 0.01f : 0f
            });

            if (Random.value <= chance)
            {
                string logId = currentTree.def.LogItemId;
                var item = GatheringInventoryHelper.GetItemData(logId, ref logItems);
                int amount = PetDropSystem.ActivePet?.id == "Beaver" ? 2 : 1;
                var petStorage = PetDropSystem.ActivePet?.id == "Beaver" && PetDropSystem.ActivePetObject != null
                    ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                    : null;
                string logName = item != null ? item.itemName : currentTree.def.DisplayName;

                var context = GatheringRewardContextBuilder.BuildContext(new GatheringRewardContextBuilder.ContextArgs
                {
                    Runner = this,
                    Skills = skills,
                    SkillType = SkillType.Woodcutting,
                    Inventory = inventory,
                    PetStorage = petStorage,
                    Item = item,
                    RewardDisplayName = logName,
                    Quantity = amount,
                    XpPerItem = currentTree.def.XpPerLog,
                    PetAssistExtraQuantity = amount - 1,
                    FloatingTextAnchor = floatingTextAnchor,
                    FallbackAnchor = transform,
                    ResourcePosition = currentTree != null ? currentTree.transform.position : (Vector3?)null,
                    Equipment = equipment,
                    EquipmentXpBonusEvaluator = data => data != null ? data.woodcuttingXpBonusMultiplier : 0f,
                    RewardMessageFormatter = qty => $"+{qty} {logName}",
                    OnItemsGranted = result => OnLogGained?.Invoke(logId, result.QuantityAwarded),
                    OnSuccess = result =>
                    {
                        int? petChance = currentTree != null && currentTree.def != null
                            ? currentTree.def.PetDropChance
                            : (int?)null;
                        SkillingPetRewarder.TryRollPet(
                            "woodcutting",
                            skills,
                            currentTree != null ? currentTree.transform : transform,
                            petChance);

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
                    OnFailure = _ => StopChopping(),
                    LevelUpFloatingTextFormatter = result => $"Woodcutting level {result.NewLevel}",
                    OnLevelUp = level => OnLevelUp?.Invoke(level)
                });

                var rewardResult = GatheringRewardProcessor.Process(context);
                if (!rewardResult.Success)
                    return;

                currentTree.OnLogChopped();
                LogDebug($"Chopped {logName} x{amount} (chance {chance:P2})");
                if (currentTree.IsDepleted)
                    StopChopping();
            }
            else
            {
                LogDebug($"Failed to chop {currentTree?.name ?? "unknown tree"} (chance {chance:P2})");
            }
        }

        public void StartChopping(TreeNode tree, AxeDefinition axe)
        {
            if (tree == null || axe == null)
                return;

            currentTree = tree;
            currentAxe = axe;
            int intervalTicks = Mathf.Max(1, Mathf.RoundToInt(tree.def.ChopIntervalTicks / Mathf.Max(0.01f, axe.SwingSpeedMultiplier)));
            chopProgressTracker.Reset(intervalTicks);
            LogDebug($"Started chopping {tree.name} with {axe.DisplayName}");
            currentTree.IsBusy = true;
            OnStartChopping?.Invoke(tree);
        }

        public void StopChopping()
        {
            if (!IsChopping)
                return;

            LogDebug("Stopped chopping");
            if (currentTree != null)
                currentTree.IsBusy = false;
            currentTree = null;
            currentAxe = null;
            chopProgressTracker.Reset(0);
            OnStopChopping?.Invoke();
        }

        public bool CanAddLog(TreeDefinition tree)
        {
            if (tree == null)
                return true;

            return GatheringInventoryHelper.CanAcceptGatheredItem(
                inventory,
                tree.LogItemId,
                "Beaver",
                ref logItems,
                out _);
        }

        /// <summary>
        /// Debug helper to directly set the woodcutting level via the SkillManager.
        /// </summary>
        public void DebugSetLevel(int newLevel)
        {
            skills?.DebugSetLevel(SkillType.Woodcutting, Mathf.Clamp(newLevel, 1, 99));
            OnLevelUp?.Invoke(Level);
        }

        private bool TryAwardWoodcuttingOutfitPiece()
        {
            return SkillingOutfitRewarder.TryAwardPiece(
                woodcuttingOutfit,
                inventory,
                BankUI.Instance,
                Random.Range,
                "Woodcutting",
                "You've received a piece of woodcutting outfit",
                "A piece of woodcutting outfit has been added to your bank",
                Level);
        }

        private void PreloadLogItems()
        {
            GatheringInventoryHelper.EnsureItemCache(ref logItems);
        }

        /// <summary>
        ///     Emits a formatted debug message when <see cref="enableDebugLogging"/> is enabled.
        /// </summary>
        /// <param name="message">Message to output to the Unity console.</param>
        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[WoodcuttingSkill] {message}");
        }

        private void HandleChopProgressAdvanced(int progress, int required)
        {
            if (!IsChopping)
                return;

            LogDebug($"Woodcutting tick: {progress}/{required}");
        }
    }
}
