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
using Skills.Outfits;
using Random = UnityEngine.Random;
using UI;
using UI.Chat;

namespace Skills.Mining
{
    /// <summary>
    /// Handles XP, level, and mining tick logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningSkill : DebuggableTickedSkillBehaviour
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Equipment equipment;
        [SerializeField] private Transform floatingTextAnchor;
        [Header("Hades Fragment Rewards")]
        [SerializeField, Tooltip("Unique item identifier for the Hades fragment drop.")]
        private string hadesFragmentItemId = "Hades Fragment";
        [SerializeField, Tooltip("Chance denominator for awarding a Hades fragment. A value of 50 represents a 1 in 50 roll.")]
        private int hadesFragmentDropDenominator = 50;
        [SerializeField, Tooltip("ScriptableObject containing the Prospector outfit configuration.")]
        private SkillingOutfitDefinition miningOutfitDefinition;

        private const string MiningOutfitResourcePath = "Skills/Outfits/MiningOutfitDefinition";

        private MineableRock currentRock;
        private PickaxeDefinition currentPickaxe;
        private readonly TickProgressTracker swingProgressTracker = new TickProgressTracker();

        private SkillManager skills;
        private Dictionary<string, ItemData> oreItems;
        private ItemData cachedHadesFragmentItem;
        private int questOreCount;
        private SkillingOutfitProgress miningOutfit;
        private bool useCompanionChatFormatting;
        private Func<string> companionChatSenderResolver;

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
        /// <summary>Inventory component used for storing mined ore.</summary>
        public Inventory.Inventory InventoryComponent => inventory;
        public float SwingProgressNormalized
        {
            get
            {
                int required = currentPickaxe != null ? Mathf.Max(1, currentPickaxe.SwingSpeedTicks) : 0;
                if (required <= 1)
                    return 0f;

                return Mathf.Clamp01((float)swingProgressTracker.ProgressTicks / (required - 1));
            }
        }

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            skills = GetComponent<SkillManager>();
            swingProgressTracker.TickAdvanced += HandleSwingProgressAdvanced;
            PreloadOreItems();
            miningOutfit = SkillingOutfitInitializer.InitializeOutfitProgress(
                ref miningOutfitDefinition,
                MiningOutfitResourcePath,
                nameof(MiningSkill),
                this);
        }

        private void OnDestroy()
        {
            SkillingOutfitProgress.Unregister(miningOutfit);
            miningOutfit = null;
        }

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

            if (swingProgressTracker.Advance())
            {
                AttemptMine();
                if (IsMining && currentPickaxe != null)
                    swingProgressTracker.Reset(currentPickaxe.SwingSpeedTicks);
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
                    Vector3? resourcePosition = currentRock != null ? currentRock.transform.position : (Vector3?)null;

                    bool hadesInventoryFull;
                    int hadesFragmentsAwarded = 0;
                    if (ShouldRollHadesFragment())
                    {
                        hadesFragmentsAwarded = TryGrantHadesFragment(resourcePosition, out hadesInventoryFull);
                        if (hadesFragmentsAwarded > 0)
                            LogDebug("Awarded a Hades fragment from mining.");
                        else if (hadesInventoryFull)
                            LogDebug("Hades fragment roll succeeded but the inventory was full.");
                    }
                    else
                    {
                        hadesInventoryFull = false;
                    }

                    bool skipOreDueToHades = false;
                    if (hadesFragmentsAwarded > 0)
                    {
                        if (!GatheringInventoryHelper.CanAcceptGatheredItem(
                                inventory,
                                ore.Id,
                                "Rock Golem",
                                ref oreItems,
                                out _))
                        {
                            skipOreDueToHades = true;
                            LogDebug("Skipping ore reward to prioritise Hades fragment due to limited inventory space.");
                        }
                    }

                    int actualOreGranted = 0;
                    bool CustomAddOre(int quantityToAdd)
                    {
                        if (quantityToAdd <= 0)
                            return true;

                        if (skipOreDueToHades)
                            return true;

                        if (item != null && inventory != null && inventory.AddItem(item, quantityToAdd))
                        {
                            actualOreGranted += quantityToAdd;
                            return true;
                        }

                        if (item != null && petStorage != null && petStorage.StoreItem(item, quantityToAdd))
                        {
                            actualOreGranted += quantityToAdd;
                            return true;
                        }

                        return false;
                    }

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
                        ResourcePosition = resourcePosition,
                        Equipment = equipment,
                        EquipmentXpBonusEvaluator = data => data != null ? data.miningXpBonusMultiplier : 0f,
                        CustomAddItemHandler = CustomAddOre,
                        RewardMessageFormatter = qty => $"+{qty} {ore.DisplayName}",
                        InventoryFullMessage = null,
                        UseCompanionChatFormatting = useCompanionChatFormatting,
                        CompanionChatSenderResolver = companionChatSenderResolver,
                        ShowItemFloatingText = false,
                        OnItemsGranted = _ =>
                        {
                            if (actualOreGranted > 0)
                            {
                                OnOreGained?.Invoke(ore.Id, actualOreGranted);
                                PublishOreGatherMessage(actualOreGranted, oreName, resourcePosition);
                            }
                        },
                        OnSuccess = _ =>
                        {
                            int? petChance = ore != null ? ore.PetDropChance : (int?)null;
                            SkillingPetRewarder.TryRollPet(
                                "mining",
                                skills,
                                currentRock != null ? currentRock.transform : transform,
                                petChance);

                            if (QuestManager.Instance != null && QuestManager.Instance.IsQuestActive("ToolsOfSurvival") && actualOreGranted > 0)
                            {
                                var quest = QuestManager.Instance.GetQuest("ToolsOfSurvival");
                                var step = quest?.Steps.Find(s => s.StepID == "MineOres");
                                if (step != null && !step.IsComplete)
                                {
                                    questOreCount += actualOreGranted;
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

                    string hadesSuffix = hadesFragmentsAwarded > 0 ? " + Hades fragment" : string.Empty;
                    LogDebug($"Mined {oreName} x{actualOreGranted} (chance {chance:P2}){hadesSuffix}");
                }

                if (currentRock.IsDepleted)
                    StopMining();
            }
            else
            {
                LogDebug($"Failed to mine {currentRock?.name ?? "unknown rock"} (chance {chance:P2})");
            }
        }

        public void StartMining(MineableRock rock, PickaxeDefinition pickaxe)
        {
            if (rock == null || pickaxe == null)
                return;

            currentRock = rock;
            currentPickaxe = pickaxe;
            swingProgressTracker.Reset(Mathf.Max(1, pickaxe.SwingSpeedTicks));
            LogDebug($"Started mining {rock.name} with {pickaxe.DisplayName}");
            OnStartMining?.Invoke(rock);
        }

        public void StopMining()
        {
            if (!IsMining)
                return;

            LogDebug("Stopped mining");
            currentRock = null;
            currentPickaxe = null;
            swingProgressTracker.Reset(0);
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
        /// Configures the chat formatting used for gathering messages. When provided, the companion
        /// name resolver routes mining rewards through the Companion chat channel instead of Game.
        /// Passing a null resolver restores the default player-centric formatting.
        /// </summary>
        /// <param name="senderResolver">Resolver that supplies the display name for companion chat output.</param>
        public void ConfigureCompanionChat(Func<string> senderResolver)
        {
            useCompanionChatFormatting = senderResolver != null;
            companionChatSenderResolver = senderResolver;
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
                "A piece of mining outfit has been added to your bank",
                Level);
        }

        /// <summary>
        /// Determines whether the current mining action should grant a Hades fragment.
        /// Uses a simple 1-in-N roll where the denominator is configurable through the inspector.
        /// </summary>
        /// <returns><c>true</c> when the fragment should be granted, otherwise <c>false</c>.</returns>
        private bool ShouldRollHadesFragment()
        {
            if (hadesFragmentDropDenominator <= 0)
                return false;

            int roll = Random.Range(1, hadesFragmentDropDenominator + 1);
            return roll == 1;
        }

        /// <summary>
        /// Resolves and caches the <see cref="ItemData"/> associated with the configured Hades fragment identifier.
        /// The lookup is cached so repeated mining rewards avoid additional Resources loads.
        /// </summary>
        /// <returns>Cached fragment item when available, otherwise <c>null</c>.</returns>
        private ItemData GetHadesFragmentItem()
        {
            if (string.IsNullOrWhiteSpace(hadesFragmentItemId))
                return null;

            if (cachedHadesFragmentItem != null && cachedHadesFragmentItem.id == hadesFragmentItemId)
                return cachedHadesFragmentItem;

            cachedHadesFragmentItem = GatheringInventoryHelper.GetItemData(hadesFragmentItemId, ref oreItems);
            return cachedHadesFragmentItem;
        }

        /// <summary>
        /// Attempts to grant a Hades fragment to the active inventory using the shared gathering reward processor.
        /// </summary>
        /// <param name="resourcePosition">World position of the mined rock for floating text placement.</param>
        /// <param name="inventoryFull">Outputs whether the inventory rejected the item due to capacity constraints.</param>
        /// <returns>The number of fragments successfully awarded (0 or 1).</returns>
        private int TryGrantHadesFragment(Vector3? resourcePosition, out bool inventoryFull)
        {
            inventoryFull = false;
            var fragmentItem = GetHadesFragmentItem();
            if (fragmentItem == null || inventory == null)
                return 0;

            int fragmentsGranted = 0;
            var context = GatheringRewardContextBuilder.BuildContext(new GatheringRewardContextBuilder.ContextArgs
            {
                Runner = this,
                Skills = skills,
                SkillType = SkillType.Mining,
                Inventory = inventory,
                PetStorage = null,
                Item = fragmentItem,
                RewardDisplayName = fragmentItem.itemName,
                Quantity = 1,
                XpPerItem = 0f,
                PetAssistExtraQuantity = 0,
                FloatingTextAnchor = floatingTextAnchor,
                FallbackAnchor = transform,
                ResourcePosition = resourcePosition,
                Equipment = equipment,
                EquipmentXpBonusEvaluator = _ => 0f,
                CustomAddItemHandler = quantity =>
                {
                    if (quantity <= 0)
                        return true;

                    if (inventory.AddItem(fragmentItem, quantity))
                    {
                        fragmentsGranted += quantity;
                        return true;
                    }

                    return false;
                },
                RewardMessageFormatter = qty => $"+{qty} {fragmentItem.itemName}",
                InventoryFullMessage = "Your inventory is too full to hold the Hades fragment.",
                UseCompanionChatFormatting = useCompanionChatFormatting,
                CompanionChatSenderResolver = companionChatSenderResolver,
                ShowXpPopup = false,
                OnItemsGranted = _ => { },
                OnSuccess = _ => { }
            });

            var result = GatheringRewardProcessor.Process(context);
            if (!result.Success)
            {
                inventoryFull = result.InventoryFull;
                return 0;
            }

            inventoryFull = false;
            return Mathf.Max(0, fragmentsGranted);
        }

        /// <summary>
        /// Publishes floating text and chat output describing the ore gathered with the correct quantity.
        /// This helper mirrors <see cref="GatheringRewardProcessor"/> behaviour while honouring companion chat formatting.
        /// </summary>
        /// <param name="quantity">Number of ore items that were successfully stored.</param>
        /// <param name="displayName">Display name to surface in UI/chat messages.</param>
        /// <param name="resourcePosition">Optional world position of the mined node for floating text alignment.</param>
        private void PublishOreGatherMessage(int quantity, string displayName, Vector3? resourcePosition)
        {
            if (quantity <= 0 || string.IsNullOrWhiteSpace(displayName))
                return;

            string message = $"+{quantity} {displayName}";
            Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
            if (anchor != null)
            {
                bool displayed = false;
                if (resourcePosition.HasValue)
                    displayed = GatheringFloatingTextService.TryShowNow(message, anchor, resourcePosition.Value);

                if (!displayed && !resourcePosition.HasValue)
                    GatheringFloatingTextService.TryShowAtAnchor(message, anchor);
            }

            PublishGatheringChatMessage(message);
        }

        /// <summary>
        /// Emits the supplied message to the appropriate chat channel, respecting companion formatting when active.
        /// </summary>
        /// <param name="message">Text that should be published to the chat service.</param>
        private void PublishGatheringChatMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var chatService = ChatService.Instance;
            if (chatService == null)
                return;

            if (useCompanionChatFormatting)
            {
                string sender = companionChatSenderResolver != null ? companionChatSenderResolver.Invoke() : null;
                if (string.IsNullOrWhiteSpace(sender))
                    sender = "Companion";

                chatService.PublishCompanionMessage(sender, message);
            }
            else
            {
                chatService.PublishGameMessage(message);
            }
        }

        private void PreloadOreItems()
        {
            GatheringInventoryHelper.EnsureItemCache(ref oreItems);
        }

        /// <summary>
        ///     Emits a formatted debug message when <see cref="enableDebugLogging"/> is enabled.
        /// </summary>
        /// <param name="message">Message to output to the Unity console.</param>
        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[MiningSkill] {message}");
        }

        private void HandleSwingProgressAdvanced(int progress, int required)
        {
            if (!IsMining)
                return;

            LogDebug($"Mining tick: {progress}/{required}");
        }
    }
}
