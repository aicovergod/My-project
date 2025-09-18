using System;
using Inventory;
using Pets;
using Skills;
using UI;
using UnityEngine;

namespace Skills.Common
{
    /// <summary>
    /// Utility responsible for constructing <see cref="GatheringRewardContext"/> payloads with
    /// consistent defaults and composing the XP/floating text callbacks shared by gathering skills.
    /// Also exposes helpers for calculating the common success roll used by gathering actions.
    /// </summary>
    public static class GatheringRewardContextBuilder
    {
        /// <summary>
        /// Encapsulates the tunable parameters for the shared gathering success calculation.
        /// </summary>
        public struct SuccessChanceSettings
        {
            public float BaseChance;
            public float PerLevelBonus;
            public float PerLevelPenalty;
            public float MinChance;
            public float MaxChance;

            public static SuccessChanceSettings Default => new SuccessChanceSettings
            {
                BaseChance = 0.35f,
                PerLevelBonus = 0.005f,
                PerLevelPenalty = 0.0025f,
                MinChance = 0.05f,
                MaxChance = 0.90f
            };
        }

        /// <summary>
        /// Arguments describing a gathering success roll. Tool bonuses and any extra additive modifiers
        /// can be injected so individual skills do not need to reimplement the shared math.
        /// </summary>
        public struct SuccessChanceArgs
        {
            public int PlayerLevel;
            public int RequiredLevel;
            public float ToolBonus;
            public float AdditionalBonus;
            public float AdditionalPenalty;
            public SuccessChanceSettings? SettingsOverride;
        }

        /// <summary>
        /// Calculates the chance for a gathering attempt to succeed using the standard OSRS style
        /// curve shared by woodcutting, fishing, and mining. Supports optional overrides so future
        /// skills can tweak the constants without copying the boilerplate.
        /// </summary>
        public static float CalculateSuccessChance(in SuccessChanceArgs args)
        {
            var settings = args.SettingsOverride ?? SuccessChanceSettings.Default;
            int penaltyLevels = Mathf.Max(args.RequiredLevel - 1, 0);
            float baseChance = settings.BaseChance;
            float bonusFromLevel = args.PlayerLevel * settings.PerLevelBonus;
            float penalty = settings.PerLevelPenalty * penaltyLevels + args.AdditionalPenalty;
            float chance = baseChance + bonusFromLevel + args.ToolBonus + args.AdditionalBonus - penalty;
            return Mathf.Clamp(chance, settings.MinChance, settings.MaxChance);
        }

        /// <summary>
        /// Payload used to build a <see cref="GatheringRewardContext"/> with consistent defaults. Each
        /// skill fills in the parts that differ (items, quest hooks, etc.) while the builder handles the
        /// repetitive boilerplate.
        /// </summary>
        public struct ContextArgs
        {
            public MonoBehaviour Runner;
            public SkillManager Skills;
            public SkillType SkillType;
            public Inventory.Inventory Inventory;
            public PetStorage PetStorage;
            public ItemData Item;
            public string RewardDisplayName;
            public int Quantity;
            public float XpPerItem;
            public int PetAssistExtraQuantity;
            public Transform FloatingTextAnchor;
            public Transform FallbackAnchor;
            public Inventory.Equipment Equipment;
            public Func<ItemData, float> EquipmentXpBonusEvaluator;
            public Func<float> AdditionalXpBonusCalculator;
            public Func<int, bool> CustomAddItemHandler;
            public Func<int, string> RewardMessageFormatter;
            public string InventoryFullMessage;
            public bool? ShowItemFloatingText;
            public bool? ShowXpPopup;
            public float? XpPopupDelayTicks;
            public Action<GatheringRewardResult> OnItemsGranted;
            public Action<GatheringRewardResult> OnSuccess;
            public Action<GatheringRewardResult> OnFailure;
            public Action<GatheringRewardResult> OnXpApplied;
            public Action<GatheringRewardResult> OnXpAppliedBeforeLevelCheck;
            public Func<GatheringRewardResult, string> LevelUpFloatingTextFormatter;
            public Action<int> OnLevelUp;
        }

        /// <summary>
        /// Builds a <see cref="GatheringRewardContext"/> from the provided arguments while wiring up the
        /// shared XP popup logic and default floating text configuration.
        /// </summary>
        public static GatheringRewardContext BuildContext(in ContextArgs args)
        {
            var context = new GatheringRewardContext
            {
                runner = args.Runner,
                skills = args.Skills,
                skillType = args.SkillType,
                inventory = args.Inventory,
                petStorage = args.PetStorage,
                item = args.Item,
                rewardDisplayName = args.RewardDisplayName,
                quantity = Mathf.Max(0, args.Quantity),
                xpPerItem = args.XpPerItem,
                petAssistExtraQuantity = Mathf.Max(0, args.PetAssistExtraQuantity),
                floatingTextAnchor = args.FloatingTextAnchor,
                fallbackAnchor = args.FallbackAnchor,
                equipment = args.Equipment,
                equipmentXpBonusEvaluator = args.EquipmentXpBonusEvaluator,
                additionalXpBonusCalculator = args.AdditionalXpBonusCalculator,
                customAddItemHandler = args.CustomAddItemHandler,
                rewardMessageFormatter = args.RewardMessageFormatter,
                inventoryFullMessage = args.InventoryFullMessage,
                showItemFloatingText = args.ShowItemFloatingText ?? true,
                showXpPopup = args.ShowXpPopup ?? true,
                xpPopupDelayTicks = args.XpPopupDelayTicks ?? 5f,
                onItemsGranted = args.OnItemsGranted,
                onSuccess = args.OnSuccess,
                onFailure = args.OnFailure,
                onXpApplied = ComposeXpAppliedDelegate(args)
            };

            return context;
        }

        private static Action<GatheringRewardResult> ComposeXpAppliedDelegate(in ContextArgs args)
        {
            bool hasCallbacks = args.OnXpAppliedBeforeLevelCheck != null ||
                                args.OnXpApplied != null ||
                                args.LevelUpFloatingTextFormatter != null ||
                                args.OnLevelUp != null;

            if (!hasCallbacks)
                return null;

            return result =>
            {
                args.OnXpAppliedBeforeLevelCheck?.Invoke(result);
                args.OnXpApplied?.Invoke(result);

                if (result.LeveledUp)
                {
                    if (args.LevelUpFloatingTextFormatter != null && result.Anchor != null)
                    {
                        string message = args.LevelUpFloatingTextFormatter.Invoke(result);
                        if (!string.IsNullOrEmpty(message))
                            FloatingText.Show(message, result.Anchor.position);
                    }

                    args.OnLevelUp?.Invoke(result.NewLevel);
                }
            };
        }
    }
}
