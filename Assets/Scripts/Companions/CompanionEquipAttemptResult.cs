using System;

namespace Companions
{
    /// <summary>
    ///     Describes the outcome of a companion equipment attempt initiated from the player inventory.
    ///     The values differentiate between successful equips, handled failure states, and scenarios where
    ///     the companion window declined to process the request so the caller can fall back to the default
    ///     player equipment flow.
    /// </summary>
    [Serializable]
    public enum CompanionEquipAttemptResult
    {
        /// <summary>
        ///     The equipment window was unable to process the request (e.g. closed or not initialised) so the caller
        ///     should fall back to normal player equipment behaviour.
        /// </summary>
        NotHandled = 0,

        /// <summary>
        ///     The companion successfully equipped the requested item.
        /// </summary>
        Equipped = 1,

        /// <summary>
        ///     The companion lacks the required skill levels to equip the item. The item has already been restored
        ///     to the originating inventory.
        /// </summary>
        RequirementsNotMet = 2,

        /// <summary>
        ///     The operation failed because there was no free space available to stash conflicting equipment.
        /// </summary>
        InventoryFull = 3,

        /// <summary>
        ///     The stack limit for the requested equipment slot has been reached, preventing additional items from being equipped.
        /// </summary>
        StackLimitReached = 4,

        /// <summary>
        ///     The requested equipment slot could not be resolved.
        /// </summary>
        InvalidSlot = 5
    }
}
