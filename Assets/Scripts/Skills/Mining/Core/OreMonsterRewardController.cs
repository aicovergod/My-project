using NPC;
using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    ///     Listens for an ore monster's death and grants the defeating player a personal
    ///     ore node reward. The node definition controls which prefab is spawned while the
    ///     killer's <see cref="MiningPersonalNodeController"/> manages the lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OreMonsterRewardController : MonoBehaviour
    {
        [Header("Reward Setup")]
        [SerializeField, Tooltip("Definition that determines which personal node to spawn after the monster dies.")]
        private OreMonsterNodeDefinition nodeDefinition;

        [Header("Debug")]
        [SerializeField, Tooltip("When enabled the controller will emit verbose logging for spawn attempts.")]
        private bool enableDebugLogging;

        private NpcCombatant npcCombatant;

        private void Awake()
        {
            npcCombatant = GetComponent<NpcCombatant>();
            if (npcCombatant == null)
            {
                Debug.LogError("OreMonsterRewardController requires an NpcCombatant on the same GameObject to operate.", this);
            }
        }

        private void OnEnable()
        {
            if (npcCombatant == null)
            {
                npcCombatant = GetComponent<NpcCombatant>();
                if (npcCombatant == null)
                {
                    Debug.LogError("OreMonsterRewardController could not locate the required NpcCombatant during OnEnable.", this);
                    return;
                }
            }

            npcCombatant.OnKilledByPlayer += HandleKilledByPlayer;
        }

        private void OnDisable()
        {
            if (npcCombatant != null)
                npcCombatant.OnKilledByPlayer -= HandleKilledByPlayer;
        }

        /// <summary>
        ///     Handles the ore monster being killed by a player by spawning the configured
        ///     personal node at the monster's current position.
        /// </summary>
        /// <param name="combatant">The combatant instance that was killed.</param>
        /// <param name="killingPlayer">GameObject responsible for the finishing blow.</param>
        private void HandleKilledByPlayer(NpcCombatant combatant, GameObject killingPlayer)
        {
            if (nodeDefinition == null)
            {
                LogDebug("No node definition assigned, skipping personal ore node spawn.");
                return;
            }

            if (killingPlayer == null)
            {
                LogDebug("Killing player reference was null, cannot award personal ore node.");
                return;
            }

            var personalNodeController = ResolvePersonalNodeController(killingPlayer);
            if (personalNodeController == null)
            {
                LogDebug($"Unable to locate MiningPersonalNodeController on killer '{killingPlayer.name}', skipping spawn.");
                return;
            }

            Vector3 spawnPosition = transform.position;
            personalNodeController.SpawnNode(nodeDefinition, spawnPosition);

            LogDebug($"Spawned personal ore node '{nodeDefinition.name}' for '{killingPlayer.name}' at {spawnPosition}.");
        }

        /// <summary>
        ///     Attempts to locate the mining personal node controller responsible for the
        ///     killing player by searching the provided GameObject, its hierarchy, and any
        ///     child objects.
        /// </summary>
        private static MiningPersonalNodeController ResolvePersonalNodeController(GameObject killer)
        {
            if (killer == null)
                return null;

            if (killer.TryGetComponent(out MiningPersonalNodeController directController))
                return directController;

            var fromParent = killer.GetComponentInParent<MiningPersonalNodeController>();
            if (fromParent != null)
                return fromParent;

            var root = killer.transform.root;
            if (root != null)
            {
                if (root.TryGetComponent(out MiningPersonalNodeController rootController))
                    return rootController;

                var rootChildController = root.GetComponentInChildren<MiningPersonalNodeController>();
                if (rootChildController != null)
                    return rootChildController;
            }

            return killer.GetComponentInChildren<MiningPersonalNodeController>();
        }

        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[OreMonsterRewardController] {message}", this);
        }
    }
}

