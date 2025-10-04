using System.Collections.Generic;
using Player;
using Pets;
using UnityEngine;

namespace Combat.Ranged
{
    /// <summary>
    /// Splash damage effect for thrown chinchompas. When assigned to a <see cref="RangedWeaponData"/>
    /// that consumes its own weapon stack, the explosion mimics Old School RuneScape's multi-target
    /// behaviour by rolling secondary ranged hits around the impact point. Designers should place the
    /// configured weapon assets under <c>Assets/Resources/Combat/Ranged/Weapons</c>, set
    /// <see cref="RangedWeaponData.consumesWeaponStack"/> to true so ammo is consumed per throw, and
    /// assign this special effect asset to ensure the detonation logic fires automatically.
    /// </summary>
    [CreateAssetMenu(menuName = "Combat/Ranged/Special Effects/Chinchompa Explosion")]
    public class ChinchompaExplosionEffect : RangedSpecialEffect
    {
        /// <summary>
        /// Tile size in world units. Ranged systems treat one Unity unit as a single tile (64x64 pixels)
        /// so explosion radii can be authored in familiar OSRS tile measurements.
        /// </summary>
        private const float TileSize = 1f;

        [SerializeField]
        [Tooltip("Radius in tiles searched when resolving the chinchompa explosion. 1 tile = 64x64 pixels.")]
        private float explosionRadiusTiles = 1f;

        [SerializeField]
        [Tooltip("Falloff curve mapping normalised distance (0-1) to a damage multiplier. Defaults to a 1 -> 0.2 curve.")]
        private AnimationCurve damageFalloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));

        [SerializeField]
        [Tooltip("Maximum number of additional targets affected by the splash (excluding the primary target).")]
        private int maxSplashTargets = 8;

        [SerializeField]
        [Tooltip("Physics layers probed when gathering potential splash targets.")]
        private LayerMask targetMask = LayerMask.GetMask("NPC", "Enemy", "Hostile", "Player", "Pets");

        [SerializeField]
        [Tooltip("Allow the explosion to damage allied player combat targets.")]
        private bool damagePlayer;

        [SerializeField]
        [Tooltip("Allow the explosion to damage friendly pets.")]
        private bool damagePets;

        [SerializeField]
        [Tooltip("Allow the thrower to be hit by their own explosion.")]
        private bool damageSelf;

        [SerializeField]
        [Tooltip("Additional multiplier applied when the thrower is damaged by their own explosion.")]
        private float selfDamageMultiplier = 0.4f;

        /// <summary>
        /// Called by the ranged pipeline once the primary projectile finishes resolving. This method
        /// gathers nearby combat targets, scales damage by distance using the configured falloff curve,
        /// and applies splash hits via the shared combat controller so XP, poison procs, and hitsplats
        /// behave exactly like standard ranged attacks.
        /// </summary>
        public override void OnImpactResolved(RangedAttackContext context)
        {
            CombatController combatController = ResolveCombatController(context);
            RangedCombatController rangedController = context.rangedController != null
                ? context.rangedController
                : combatController != null ? combatController.GetComponent<RangedCombatController>() : null;

            if (combatController == null || rangedController == null)
                return;

            float radiusTiles = Mathf.Max(0f, explosionRadiusTiles);
            if (radiusTiles <= 0f)
                return;

            Vector3 center = ResolveDetonationPoint(context);
            float radiusWorld = radiusTiles * TileSize;

            var processedTargets = new HashSet<CombatTarget>();
            if (context.target != null)
                processedTargets.Add(context.target);

            CombatTarget selfTarget = ResolveSelfTarget(context);

            int mask = targetMask.value;
            if (mask == 0)
                mask = Physics2D.DefaultRaycastLayers;

            Collider2D[] hits = Physics2D.OverlapCircleAll(center, radiusWorld, mask);
            if (hits == null || hits.Length == 0)
                return;

            int applied = 0;
            foreach (var hit in hits)
            {
                if (applied >= maxSplashTargets)
                    break;
                if (hit == null)
                    continue;

                CombatTarget candidate = hit.GetComponent<CombatTarget>() ?? hit.GetComponentInParent<CombatTarget>();
                if (candidate == null || processedTargets.Contains(candidate) || !candidate.IsAlive)
                    continue;

                bool isPlayerTarget = candidate is PlayerCombatTarget;
                if (isPlayerTarget && !damagePlayer)
                    continue;

                bool isPetTarget = candidate is PetCombatController;
                if (isPetTarget && !damagePets)
                    continue;

                bool isSelf = selfTarget != null && candidate == selfTarget;
                if (isSelf && !damageSelf)
                    continue;

                float distance = Vector2.Distance(center, candidate.transform.position);
                float normalisedDistance = radiusWorld > Mathf.Epsilon ? Mathf.Clamp01(distance / radiusWorld) : 0f;
                float falloff = damageFalloff != null && damageFalloff.length > 0
                    ? Mathf.Max(0f, damageFalloff.Evaluate(normalisedDistance))
                    : Mathf.Clamp01(1f - normalisedDistance);

                float scale = isSelf ? falloff * Mathf.Max(0f, selfDamageMultiplier) : falloff;
                if (scale <= 0f)
                    continue;

                var splashResult = rangedController.RollSecondaryRangedDamage(in context, candidate, scale);
                CombatantStats attackerStats = context.attacker;
                CombatStyle style = attackerStats != null ? attackerStats.Style : CombatStyle.Accurate;

                combatController.ApplyDamageResult(
                    candidate,
                    splashResult.damage,
                    splashResult.hit,
                    splashResult.maxHit,
                    style,
                    DamageType.Ranged,
                    SpellElement.None);

                processedTargets.Add(candidate);
                applied++;
            }
        }

        /// <summary>
        /// Determines the world-space centre used for the explosion query.
        /// </summary>
        private static Vector3 ResolveDetonationPoint(RangedAttackContext context)
        {
            if (context.target != null)
                return context.target.transform.position;
            Vector3 position = context.targetPosition;
            if (position == Vector3.zero)
                position = context.origin;
            return position;
        }

        /// <summary>
        /// Resolves the combat controller that should receive splash application calls.
        /// </summary>
        private static CombatController ResolveCombatController(RangedAttackContext context)
        {
            if (context.combatController != null)
                return context.combatController;
            if (context.rangedController != null)
                return context.rangedController.GetComponent<CombatController>();
            return null;
        }

        /// <summary>
        /// Attempts to find the thrower's <see cref="CombatTarget"/> so self-damage can be filtered.
        /// </summary>
        private static CombatTarget ResolveSelfTarget(RangedAttackContext context)
        {
            if (context.rangedController == null)
                return null;

            var owner = context.rangedController.GetComponent<CombatTarget>();
            if (owner != null)
                return owner;

            owner = context.rangedController.GetComponentInParent<CombatTarget>();
            if (owner != null)
                return owner;

            return context.rangedController.GetComponentInChildren<CombatTarget>();
        }
    }
}
