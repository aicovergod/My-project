using Inventory;

namespace MyGame.Drops
{
    /// <summary>
    /// Result of a resolved drop.
    /// </summary>
    public readonly struct ResolvedDrop
    {
        /// <summary>The item definition.</summary>
        public readonly ItemData item;

        /// <summary>The quantity.</summary>
        public readonly int quantity;

        /// <summary>
        /// Creates a new resolved drop.
        /// </summary>
        public ResolvedDrop(ItemData item, int quantity)
        {
            this.item = item;
            this.quantity = quantity;
        }
    }
}
