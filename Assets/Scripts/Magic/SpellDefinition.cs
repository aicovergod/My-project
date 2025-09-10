using UnityEngine;
using Combat;

namespace Magic
{
    /// <summary>
    /// Defines a magic spell, including range, visuals and damage info.
    /// </summary>
    [CreateAssetMenu(menuName = "Magic/Spell Definition")]
    public class SpellDefinition : ScriptableObject
    {
        [Tooltip("Display name of the spell")]
        public string displayName;

        [Tooltip("Maximum range of the spell")]
        public float range = CombatMath.MELEE_RANGE;

        [Tooltip("Projectile travel speed for this spell")]
        public float speed = 8f;

        [Tooltip("Projectile prefab to spawn when casting")]
        public GameObject projectilePrefab;

        [Tooltip("Prefab to spawn on impact")]
        public GameObject hitEffectPrefab;

        [Tooltip("Time for the hit effect to fade out")]
        public float hitFadeTime = 0.5f;

        [Tooltip("Maximum damage this spell can inflict before bonuses")]
        public int maxHit = 0;

        [Tooltip("Icon used in the spell book")]
        public Sprite icon;
    }
}
