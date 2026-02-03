/// Feature: Added ItemPickup wrapper for companion pickup commands.
using System.Reflection;
using Inventory;
using UnityEngine;

namespace Inventory.GroundItems
{
    /// <summary>
    /// Lightweight adapter that exposes inventory-friendly information about an <see cref="ItemPickup"/>.
    /// Used to keep companion pickup logic decoupled from the concrete pickup component implementation.
    /// </summary>
    public sealed class WorldDrop
    {
        private static readonly FieldInfo IsBeingCollectedField =
            typeof(ItemPickup).GetField("isBeingCollected", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly ItemPickup pickup;

        private WorldDrop(ItemPickup pickup)
        {
            this.pickup = pickup;
        }

        /// <summary>Creates a wrapper for the supplied pickup when valid.</summary>
        public static WorldDrop FromPickup(ItemPickup pickup)
        {
            return pickup == null ? null : new WorldDrop(pickup);
        }

        /// <summary>Underlying stack represented by the world drop.</summary>
        public ItemStack Stack => pickup != null ? new ItemStack(pickup.Item, pickup.Amount) : default;

        /// <summary>Transform used when navigating towards the drop.</summary>
        public Transform PickupTransform => pickup != null ? pickup.transform : null;

        /// <summary>True while the drop is still present and interactable in the world.</summary>
        public bool IsAvailable => pickup != null && pickup.gameObject != null && pickup.gameObject.activeInHierarchy;

        /// <summary>Exposes the backing pickup for systems that need the concrete component.</summary>
        internal ItemPickup SourcePickup => pickup;

        /// <summary>Removes the drop from the world after a successful collection.</summary>
        public void Despawn()
        {
            if (pickup == null)
                return;

            if (pickup.IsBeingCollected)
                return;

            if (IsBeingCollectedField != null)
                IsBeingCollectedField.SetValue(pickup, true);

            if (pickup.gameObject != null)
                Object.Destroy(pickup.gameObject);
        }
    }
}
