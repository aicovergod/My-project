namespace Fishing.Bycatch
{
    public readonly struct BycatchResult
    {
        public readonly BycatchItemDefinition item;
        public readonly int quantity;
        public readonly Rarity rarity;

        public BycatchItemDefinition Item => item;
        public int Quantity => quantity;
        public Rarity Rarity => rarity;

        public bool IsNone => item == null || quantity <= 0;

        public BycatchResult(BycatchItemDefinition item, int quantity, Rarity rarity)
        {
            this.item = item;
            this.quantity = quantity;
            this.rarity = rarity;
        }

        public static BycatchResult None => new BycatchResult(null, 0, Rarity.Common);
    }
}
