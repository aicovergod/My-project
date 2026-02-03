namespace Combat.Ranged
{
    /// <summary>
    /// Abstraction consumed by <see cref="RangedProjectile"/> so ownership can be shared between
    /// the player ranged controller and companion-specific adapters.
    /// </summary>
    public interface IRangedProjectileOwner
    {
        /// <summary>
        /// Invoked when a projectile completes its travel so the owning controller can resolve
        /// damage application and cleanup.
        /// </summary>
        /// <param name="context">Context captured when the projectile was fired.</param>
        /// <param name="projectile">Projectile instance that reached its destination.</param>
        void HandleProjectileImpact(RangedAttackContext context, RangedProjectile projectile);
    }
}
