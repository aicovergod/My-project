using System.Collections.Generic;
using UnityEngine;

namespace Combat
{
    /// <summary>
    /// ScriptableObject that centralises every hitsplat sprite so gameplay systems can
    /// reference them without performing runtime Resources.Load calls.
    /// </summary>
    [CreateAssetMenu(fileName = "HitSplatLibrary", menuName = "Combat/Hit Splat Library")]
    public class HitSplatLibrary : ScriptableObject
    {
        /// <summary>
        /// Simple pairing of an elemental affinity with the sprite that should represent it.
        /// Serialized so designers can configure the mapping directly in the inspector.
        /// </summary>
        [System.Serializable]
        private struct ElementHitSplat
        {
            [Tooltip("The elemental affinity these hitsplat visuals belong to.")]
            public SpellElement element;

            [Tooltip("Sprite displayed when damage is dealt with the configured element.")]
            public Sprite sprite;
        }

        [Header("Core Hitsplat Sprites")]
        [Tooltip("Default sprite displayed for non-elemental successful hits.")]
        [SerializeField] private Sprite damageHitsplat;

        [Tooltip("Sprite displayed when an attack deals zero damage.")]
        [SerializeField] private Sprite zeroDamageHitsplat;

        [Tooltip("Sprite used when the attacker rolls their maximum possible hit.")]
        [SerializeField] private Sprite maxHitHitsplat;

        [Tooltip("Sprite used for damage caused by burning effects.")]
        [SerializeField] private Sprite burnHitsplat;

        [Tooltip("Sprite used for damage caused by poison effects.")]
        [SerializeField] private Sprite poisonHitsplat;

        [Header("Elemental Hitsplat Overrides")]
        [Tooltip("Overrides that map elemental spell damage to themed hitsplat sprites.")]
        [SerializeField] private ElementHitSplat[] elementalHitsplats;

        /// <summary>
        /// Runtime lookup built from the serialized array so systems can grab sprites quickly.
        /// </summary>
        private readonly Dictionary<SpellElement, Sprite> elementLookup = new Dictionary<SpellElement, Sprite>();

        /// <summary>
        /// Tracks whether the lookup must be rebuilt due to inspector edits or asset reloads.
        /// </summary>
        private bool elementLookupDirty = true;

        /// <summary>Sprite displayed for standard damaging hits.</summary>
        public Sprite DamageHitsplat => damageHitsplat;

        /// <summary>Sprite displayed when an attack misses or deals zero damage.</summary>
        public Sprite ZeroDamageHitsplat => zeroDamageHitsplat;

        /// <summary>Sprite displayed when the attacker achieves their maximum hit.</summary>
        public Sprite MaxHitHitsplat => maxHitHitsplat;

        /// <summary>Sprite displayed when damage comes from a burning effect.</summary>
        public Sprite BurnHitsplat => burnHitsplat;

        /// <summary>Sprite displayed when damage comes from a poison effect.</summary>
        public Sprite PoisonHitsplat => poisonHitsplat;

        /// <summary>
        /// Provides a cached, read-only dictionary containing every configured elemental sprite.
        /// Consumers can cache the result or query it directly whenever they need an elemental hitsplat.
        /// </summary>
        public IReadOnlyDictionary<SpellElement, Sprite> ElementHitsplats
        {
            get
            {
                EnsureLookup();
                return elementLookup;
            }
        }

        /// <summary>
        /// Fetch the sprite assigned to a specific elemental affinity.
        /// Returns null if the element is unconfigured or represents the "None" type.
        /// </summary>
        public Sprite GetElementHitsplat(SpellElement element)
        {
            if (element == SpellElement.None)
                return null;

            EnsureLookup();
            elementLookup.TryGetValue(element, out var sprite);
            return sprite;
        }

        /// <summary>
        /// Ensures the runtime dictionary reflects the serialized inspector data.
        /// Only rebuilds when flagged dirty to avoid unnecessary allocations at runtime.
        /// </summary>
        private void EnsureLookup()
        {
            if (!elementLookupDirty)
                return;

            elementLookup.Clear();
            if (elementalHitsplats != null)
            {
                for (int i = 0; i < elementalHitsplats.Length; i++)
                {
                    var entry = elementalHitsplats[i];
                    if (entry.sprite == null || entry.element == SpellElement.None)
                        continue;

                    elementLookup[entry.element] = entry.sprite;
                }
            }

            elementLookupDirty = false;
        }

        /// <summary>
        /// Unity callback invoked when the asset becomes active. Marks the lookup as dirty so it
        /// will rebuild using the most up-to-date inspector data the next time it is queried.
        /// </summary>
        private void OnEnable()
        {
            elementLookupDirty = true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Ensures the lookup refreshes immediately when values change inside the editor.
        /// </summary>
        private void OnValidate()
        {
            elementLookupDirty = true;
        }
#endif
    }
}
