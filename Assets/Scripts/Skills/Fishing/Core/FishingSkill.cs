using System;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Util;
using UI;
using Pets;
using Core;
using BankSystem;
using Core.Save;
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
        private BycatchManager bycatchManager;

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
            if (bycatchManager == null)
                bycatchManager = GameManager.BycatchManager;
        }

        private void OnDestroy()
        {
            SaveManager.Unregister(fishingOutfit);
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
                StopFishing();
                return;
            }
            if (!string.IsNullOrEmpty(currentSpot.def.BaitItemId))
            {
                Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
                if (inventory == null || !inventory.RemoveItem(currentSpot.def.BaitItemId))
                {
                    FloatingText.Show("You need bait", anchor.position);
                    StopFishing();
                    return;
                }
                var baitItem = ItemDatabase.GetItem(currentSpot.def.BaitItemId);
                if (baitItem != null)
                    FloatingText.Show($"-1 {baitItem.itemName}", anchor.position);
                else
                    FloatingText.Show("-1 bait", anchor.position);
            }

            float baseChance = 0.35f;
            float penalty = 0.0025f * Mathf.Max(fish.RequiredLevel - 1, 0);
            int level = skills.GetLevel(SkillType.Fishing);
            float chance = baseChance + (level * 0.005f) + currentTool.CatchBonus * 0.01f - penalty;
            chance = Mathf.Clamp(chance, 0.05f, 0.90f);

            if (UnityEngine.Random.value <= chance)
            {
                int amount = PetDropSystem.ActivePet?.id == "Heron" ? 2 : 1;
                var item = GatheringInventoryHelper.GetItemData(fish.ItemId, ref fishItems);
                var petStorage = PetDropSystem.ActivePet?.id == "Heron" && PetDropSystem.ActivePetObject != null
                    ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                    : null;
                var waterType = currentSpot != null && currentSpot.def != null ? currentSpot.def.WaterType : WaterType.Any;

                var context = new GatheringRewardContext
                {
                    runner = this,
                    skills = skills,
                    skillType = SkillType.Fishing,
                    inventory = inventory,
                    petStorage = petStorage,
                    item = item,
                    rewardDisplayName = fish.DisplayName,
                    quantity = amount,
                    xpPerItem = fish.Xp,
                    petAssistExtraQuantity = Mathf.Max(0, amount - 1),
                    floatingTextAnchor = floatingTextAnchor,
                    fallbackAnchor = transform,
                    equipment = equipment,
                    equipmentXpBonusEvaluator = data =>
                        data != null && (data.fishingXpBonusWaterTypes & waterType) != 0
                            ? data.fishingXpBonusMultiplier
                            : 0f,
                    showItemFloatingText = true,
                    showXpPopup = true,
                    xpPopupDelayTicks = 5f,
                    rewardMessageFormatter = qty => $"+{qty} {fish.DisplayName}",
                    onItemsGranted = result => OnFishCaught?.Invoke(fish.Id, result.QuantityAwarded),
                    onXpApplied = result =>
                    {
                        if (result.LeveledUp && result.Anchor != null)
                        {
                            FloatingText.Show($"Fishing level {result.NewLevel}", result.Anchor.position);
                            OnLevelUp?.Invoke(result.NewLevel);
                        }
                    },
                    onSuccess = result =>
                    {
                        TryRollBycatch(result.Anchor);
                        TryAwardFishingOutfitPiece();
                    },
                    onFailure = _ => StopFishing()
                };

                var rewardResult = GatheringRewardProcessor.Process(context);
                if (!rewardResult.Success)
                    return;

                currentSpot.OnFishCaught();
                if (currentSpot.IsDepleted)
                    StopFishing();
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
            int streak = bycatchManager.GetStreak(waterType);
            int playerIdHash = gameObject.GetInstanceID();
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
                FloatingText.Show("Your inventory is full", anchor.position);
                return;
            }

            FloatingText.Show($"+{res.quantity} {res.item.DisplayName}", anchor.position);
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
                int seed = DateTime.UtcNow.Date.GetHashCode();
                seed = HashCombine(seed, ctx.playerIdHash);
                seed = HashCombine(seed, ctx.nodeHash);
                seed = HashCombine(seed, ctx.rollIndex);
                return new System.Random(seed);
            }

            return new System.Random();
        }

        private static int HashCombine(int a, int b)
        {
            unchecked
            {
                return (a * 397) ^ b;
            }
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
                    FloatingText.Show("You need bait", anchor.position);
                    return;
                }
            }
            currentSpot = spot;
            currentTool = tool;
            catchProgress = 0;
            currentIntervalTicks = Mathf.Max(1, Mathf.RoundToInt(spot.def.CatchIntervalTicks / Mathf.Max(0.01f, tool.SwingSpeedMultiplier)));
            currentSpot.IsBusy = true;
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
    }
}
