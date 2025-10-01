using System;
using Core.Save;
using Inventory;
using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    ///     Coordinates the lifecycle of a player's personal ore node that appears after
    ///     defeating an ore monster encounter. Handles prefab spawning, lifetime tracking,
    ///     and exposes events so the UI or other systems can react to timer changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MiningPersonalNodeController : MonoBehaviour
    {
        [Header("Runtime References")]
        [SerializeField] private MiningSkill miningSkill;
        [SerializeField] private SkillManager skillManager;
        [SerializeField] private Equipment equipment;

        [Header("Debug")]
        [SerializeField, Tooltip("Enables verbose logging for personal ore node lifecycle events.")]
        private bool enableDebugLogging;

        private PersonalOreNode activeNode;
        private float activeNodeLifetimeSeconds;

        /// <summary>
        ///     Currently spawned personal ore node instance, or <c>null</c> if the player
        ///     does not have an active node.
        /// </summary>
        public PersonalOreNode ActiveNode => activeNode;

        /// <summary>
        ///     Raised whenever the active personal node changes. The provided argument is the
        ///     new node instance (or <c>null</c> when the node despawns).
        /// </summary>
        public event Action<PersonalOreNode> ActiveNodeChanged;

        /// <summary>
        ///     Raised whenever the active node reports a lifetime update. The first argument
        ///     is the remaining lifetime in seconds while the second is the normalized value
        ///     (0-1) relative to the original lifetime duration.
        /// </summary>
        public event Action<float, float> ActiveNodeTimerUpdated;

        private void Awake()
        {
            if (miningSkill == null)
                miningSkill = GetComponent<MiningSkill>();
            if (skillManager == null)
                skillManager = GetComponent<SkillManager>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
        }

        private void OnDisable()
        {
            // Ensure the node is forced to expire without any presentation so we do not
            // leave an orphaned instance in the scene when this component is disabled.
            DespawnActiveNode(false);
        }

        /// <summary>
        ///     Spawns a personal ore node prefab and wires up lifetime callbacks so other
        ///     systems can react to exclusive node availability.
        /// </summary>
        /// <param name="definition">Definition that provides the prefab and lifetime data.</param>
        /// <param name="spawnOrigin">World-space origin used to position the node.</param>
        public void SpawnNode(OreMonsterNodeDefinition definition, Vector3 spawnOrigin)
        {
            if (definition == null)
            {
                LogDebug("Cannot spawn personal node because the supplied definition is null.");
                return;
            }

            var prefab = definition.PersonalOreNodePrefab;
            if (prefab == null)
            {
                LogDebug($"Definition '{definition.name}' is missing a personal ore node prefab, aborting spawn.");
                return;
            }

            // Remove any existing node before spawning a new one to ensure exclusivity.
            DespawnActiveNode(true);

            var spawnPosition = spawnOrigin + definition.SpawnOffset;
            var nodeInstance = Instantiate(prefab, spawnPosition, prefab.transform.rotation);

            bool charmEquipped = HasCharmEquipped(definition.CharmItemId);
            float lifetimeSeconds = definition.ResolveLifetimeSeconds(charmEquipped);
            activeNodeLifetimeSeconds = lifetimeSeconds;

            string ownerProfileId = SaveManager.ActiveProfileId;
            if (string.IsNullOrWhiteSpace(ownerProfileId))
                ownerProfileId = string.Empty;

            activeNode = nodeInstance;
            RegisterNodeCallbacks(activeNode);
            ActiveNodeChanged?.Invoke(activeNode);

            activeNode.Initialise(this, definition, ownerProfileId, lifetimeSeconds);

            LogDebug(
                $"Spawned personal ore node '{prefab.name}' at {spawnPosition} with lifetime {lifetimeSeconds:0.00}s (charm equipped: {charmEquipped}).");
        }

        /// <summary>
        ///     Removes the active node instance, optionally allowing the prefab to play its
        ///     despawn visuals.
        /// </summary>
        /// <param name="playVfx">True to allow VFX, false to suppress them.</param>
        private void DespawnActiveNode(bool playVfx)
        {
            if (activeNode == null)
                return;

            UnregisterNodeCallbacks(activeNode);
            try
            {
                activeNode.ForceExpire(playVfx);
            }
            catch (MissingReferenceException)
            {
                // Ignore missing reference exceptions if the instance was destroyed elsewhere.
            }

            LogDebug($"Personal ore node despawned (playVfx={playVfx}).");

            activeNode = null;
            activeNodeLifetimeSeconds = 0f;
            ActiveNodeChanged?.Invoke(null);
            ActiveNodeTimerUpdated?.Invoke(0f, 0f);
        }

        private void RegisterNodeCallbacks(PersonalOreNode node)
        {
            if (node == null)
                return;

            node.Expired += HandleActiveNodeExpired;
            node.LifetimeChanged += HandleActiveNodeLifetimeChanged;
        }

        private void UnregisterNodeCallbacks(PersonalOreNode node)
        {
            if (node == null)
                return;

            node.Expired -= HandleActiveNodeExpired;
            node.LifetimeChanged -= HandleActiveNodeLifetimeChanged;
        }

        private void HandleActiveNodeExpired(PersonalOreNode node)
        {
            if (node != activeNode)
                return;

            LogDebug("Active personal ore node reported lifetime expiry.");

            UnregisterNodeCallbacks(node);
            activeNode = null;
            activeNodeLifetimeSeconds = 0f;
            ActiveNodeChanged?.Invoke(null);
            ActiveNodeTimerUpdated?.Invoke(0f, 0f);
        }

        private void HandleActiveNodeLifetimeChanged(PersonalOreNode node, float remainingSeconds, float normalized)
        {
            if (node != activeNode)
                return;

            float normalizedLifetime = activeNodeLifetimeSeconds > 0f
                ? Mathf.Clamp01(remainingSeconds / activeNodeLifetimeSeconds)
                : Mathf.Clamp01(normalized);

            ActiveNodeTimerUpdated?.Invoke(Mathf.Max(0f, remainingSeconds), normalizedLifetime);
        }

        private bool HasCharmEquipped(string charmItemId)
        {
            if (string.IsNullOrWhiteSpace(charmItemId) || equipment == null)
                return false;

            var entry = equipment.GetEquipped(EquipmentSlot.Charm);
            var equippedItem = entry.item;
            if (equippedItem == null || string.IsNullOrEmpty(equippedItem.id))
                return false;

            bool matches = string.Equals(
                equippedItem.id.Trim(),
                charmItemId.Trim(),
                StringComparison.OrdinalIgnoreCase);

            LogDebug($"Charm check for '{charmItemId}': {(matches ? "matched" : "not matched")} (equipped: '{equippedItem.id}').");
            return matches;
        }

        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[MiningPersonalNodeController] {message}", this);
        }
    }
}
