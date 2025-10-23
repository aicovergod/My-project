using System;
using Companions;
using Companions.Conversation;
using Inventory;
using Player;
using Pets;
using Skills;
using UnityEngine;
using UI.Chat;

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
        public Vector3? resourcePosition;
        public Equipment equipment;
        public Func<ItemData, float> equipmentXpBonusEvaluator;
        public Func<float> additionalXpBonusCalculator;
        public Func<int, bool> customAddItemHandler;
        public Func<int, string> rewardMessageFormatter;
        public string inventoryFullMessage;
        public bool showItemFloatingText;
        public bool showXpPopup;
        public float xpPopupDelayTicks;
        public bool useCompanionChatFormatting;
        public Func<string> companionChatSenderResolver;
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
        public bool HasResourcePosition;
        public Vector3 ResourcePosition;
    }

    /// <summary>
    /// Shared helper that executes the add item + XP workflow used by gathering
    /// skills such as woodcutting, fishing, mining, and cooking.
    /// </summary>
    public static class GatheringRewardProcessor
    {
        public static GatheringRewardResult Process(in GatheringRewardContext context)
        {
            var anchor = context.floatingTextAnchor != null ? context.floatingTextAnchor : context.fallbackAnchor;
            Vector3? resourcePosition = context.resourcePosition;
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
                DisplayName = displayName,
                HasResourcePosition = resourcePosition.HasValue,
                ResourcePosition = resourcePosition ?? (anchor != null ? anchor.position : Vector3.zero)
            };

            if (result.RequestedQuantity <= 0)
            {
                result.Success = true;
                result.NewLevel = result.PreviousLevel;
                return result;
            }

            if (!TryAddItems(context, ref result))
            {
                string floatingTextMessage = string.IsNullOrEmpty(context.inventoryFullMessage)
                    ? "Your inventory is full"
                    : context.inventoryFullMessage;
                string chatMessage = ResolveInventoryFullChatMessage(context, floatingTextMessage);
                if (anchor != null)
                {
                    bool displayed = false;
                    if (resourcePosition.HasValue)
                        displayed = GatheringFloatingTextService.TryShowNow(floatingTextMessage, anchor, resourcePosition.Value);

                    if (!displayed && !resourcePosition.HasValue)
                        GatheringFloatingTextService.TryShowAtAnchor(floatingTextMessage, anchor);
                }

                PublishChatMessage(chatMessage, context);
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
                {
                    bool displayed = false;
                    if (resourcePosition.HasValue)
                        displayed = GatheringFloatingTextService.TryShowNow(rewardMessage, anchor, resourcePosition.Value);

                    if (!displayed && !resourcePosition.HasValue)
                        GatheringFloatingTextService.TryShowAtAnchor(rewardMessage, anchor);

                    PublishChatMessage(rewardMessage, context);
                }
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

                if (context.showXpPopup && anchor != null)
                {
                    Vector3 xpSource = resourcePosition ?? anchor.position;
                    GatheringFloatingTextService.QueueDelayedXpPopup(xpGain, anchor, xpSource, context.xpPopupDelayTicks);
                }
            }
            else
            {
                result.NewLevel = result.PreviousLevel;
                result.LeveledUp = false;
            }

            context.onXpApplied?.Invoke(result);
            context.onSuccess?.Invoke(result);

            RegisterGatheringEvent(context, result);

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

        /// <summary>
        /// Determines the chat line that should be emitted when the player's inventory cannot accept more items.
        /// Falls back to the supplied message for floating text when no special companion message is required.
        /// </summary>
        /// <param name="context">Reward context describing the failed gather attempt.</param>
        /// <param name="fallbackMessage">Default message to use when no specialised companion line applies.</param>
        /// <returns>Chat message that should be published to the appropriate channel.</returns>
        private static string ResolveInventoryFullChatMessage(in GatheringRewardContext context, string fallbackMessage)
        {
            if (!string.IsNullOrWhiteSpace(context.inventoryFullMessage))
                return context.inventoryFullMessage;

            if (context.useCompanionChatFormatting && CompanionManager.HasActiveCompanion)
            {
                bool companionInventoryFull = IsCompanionInventoryFullForItem(context);
                return companionInventoryFull
                    ? CompanionChatLibrary.GetRandomPlayerAndCompanionInventoryFullLine()
                    : CompanionChatLibrary.GetRandomPlayerInventoryFullLine();
            }

            return fallbackMessage;
        }

        /// <summary>
        /// Checks whether the active companion's inventory can accept the gathered item.
        /// </summary>
        /// <param name="context">Reward context providing the item data to evaluate.</param>
        /// <returns><c>true</c> when the companion inventory cannot store the item, otherwise <c>false</c>.</returns>
        private static bool IsCompanionInventoryFullForItem(in GatheringRewardContext context)
        {
            if (context.item == null)
                return false;

            var companionInventoryWrapper = CompanionManager.CompanionInventory;
            if (companionInventoryWrapper == null)
                return false;

            var companionInventory = companionInventoryWrapper.InventoryComponent;
            if (companionInventory == null)
                return false;

            return !companionInventory.CanAddItem(context.item, 1);
        }

        private static void PublishChatMessage(string message, in GatheringRewardContext context)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var chatService = ChatService.Instance;
            if (chatService == null)
                return;

            if (context.useCompanionChatFormatting)
            {
                string sender = context.companionChatSenderResolver != null
                    ? context.companionChatSenderResolver.Invoke()
                    : null;
                if (string.IsNullOrWhiteSpace(sender))
                    sender = "Companion";

                chatService.PublishCompanionMessage(sender, message);
            }
            else
            {
                chatService.PublishGameMessage(message);
            }
        }

        /// <summary>
        /// Registers a structured event describing the successful gathering action so companions can reference it.
        /// </summary>
        private static void RegisterGatheringEvent(in GatheringRewardContext context, in GatheringRewardResult result)
        {
            if (!result.Success || result.QuantityAwarded <= 0)
                return;

            string summary = BuildGatheringSummary(result);
            if (string.IsNullOrEmpty(summary))
                return;

            string actor = ResolveGatheringActor(context.runner);
            Vector3? location = result.HasResourcePosition ? result.ResourcePosition : (Vector3?)null;
            string additional = result.XpGained > 0 ? $"Earned {result.XpGained} XP." : null;

            var metadata = CompanionEventMetadata.Create(
                primaryActor: actor,
                worldPosition: location,
                skill: context.skillType,
                additionalContext: additional);

            CompanionConversationService.RegisterEvent(summary, CompanionEventType.Gathering, metadata);
        }

        private static string BuildGatheringSummary(in GatheringRewardResult result)
        {
            string name = !string.IsNullOrWhiteSpace(result.DisplayName)
                ? result.DisplayName.Trim()
                : result.Item != null ? result.Item.itemName : string.Empty;

            if (string.IsNullOrWhiteSpace(name))
                name = "resources";

            if (result.QuantityAwarded <= 0)
                return string.Empty;

            if (result.QuantityAwarded == 1)
                return $"gathered {name}";

            return $"gathered {result.QuantityAwarded} {name}";
        }

        private static string ResolveGatheringActor(MonoBehaviour runner)
        {
            if (runner == null)
                return "You";

            var companionController = runner.GetComponentInParent<CompanionController>();
            if (companionController != null)
                return CompanionManager.GetCompanionDisplayName();

            var companionMining = runner.GetComponentInParent<CompanionMiningController>();
            if (companionMining != null)
                return CompanionManager.GetCompanionDisplayName();

            var playerMover = runner.GetComponentInParent<PlayerMover>();
            if (playerMover != null)
                return "You";

            if (runner.CompareTag("Player"))
                return "You";

            return "You";
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
    }
}
