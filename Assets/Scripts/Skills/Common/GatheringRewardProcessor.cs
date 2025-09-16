using System;
using System.Collections;
using Inventory;
using Pets;
using Skills;
using UI;
using UnityEngine;
using Util;

namespace Skills.Common
{
    /// <summary>
    /// Context payload describing how a gathering reward should be processed.
    /// Provides the inventories to receive items, XP information, floating text
    /// anchors, and delegates for skill specific hooks.
    /// </summary>
    public struct GatheringRewardContext
    {
        public MonoBehaviour runner;
        public SkillManager skills;
        public SkillType skillType;
        public Inventory.Inventory inventory;
        public PetStorage petStorage;
        public ItemData item;
        public string rewardDisplayName;
        public int quantity;
        public float xpPerItem;
        public int petAssistExtraQuantity;
        public Transform floatingTextAnchor;
        public Transform fallbackAnchor;
        public Equipment equipment;
        public Func<ItemData, float> equipmentXpBonusEvaluator;
        public Func<float> additionalXpBonusCalculator;
        public Func<int, bool> customAddItemHandler;
        public Func<int, string> rewardMessageFormatter;
        public string inventoryFullMessage;
        public bool showItemFloatingText;
        public bool showXpPopup;
        public float xpPopupDelayTicks;
        public Action<GatheringRewardResult> onItemsGranted;
        public Action<GatheringRewardResult> onXpApplied;
        public Action<GatheringRewardResult> onSuccess;
        public Action<GatheringRewardResult> onFailure;
    }

    /// <summary>
    /// Result data produced after processing a gathering reward.
    /// </summary>
    public struct GatheringRewardResult
    {
        public bool Success;
        public bool InventoryFull;
        public int RequestedQuantity;
        public int QuantityAwarded;
        public int XpGained;
        public int PreviousLevel;
        public int NewLevel;
        public bool LeveledUp;
        public float AppliedXpMultiplier;
        public float XpPerItem;
        public Transform Anchor;
        public ItemData Item;
        public string DisplayName;
    }

    /// <summary>
    /// Shared helper that executes the add item + XP workflow used by gathering
    /// skills such as woodcutting, fishing, mining, and cooking.
    /// </summary>
    public static class GatheringRewardProcessor
    {
        private const float DefaultXpPopupDelayTicks = 5f;

        /// <summary>
        /// Consistent floating text scale applied to all gathering rewards so different skills
        /// present feedback using the same visual size as prospecting popups.
        /// </summary>
        internal const float DefaultFloatingTextSize = 0.65f;

        public static GatheringRewardResult Process(in GatheringRewardContext context)
        {
            var anchor = context.floatingTextAnchor != null ? context.floatingTextAnchor : context.fallbackAnchor;
            string displayName = !string.IsNullOrEmpty(context.rewardDisplayName)
                ? context.rewardDisplayName
                : context.item != null ? context.item.itemName : string.Empty;

            var result = new GatheringRewardResult
            {
                Success = false,
                InventoryFull = false,
                RequestedQuantity = Mathf.Max(0, context.quantity),
                QuantityAwarded = 0,
                XpGained = 0,
                PreviousLevel = context.skills != null ? context.skills.GetLevel(context.skillType) : 0,
                NewLevel = 0,
                LeveledUp = false,
                AppliedXpMultiplier = 1f,
                XpPerItem = context.xpPerItem,
                Anchor = anchor,
                Item = context.item,
                DisplayName = displayName
            };

            if (result.RequestedQuantity <= 0)
            {
                result.Success = true;
                result.NewLevel = result.PreviousLevel;
                return result;
            }

            if (!TryAddItems(context, ref result))
            {
                string fullMessage = string.IsNullOrEmpty(context.inventoryFullMessage)
                    ? "Your inventory is full"
                    : context.inventoryFullMessage;
                if (anchor != null)
                    FloatingText.Show(fullMessage, anchor, null, DefaultFloatingTextSize);
                result.InventoryFull = true;
                result.NewLevel = result.PreviousLevel;
                context.onFailure?.Invoke(result);
                return result;
            }

            result.Success = true;
            result.QuantityAwarded = result.RequestedQuantity;

            if (context.petAssistExtraQuantity > 0 && context.xpPerItem > 0f)
                BeastmasterXp.TryGrantFromPetAssist(context.xpPerItem * context.petAssistExtraQuantity);

            if (context.showItemFloatingText && anchor != null)
            {
                string rewardMessage = context.rewardMessageFormatter != null
                    ? context.rewardMessageFormatter(result.QuantityAwarded)
                    : $"+{result.QuantityAwarded} {displayName}";
                if (!string.IsNullOrEmpty(rewardMessage))
                    FloatingText.Show(rewardMessage, anchor, null, DefaultFloatingTextSize);
            }

            context.onItemsGranted?.Invoke(result);

            float xpMultiplierBonus = CalculateXpBonus(context);
            result.AppliedXpMultiplier = 1f + xpMultiplierBonus;
            int xpGain = Mathf.RoundToInt(context.xpPerItem * result.QuantityAwarded * result.AppliedXpMultiplier);
            result.XpGained = xpGain;

            if (context.skills != null && xpGain > 0)
            {
                int oldLevel = context.skills.GetLevel(context.skillType);
                int newLevel = context.skills.AddXP(context.skillType, xpGain);
                result.PreviousLevel = oldLevel;
                result.NewLevel = newLevel;
                result.LeveledUp = newLevel > oldLevel;

                if (context.showXpPopup && context.runner != null && anchor != null)
                {
                    float delayTicks = context.xpPopupDelayTicks > 0f ? context.xpPopupDelayTicks : DefaultXpPopupDelayTicks;
                    context.runner.StartCoroutine(ShowXpPopupAfterDelay(xpGain, anchor, delayTicks));
                }
            }
            else
            {
                result.NewLevel = result.PreviousLevel;
                result.LeveledUp = false;
            }

            context.onXpApplied?.Invoke(result);
            context.onSuccess?.Invoke(result);

            return result;
        }

        private static bool TryAddItems(in GatheringRewardContext context, ref GatheringRewardResult result)
        {
            for (int i = 0; i < result.RequestedQuantity; i++)
            {
                bool stepAdded = false;
                if (context.customAddItemHandler != null)
                {
                    stepAdded = context.customAddItemHandler.Invoke(1);
                }
                else if (context.item != null && context.inventory != null && context.inventory.AddItem(context.item, 1))
                {
                    stepAdded = true;
                }
                else if (context.item != null && context.petStorage != null && context.petStorage.StoreItem(context.item, 1))
                {
                    stepAdded = true;
                }

                if (!stepAdded)
                    return false;
            }

            return true;
        }

        private static float CalculateXpBonus(in GatheringRewardContext context)
        {
            float totalBonus = 0f;
            if (context.equipment != null && context.equipmentXpBonusEvaluator != null)
            {
                foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                {
                    if (slot == EquipmentSlot.None)
                        continue;
                    var entry = context.equipment.GetEquipped(slot);
                    if (entry.item != null)
                        totalBonus += context.equipmentXpBonusEvaluator(entry.item);
                }
            }

            if (context.additionalXpBonusCalculator != null)
                totalBonus += context.additionalXpBonusCalculator.Invoke();

            return totalBonus;
        }

        private static IEnumerator ShowXpPopupAfterDelay(int xp, Transform anchor, float delayTicks)
        {
            float delaySeconds = Mathf.Max(0f, delayTicks) * Ticker.TickDuration;
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);
            if (anchor != null)
                FloatingText.Show($"+{xp} XP", anchor, null, DefaultFloatingTextSize);
        }
    }
}
