using UnityEngine;
using Status;

namespace Inventory
{
    /// <summary>
    /// Identifies how an item was used so downstream listeners can react appropriately.
    /// </summary>
    public enum ItemUseType
    {
        Consumed,
        Equipped,
        Unequipped
    }

    /// <summary>
    /// Payload describing a specific item usage action.
    /// </summary>
    public struct ItemUseContext
    {
        public GameObject user;
        public ItemData item;
        public ItemUseType useType;
    }

    /// <summary>
    /// Lightweight static helper that translates inventory and equipment events into
    /// <see cref="BuffEvents"/> broadcasts.
    /// </summary>
    public static class ItemUseResolver
    {
        /// <summary>Raised whenever any item is used, equipped or unequipped.</summary>
        public static event System.Action<ItemUseContext> ItemUsed;

        /// <summary>
        /// Notify listeners that an item was used and automatically raise buff events for any
        /// configured <see cref="ItemData.buffEffects"/> entries.
        /// </summary>
        public static void NotifyItemUsed(GameObject user, ItemData item, ItemUseType useType)
        {
            if (user == null || item == null)
                return;

            var context = new ItemUseContext { user = user, item = item, useType = useType };
            ItemUsed?.Invoke(context);

            if (item.buffEffects == null || item.buffEffects.Length == 0)
                return;

            switch (useType)
            {
                case ItemUseType.Consumed:
                    ApplyBuffs(user, item, BuffTrigger.OnConsume, BuffSourceType.Potion);
                    break;
                case ItemUseType.Equipped:
                    ApplyBuffs(user, item, BuffTrigger.OnEquip, BuffSourceType.Equipment);
                    break;
                case ItemUseType.Unequipped:
                    RemoveEquipmentBuffs(user, item);
                    break;
            }
        }

        private static void ApplyBuffs(GameObject user, ItemData item, BuffTrigger trigger, BuffSourceType source)
        {
            for (int i = 0; i < item.buffEffects.Length; i++)
            {
                var effect = item.buffEffects[i];
                if (effect.trigger != trigger)
                    continue;

                var context = new BuffEventContext
                {
                    target = user,
                    definition = effect.timer,
                    sourceType = source,
                    sourceId = string.IsNullOrEmpty(effect.customSourceId) ? item.id : effect.customSourceId
                };

                if (effect.refreshOnReapply)
                    BuffEvents.RaiseBuffApplied(context);
                else
                    BuffEvents.RaiseBuffRefreshed(context);
            }
        }

        private static void RemoveEquipmentBuffs(GameObject user, ItemData item)
        {
            for (int i = 0; i < item.buffEffects.Length; i++)
            {
                var effect = item.buffEffects[i];
                bool shouldRemove = effect.trigger == BuffTrigger.OnUnequip ||
                    (effect.trigger == BuffTrigger.OnEquip && effect.removeOnUnequip);
                if (!shouldRemove)
                    continue;

                var context = new BuffEventContext
                {
                    target = user,
                    definition = effect.timer,
                    sourceType = BuffSourceType.Equipment,
                    sourceId = string.IsNullOrEmpty(effect.customSourceId) ? item.id : effect.customSourceId
                };
                BuffEvents.RaiseBuffRemoved(context);
            }
        }
    }
}
