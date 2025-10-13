using System;
using System.Collections.Generic;
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
        /// Default Unity layers that should always be considered solid for combat line-of-sight
        /// tests. We expose the names so other systems (NPC combat, editor tooling) can reliably
        /// merge the same defaults without duplicating string literals.
        /// </summary>
        public static readonly string[] DefaultObstructionLayerNames =
        {
            "Obstacles",
            "Obstacle",
            "Physical Objects"
        };

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
        /// Ensures the supplied mask always contains the default LOS blocking layers. When the
        /// incoming <paramref name="configuredMask"/> is empty we replace it with the defaults;
        /// otherwise we merge the defaults in so designer overrides cannot accidentally remove
        /// essential obstacle layers.
        /// </summary>
        /// <param name="configuredMask">Mask provided by an inspector or runtime configuration.</param>
        /// <returns>A mask guaranteed to contain the default obstruction layers.</returns>
        public static LayerMask EnsureDefaultObstructionMask(LayerMask configuredMask)
        {
            int maskValue = configuredMask.value;
            int defaultMask = LayerMask.GetMask(DefaultObstructionLayerNames);

            if (maskValue == 0)
                maskValue = defaultMask;
            else
                maskValue |= defaultMask;

            return maskValue;
        }

        /// <summary>
        /// Builds a runtime obstruction mask from the supplied configuration. Callers can optionally
        /// provide a function that enriches the mask at runtime (for example by adding pathfinding
        /// blockers) and specify layers that should be stripped from the final result (friendly NPCs,
        /// interactables, etc.).
        /// </summary>
        /// <param name="configuredMask">Mask sourced from serialized data.</param>
        /// <param name="runtimeMaskEnricher">
        /// Optional callback invoked after defaults are merged. It receives the current mask value
        /// and must return the enriched mask.
        /// </param>
        /// <param name="layersToIgnore">
        /// Bitmask describing layers that should be removed from the final runtime mask. Supply 0 to
        /// keep all layers.
        /// </param>
        /// <returns>The runtime-ready obstruction mask.</returns>
        public static LayerMask BuildRuntimeObstructionMask(
            LayerMask configuredMask,
            Func<int, int> runtimeMaskEnricher = null,
            int layersToIgnore = 0)
        {
            int maskValue = EnsureDefaultObstructionMask(configuredMask).value;

            if (runtimeMaskEnricher != null)
                maskValue = runtimeMaskEnricher(maskValue);

            if (layersToIgnore != 0)
                maskValue &= ~layersToIgnore;

            return maskValue;
        }

        /// <summary>
        /// Builds a layer mask from the supplied collection of layer names. Invalid or unset layer
        /// names are ignored so the method remains safe when optional layers are absent in the
        /// project.
        /// </summary>
        /// <param name="layerNames">Layer names that should be combined into a mask.</param>
        /// <returns>Bitmask representing the supplied layers.</returns>
        public static int BuildLayerMask(IEnumerable<string> layerNames)
        {
            if (layerNames == null)
                return 0;

            int mask = 0;
            foreach (string layerName in layerNames)
            {
                if (string.IsNullOrEmpty(layerName))
                    continue;

                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0)
                    mask |= 1 << layer;
            }

            return mask;
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
