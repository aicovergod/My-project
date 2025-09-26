using System;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Util;
using Pets;
using Core;
using BankSystem;
using Core.Save;
using Core.Time;
using Skills.Outfits;
using Skills.Common;

namespace Skills.Fishing
{
    [DisallowMultipleComponent]
    public class FishingSkill : TickedSkillBehaviour
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Equipment equipment;
        [SerializeField] private Transform floatingTextAnchor;
        private const string DefaultBycatchProfileKey = "anon-profile";
        private static readonly int DefaultBycatchProfileHash = ComputeStableStringHash(DefaultBycatchProfileKey);

        private BycatchManager bycatchManager;
        private bool waitingForServices;

        [SerializeField, Tooltip("Enables verbose debug logging for fishing actions.")]
        private bool enableDebugLogging;

        private FishableSpot currentSpot;
        private FishingToolDefinition currentTool;
        private int catchProgress;
        private int currentIntervalTicks;
        private int bycatchRollIndex;
        private int consecutiveFails;

        private Dictionary<string, ItemData> fishItems;
        private SkillingOutfitProgress fishingOutfit;

        public event System.Action<FishableSpot> OnStartFishing;
        public event System.Action OnStopFishing;
        public event System.Action<string, int> OnFishCaught;
        public event System.Action<int> OnLevelUp;

        public int Level => skills != null ? skills.GetLevel(SkillType.Fishing) : 1;
        public float Xp => skills != null ? skills.GetXp(SkillType.Fishing) : 0f;
        public bool IsFishing => currentSpot != null;
        public FishableSpot CurrentSpot => currentSpot;
        public FishingToolDefinition CurrentTool => currentTool;
        public float CatchProgressNormalized => currentIntervalTicks <= 1 ? 0f : (float)catchProgress / (currentIntervalTicks - 1);
        public int CurrentCatchIntervalTicks => currentIntervalTicks;

