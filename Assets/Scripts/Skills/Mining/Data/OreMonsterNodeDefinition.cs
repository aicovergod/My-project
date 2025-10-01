using Combat;
using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    /// ScriptableObject definition for an ore monster encounter that protects a mining node.
    /// Provides designers with combat, prefab, and timing references so personal ore nodes can be
    /// spawned with consistent behaviour.
    /// </summary>
    [CreateAssetMenu(menuName = "Skills/Mining/Ore Monster Node Definition")]
    public sealed class OreMonsterNodeDefinition : ScriptableObject
    {
        [Header("Monster Setup")]
        [Tooltip("Combat profile used when instantiating the monster guarding this ore node.")]
        [SerializeField] private NpcCombatProfile monsterCombatProfile;

        [Tooltip("Prefab for the personal ore node that becomes available after defeating the monster.")]
        [SerializeField] private PersonalOreNode personalOreNodePrefab;

        [Header("Lifetime (Seconds)")]
        [Tooltip("Default lifetime for the personal ore node before it despawns.")]
        [Min(0f)]
        [SerializeField] private float baseLifetimeSeconds = 60f;

        [Tooltip("Extended lifetime applied when the matching charm is equipped.")]
        [Min(0f)]
        [SerializeField] private float charmLifetimeSeconds = 120f;

        [Header("Charm Requirements")]
        [Tooltip("Inventory item ID for the charm that extends this node's lifetime.")]
        [SerializeField] private string charmItemId = string.Empty;

        [Tooltip("If enabled, players must meet a combat level requirement before the monster spawns.")]
        [SerializeField] private bool requiresCombatLevel;

        [Tooltip("Minimum combat level required to challenge this ore monster.")]
        [Min(1)]
        [SerializeField] private int requiredCombatLevel = 1;

        [Header("Presentation")]
        [Tooltip("Solo-lock sprite that communicates exclusive access to the spawned node.")]
        [SerializeField] private Sprite soloLockSprite;

        [Tooltip("Positional offset for spawning the monster relative to the node anchor.")]
        [SerializeField] private Vector3 spawnOffset = Vector3.zero;

        /// <summary>
        /// Combat profile used for spawning the monster guardian.
        /// </summary>
        public NpcCombatProfile MonsterCombatProfile => monsterCombatProfile;

        /// <summary>
        /// Prefab instance spawned for the personal ore node reward.
        /// </summary>
        public PersonalOreNode PersonalOreNodePrefab => personalOreNodePrefab;

        /// <summary>
        /// Lifetime applied when no charm bonus is active.
        /// </summary>
        public float BaseLifetimeSeconds => baseLifetimeSeconds;

        /// <summary>
        /// Lifetime applied when the matching charm bonus is active.
        /// </summary>
        public float CharmLifetimeSeconds => charmLifetimeSeconds;

        /// <summary>
        /// Inventory item ID that grants the lifetime bonus when equipped.
        /// </summary>
        public string CharmItemId => charmItemId;

        /// <summary>
        /// Indicates whether the encounter enforces a combat level prerequisite.
        /// </summary>
        public bool RequiresCombatLevel => requiresCombatLevel;

        /// <summary>
        /// Minimum combat level required when <see cref="RequiresCombatLevel"/> is true.
        /// </summary>
        public int RequiredCombatLevel => requiredCombatLevel;

        /// <summary>
        /// Solo-lock indicator sprite shown when the node is reserved for the defeating player.
        /// </summary>
        public Sprite SoloLockSprite => soloLockSprite;

        /// <summary>
        /// World-space offset used when spawning the monster relative to its target rock.
        /// </summary>
        public Vector3 SpawnOffset => spawnOffset;

        /// <summary>
        /// Resolves the active lifetime for the personal node while ensuring a minimum duration of one second.
        /// </summary>
        /// <param name="hasCharmEquipped">Indicates whether the player currently meets the charm requirement.</param>
        /// <returns>The clamped lifetime in seconds, adjusted if the charm bonus is active.</returns>
        public float ResolveLifetimeSeconds(bool hasCharmEquipped)
        {
            var lifetime = hasCharmEquipped ? charmLifetimeSeconds : baseLifetimeSeconds;
            return Mathf.Max(1f, lifetime);
        }
    }
}
