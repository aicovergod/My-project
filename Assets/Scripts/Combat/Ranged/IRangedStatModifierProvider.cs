namespace Combat.Ranged
{
    /// <summary>
    /// Implemented by components that supply ranged combat modifiers (prayers, potions, equipment sets).
    /// The interface keeps the ranged combat controller decoupled from specific buff systems.
    /// </summary>
    public interface IRangedStatModifierProvider
    {
        /// <summary>
        /// Multiplier applied to the accuracy roll. 1 = no change.
        /// </summary>
        float GetAccuracyMultiplier();

        /// <summary>
        /// Multiplier applied to the damage roll. 1 = no change.
        /// </summary>
        float GetDamageMultiplier();

        /// <summary>
        /// Additional tiles added to the effective range.
        /// </summary>
        float GetAdditionalRangeTiles();

        /// <summary>
        /// Multiplier applied to ammo consumption. 1 = normal, 0 = infinite ammo.
        /// </summary>
        float GetAmmoConsumptionMultiplier();
    }
}