        /// <summary>
        ///     Gets or sets the runtime flag controlling verbose debug logging for this skill.
        /// </summary>
        public bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set => enableDebugLogging = value;
        }

        private SkillManager skills;

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            skills = GetComponent<SkillManager>();
            PreloadFishItems();
            fishingOutfit = new SkillingOutfitProgress(new[]
            {
                "Fishing Helmet",
                "Fishing Top",
                "Fishing Pants",
                "Fishing Boots",
                "Fishing Gloves"
            }, "FishingOutfitOwned");
            TryResolveBycatchManager();
            if (bycatchManager == null)
                SubscribeToServicesReady();
        }

        private void OnDestroy()
        {
            if (waitingForServices)
            {
                GameManager.ServicesReady -= HandleGameManagerServicesReady;
                waitingForServices = false;
            }
            SaveManager.Unregister(fishingOutfit);
        }

        protected override bool LogTickerSubscription => enableDebugLogging;

        /// <summary>
        /// Attempts to resolve the bycatch manager from the <see cref="GameManager"/> when available
        /// and falls back to a direct scene search if necessary.
        /// </summary>
        private void TryResolveBycatchManager()
        {
            if (bycatchManager != null)
                return;

            if (GameManager.Instance != null)
                bycatchManager = GameManager.BycatchManager;

            if (bycatchManager == null)
                bycatchManager = FindObjectOfType<BycatchManager>(true);
        }

        /// <summary>
        /// Hooks into <see cref="GameManager.ServicesReady"/> so the bycatch manager can be cached
        /// once the core services have finished bootstrapping.
        /// </summary>
        private void SubscribeToServicesReady()
        {
            if (waitingForServices)
                return;

            GameManager.ServicesReady -= HandleGameManagerServicesReady;
            GameManager.ServicesReady += HandleGameManagerServicesReady;
            waitingForServices = true;
        }

        /// <summary>
        /// Handles the <see cref="GameManager.ServicesReady"/> event by caching the bycatch manager
        /// and removing the subscription to avoid duplicate callbacks.
        /// </summary>
        private void HandleGameManagerServicesReady()
        {
            TryResolveBycatchManager();

            if (!waitingForServices)
                return;

            GameManager.ServicesReady -= HandleGameManagerServicesReady;
            waitingForServices = false;
        }

        protected override void HandleTick()
        {
            if (!IsFishing)
                return;
            if (currentSpot == null || currentSpot.IsDepleted)
            {
                StopFishing();
                return;
            }
            catchProgress++;
            LogDebug($"Fishing tick: {catchProgress}/{currentIntervalTicks}");
            if (catchProgress >= currentIntervalTicks)
            {
                catchProgress = 0;
                AttemptCatch();
            }
        }

        private void AttemptCatch()
        {
            var fish = GetRandomFish(currentSpot.def);
            if (fish == null)
            {
                LogDebug("No eligible fish could be selected; stopping.");
                StopFishing();
                return;
            }
            Vector3? spotPosition = currentSpot != null ? currentSpot.transform.position : (Vector3?)null;
            if (!string.IsNullOrEmpty(currentSpot.def.BaitItemId))
            {
                Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
                if (inventory == null || !inventory.RemoveItem(currentSpot.def.BaitItemId))
                {
                    bool displayed = false;
                    if (spotPosition.HasValue)
                        displayed = GatheringFloatingTextService.TryShowNow("You need bait", anchor, spotPosition.Value);

                    if (!displayed && !spotPosition.HasValue)
                        GatheringFloatingTextService.TryShowAtAnchor("You need bait", anchor);
                    LogDebug("Bait requirement not met; cancelling fishing session.");
                    StopFishing();
                    return;
                }
                var baitItem = ItemDatabase.GetItem(currentSpot.def.BaitItemId);
                if (baitItem != null)
                {
                    bool displayed = false;
                    string message = $"-1 {baitItem.itemName}";
                    if (spotPosition.HasValue)
                        displayed = GatheringFloatingTextService.TryShowNow(message, anchor, spotPosition.Value);

                    if (!displayed && !spotPosition.HasValue)
                        GatheringFloatingTextService.TryShowAtAnchor(message, anchor);
                }
                else
                {
                    const string fallbackMessage = "-1 bait";
                    bool displayed = false;
                    if (spotPosition.HasValue)
                        displayed = GatheringFloatingTextService.TryShowNow(fallbackMessage, anchor, spotPosition.Value);

                    if (!displayed && !spotPosition.HasValue)
                        GatheringFloatingTextService.TryShowAtAnchor(fallbackMessage, anchor);
                }
            }

            float chance = GatheringRewardContextBuilder.CalculateSuccessChance(new GatheringRewardContextBuilder.SuccessChanceArgs
            {
                PlayerLevel = skills.GetLevel(SkillType.Fishing),
                RequiredLevel = fish.RequiredLevel,
                ToolBonus = currentTool != null ? currentTool.CatchBonus * 0.01f : 0f
            });

            if (UnityEngine.Random.value <= chance)
            {
                int amount = PetDropSystem.ActivePet?.id == "Heron" ? 2 : 1;
                var item = GatheringInventoryHelper.GetItemData(fish.ItemId, ref fishItems);
                var petStorage = PetDropSystem.ActivePet?.id == "Heron" && PetDropSystem.ActivePetObject != null
                    ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                    : null;
                var waterType = currentSpot != null && currentSpot.def != null ? currentSpot.def.WaterType : WaterType.Any;

                var context = GatheringRewardContextBuilder.BuildContext(new GatheringRewardContextBuilder.ContextArgs
                {
                    Runner = this,
                    Skills = skills,
                    SkillType = SkillType.Fishing,
                    Inventory = inventory,
                    PetStorage = petStorage,
                    Item = item,
                    RewardDisplayName = fish.DisplayName,
                    Quantity = amount,
                    XpPerItem = fish.Xp,
                    PetAssistExtraQuantity = amount - 1,
                    FloatingTextAnchor = floatingTextAnchor,
                    FallbackAnchor = transform,
                    ResourcePosition = spotPosition,
                    Equipment = equipment,
                    EquipmentXpBonusEvaluator = data =>
                        data != null && (data.fishingXpBonusWaterTypes & waterType) != 0
                            ? data.fishingXpBonusMultiplier
                            : 0f,
                    RewardMessageFormatter = qty => $"+{qty} {fish.DisplayName}",
                    OnItemsGranted = result => OnFishCaught?.Invoke(fish.Id, result.QuantityAwarded),
                    OnSuccess = result =>
                    {
                        int? petChance = fish != null ? fish.PetDropChance : (int?)null;
                        Transform petAnchor = currentSpot != null
                            ? currentSpot.transform
                            : result.Anchor != null ? result.Anchor : transform;
                        SkillingPetRewarder.TryRollPet("fishing", skills, petAnchor, petChance);
                        TryRollBycatch(result.Anchor);
                        TryAwardFishingOutfitPiece();
                    },
                    OnFailure = _ => StopFishing(),
                    LevelUpFloatingTextFormatter = result => $"Fishing level {result.NewLevel}",
                    OnLevelUp = level => OnLevelUp?.Invoke(level)
                });

                var rewardResult = GatheringRewardProcessor.Process(context);
                if (!rewardResult.Success)
                    return;

                currentSpot.OnFishCaught();
                LogDebug($"Caught {fish.DisplayName} x{amount} (chance {chance:P2})");
                if (currentSpot.IsDepleted)
                    StopFishing();
            }
            else
            {
                LogDebug($"Failed to catch fish at {currentSpot?.name ?? "unknown spot"} (chance {chance:P2})");
            }
        }

        private FishDefinition GetRandomFish(FishingSpotDefinition spot)
        {
            if (spot == null) return null;
            var eligible = new List<FishDefinition>();
            int level = skills.GetLevel(SkillType.Fishing);
            foreach (var f in spot.AvailableFish)
            {
                if (f != null && level >= f.RequiredLevel)
                    eligible.Add(f);
            }
            if (eligible.Count == 0)
                return null;
            return eligible[UnityEngine.Random.Range(0, eligible.Count)];
        }

        private void TryRollBycatch(Transform anchor)
        {
            if (bycatchManager == null || currentSpot == null || currentTool == null)
                return;

            var waterType = currentSpot.def != null ? currentSpot.def.WaterType : WaterType.Any;
            Vector3? spotPosition = currentSpot != null ? currentSpot.transform.position : (Vector3?)null;
            int streak = bycatchManager.GetStreak(waterType);
            string profileId = SaveManager.ActiveProfileId;
            int playerIdHash = !string.IsNullOrEmpty(profileId)
                ? ComputeStableStringHash(profileId)
                : DefaultBycatchProfileHash;
            int nodeHash = currentSpot.def != null ? currentSpot.def.Id.GetHashCode() : currentSpot.GetInstanceID();

            int chanceRollIndex = bycatchRollIndex++;
            int level = skills.GetLevel(SkillType.Fishing);
            var ctx = new BycatchRollContext
            {
                playerLevel = level,
                hasBait = !string.IsNullOrEmpty(currentSpot.def.BaitItemId),
                waterType = waterType,
                tool = MapTool(currentTool),
                luck = 0f,
                spotRarityMultiplier = 1f,
                noRareStreakForThisWater = streak,
                playerIdHash = playerIdHash,
                nodeHash = nodeHash,
                rollIndex = chanceRollIndex
            };

            int L = Mathf.Clamp(level, 1, 99);
            float t = (L - 1f) / 98f;
            float baseChance = Mathf.Lerp(0.015f, 0.10f, t);
            float pityBonus = consecutiveFails >= 50 ? (consecutiveFails - 49) * 0.01f : 0f;
            float gearBonus = 0f;
            if (equipment != null)
            {
                foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                {
                    if (slot == EquipmentSlot.None)
                        continue;
                    var entry = equipment.GetEquipped(slot);
                    if (entry.item != null)
                        gearBonus += entry.item.bycatchChanceBonus;
                }
            }
            float finalChance = Mathf.Clamp01(baseChance + pityBonus + gearBonus);

            var rng = CreateRng(ctx);
            bool success = rng.NextDouble() < finalChance;
            if (!success)
            {
                consecutiveFails++;
                bycatchManager.ApplyStreakResult(waterType, BycatchResult.None);
                if (BycatchManager.DebugBycatchRolls)
                    Debug.Log($"[Bycatch] roll {ctx.rollIndex} lvl={ctx.playerLevel} bait={ctx.hasBait} water={ctx.waterType} tool={ctx.tool} streak={streak} chance={finalChance * 100f:F2}% -> no bycatch");
                return;
            }

            ctx.rollIndex = bycatchRollIndex++;
            var res = bycatchManager.Roll(ctx);
            if (BycatchManager.DebugBycatchRolls)
            {
                string result = res.IsNone
                    ? "no bycatch"
                    : $"{res.item.DisplayName} x{res.quantity} ({res.Rarity})";
                Debug.Log($"[Bycatch] roll {ctx.rollIndex} lvl={ctx.playerLevel} bait={ctx.hasBait} water={ctx.waterType} tool={ctx.tool} streak={streak} chance={finalChance * 100f:F2}% -> {result}");
            }

            bycatchManager.ApplyStreakResult(waterType, res);
            if (res.IsNone)
            {
                consecutiveFails++;
                return;
            }

            consecutiveFails = 0;
            var itemData = ItemDatabase.GetItem(res.item.ItemId);
            if (itemData == null || inventory == null || !inventory.AddItem(itemData, res.quantity))
            {
                bool displayed = false;
                if (spotPosition.HasValue)
                    displayed = GatheringFloatingTextService.TryShowNow("Your inventory is full", anchor, spotPosition.Value);

                if (!displayed && !spotPosition.HasValue)
                    GatheringFloatingTextService.TryShowAtAnchor("Your inventory is full", anchor);
                return;
            }

            string message = $"+{res.quantity} {res.item.DisplayName}";
            bool rewardDisplayed = false;
            if (spotPosition.HasValue)
                rewardDisplayed = GatheringFloatingTextService.TryShowNow(message, anchor, spotPosition.Value);

            if (!rewardDisplayed && !spotPosition.HasValue)
                GatheringFloatingTextService.TryShowAtAnchor(message, anchor);
        }

        /// <summary>
        ///     Computes a deterministic hash for the supplied string so bycatch RNG seeds
        ///     stay stable across sessions and platforms.
        /// </summary>
        /// <param name="value">Profile identifier that should be transformed into a hash.</param>
        /// <returns>Stable 32-bit hash derived from the provided string.</returns>
        private static int ComputeStableStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            const uint fnvOffsetBasis = 2166136261u;
            const uint fnvPrime = 16777619u;

            uint hash = fnvOffsetBasis;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return unchecked((int)hash);
        }

        private FishingTool MapTool(FishingToolDefinition tool)
        {
            if (tool == null)
                return FishingTool.Any;
            string name = tool.DisplayName?.Replace(" ", "");
            if (!string.IsNullOrEmpty(name) && Enum.TryParse<FishingTool>(name, true, out var res))
                return res;
            name = tool.Id?.Replace(" ", "");
            if (!string.IsNullOrEmpty(name) && Enum.TryParse<FishingTool>(name, true, out res))
                return res;
            return FishingTool.Any;
        }

        private System.Random CreateRng(in BycatchRollContext ctx)
        {
            if (bycatchManager != null && bycatchManager.useDailySeed)
            {
                int seed = DailyGameTimeService.ComposeDailySeed(stackalloc int[]
                {
                    ctx.playerIdHash,
                    ctx.nodeHash,
                    ctx.rollIndex
                });
                return new System.Random(seed);
            }

            return new System.Random();
        }

        public void StartFishing(FishableSpot spot, FishingToolDefinition tool)
        {
            if (spot == null || tool == null)
                return;
            if (!string.IsNullOrEmpty(spot.def.BaitItemId))
            {
                if (inventory == null || !inventory.HasItem(spot.def.BaitItemId))
                {
                    Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
                    Vector3? spotPosition = spot != null ? spot.transform.position : (Vector3?)null;
                    bool displayed = false;
                    if (spotPosition.HasValue)
                        displayed = GatheringFloatingTextService.TryShowNow("You need bait", anchor, spotPosition.Value);

                    if (!displayed && !spotPosition.HasValue)
                        GatheringFloatingTextService.TryShowAtAnchor("You need bait", anchor);
                    LogDebug("Unable to start fishing: missing bait.");
                    return;
                }
            }
            currentSpot = spot;
            currentTool = tool;
            catchProgress = 0;
            currentIntervalTicks = Mathf.Max(1, Mathf.RoundToInt(spot.def.CatchIntervalTicks / Mathf.Max(0.01f, tool.SwingSpeedMultiplier)));
            currentSpot.IsBusy = true;
            LogDebug($"Started fishing {spot.name} with {tool.DisplayName}");
            OnStartFishing?.Invoke(spot);
        }

        public void StopFishing()
        {
            if (!IsFishing)
                return;
            if (currentSpot != null)
                currentSpot.IsBusy = false;
            currentSpot = null;
            currentTool = null;
            catchProgress = 0;
            currentIntervalTicks = 0;
            LogDebug("Stopped fishing");
            OnStopFishing?.Invoke();
        }

        public bool CanAddFish(FishDefinition fish)
        {
            if (fish == null)
                return true;

            return GatheringInventoryHelper.CanAcceptGatheredItem(
                inventory,
                fish.ItemId,
                "Heron",
                ref fishItems,
                out _);
        }

        public void DebugSetLevel(int newLevel)
        {
            skills?.DebugSetLevel(SkillType.Fishing, Mathf.Clamp(newLevel, 1, 99));
            OnLevelUp?.Invoke(Level);
        }

        private bool TryAwardFishingOutfitPiece()
        {
            return SkillingOutfitRewarder.TryAwardPiece(
                fishingOutfit,
                inventory,
                BankUI.Instance,
                UnityEngine.Random.Range,
                "Fishing",
                "You've received a piece of fishing outfit",
                "A piece of fishing outfit has been added to your bank");
        }

        private void PreloadFishItems()
        {
            GatheringInventoryHelper.EnsureItemCache(ref fishItems);
        }

        /// <summary>
        ///     Emits a formatted debug message when <see cref="enableDebugLogging"/> is enabled.
        /// </summary>
        /// <param name="message">Message to output to the Unity console.</param>
        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[FishingSkill] {message}");
        }
    }
}
