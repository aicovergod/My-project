/// Feature: Added centralised routing for companion ground drop pickups.
using Inventory.GroundItems;

namespace Companions
{
    /// <summary>
    /// Static helper that locates the active companion and forwards ground drop pickup requests.
    /// </summary>
    public static class CompanionPickupService
    {
        /// <summary>
        /// Requests that the active companion collect the supplied drop when valid.
        /// </summary>
        /// <param name="drop">World drop the companion should attempt to pick up.</param>
        /// <returns>True when a pickup command was issued.</returns>
        public static bool RequestPickup(WorldDrop drop)
        {
            if (drop == null || !drop.IsAvailable)
                return false;

            if (drop.PickupTransform == null)
                return false;

            if (!CompanionManager.HasActiveCompanion)
                return false;

            var pickupController = CompanionManager.CompanionPickupController;
            if (pickupController == null)
                return false;

            pickupController.CancelActivePickup();
            pickupController.CommandPickup(drop);
            return true;
        }
    }
}
