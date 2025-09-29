using System;
using UnityEngine;
using Pets;
using Player;

namespace Combat
{
    /// <summary>
    /// Helper methods for resolving line-of-sight checks between combatants while
    /// respecting friendly units and designer-configured obstruction masks.
    /// </summary>
    public static class LineOfSightUtility
    {
        /// <summary>
        /// Name of the physics layer used to flag colliders that should only block
        /// melee attacks. Magic and ranged projectiles can pass through them.
        /// </summary>
        public const string AntiMeleeObstacleLayerName = "AntiMeleeObstacle";

        /// <summary>
        /// Cached layer index for <see cref="AntiMeleeObstacleLayerName"/>. Unity
        /// returns -1 when the layer has not been defined in the project.
        /// </summary>
        private static int antiMeleeObstacleLayer = -2;

        /// <summary>
        /// Provides the resolved layer index for
        /// <see cref="AntiMeleeObstacleLayerName"/> while caching the lookup.
        /// </summary>
        private static int AntiMeleeObstacleLayer
        {
            get
            {
                if (antiMeleeObstacleLayer == -2)
                    antiMeleeObstacleLayer = LayerMask.NameToLayer(AntiMeleeObstacleLayerName);
                return antiMeleeObstacleLayer;
            }
        }

        /// <summary>
        /// Determines whether any collider on the supplied <paramref name="mask"/>
        /// blocks the sightline between <paramref name="origin"/> and
        /// <paramref name="destination"/>.
        /// </summary>
        /// <param name="origin">World-space starting position for the trace.</param>
        /// <param name="destination">World-space point the trace should reach.</param>
        /// <param name="mask">Layer mask describing which colliders can obstruct vision.</param>
        /// <param name="source">Transform representing the attacker. Used to
        /// ignore self-collisions.</param>
        /// <param name="target">Transform representing the intended victim. Used to
        /// ignore the target's colliders.</param>
        /// <param name="additionalIgnore">Optional predicate that can skip specific
        /// colliders (pets, friendlies, etc.). When supplied, colliders returning true
        /// will not be treated as blockers.</param>
        /// <returns>True when no blocking collider lies between the two points.</returns>
        public static bool HasLineOfSight(
            Vector2 origin,
            Vector2 destination,
            LayerMask mask,
            Transform source,
            Transform target,
            Func<Collider2D, bool> additionalIgnore = null)
        {
            if (mask == 0)
                return true;

            Vector2 toTarget = destination - origin;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon)
                return true;

            Vector2 direction = toTarget / distance;
            // Nudge the origin forward slightly so colliders on the attacker do not
            // immediately trigger the ray.
            origin += direction * 0.05f;

            var hits = Physics2D.RaycastAll(origin, direction, distance, mask);
            if (hits == null || hits.Length == 0)
                return true;

            Array.Sort(hits, static (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                var collider = hit.collider;
                if (collider == null)
                    continue;

                if (collider.isTrigger)
                    continue;

                var hitTransform = collider.transform;
                if (source != null && (hitTransform == source || hitTransform.IsChildOf(source)))
                    continue;
                if (target != null && (hitTransform == target || hitTransform.IsChildOf(target)))
                    continue;

                if (additionalIgnore != null && additionalIgnore(collider))
                    continue;

                // Ignore colliders belonging to combatants so follower pets or party
                // members do not block melee swings or projectiles.
                if (IsFriendlyCollider(collider))
                    continue;

                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true when the supplied collider is configured as an
        /// anti-melee obstacle layer. Magic and ranged attacks can optionally
        /// ignore these colliders when computing line-of-sight checks.
        /// </summary>
        /// <param name="collider">Collider to inspect.</param>
        public static bool IsAntiMeleeObstacle(Collider2D collider)
        {
            if (collider == null)
                return false;

            int layerIndex = AntiMeleeObstacleLayer;
            if (layerIndex < 0)
                return false;

            return collider.gameObject.layer == layerIndex;
        }

        /// <summary>
        /// Determines whether the supplied collider belongs to a combatant that should
        /// not obstruct the line-of-sight tests (players, pets, allies).
        /// </summary>
        private static bool IsFriendlyCollider(Collider2D collider)
        {
            if (collider == null)
                return false;

            var transform = collider.transform;
            if (transform == null)
                return false;

            // Pets and player combat targets are treated as friendlies so they do not
            // block their owner's attacks.
            if (transform.GetComponentInParent<PetCombatController>() != null)
                return true;
            if (transform.GetComponentInParent<PlayerCombatTarget>() != null)
                return true;

            return false;
        }
    }
}
