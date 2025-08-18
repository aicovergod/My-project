using Inventory;

namespace MyGame.Drops
{
    /// <summary>
    /// Result of a resolved drop.
    /// </summary>
    public readonly struct ResolvedDrop
    {
        /// <summary>The item definition.</summary>
        public readonly ItemDefinition item;

        /// <summary>The quantity.</summary>
        public readonly int quantity;

        /// <summary>
        /// Creates a new resolved drop.
        /// </summary>
        public ResolvedDrop(ItemDefinition item, int quantity)
        {
            this.item = item;
            this.quantity = quantity;
        }
    }
}
