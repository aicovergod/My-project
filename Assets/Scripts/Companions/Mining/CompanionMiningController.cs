using System.Collections;
using System.Collections.Generic;
using Inventory;
using Pets;
using Skills;
using Skills.Common;
using Skills.Mining;
using UI.Chat;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Handles companion-directed mining commands by approaching rocks, validating requirements,
    /// and delegating the actual mining routine to <see cref="MiningSkill"/> once in range.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionMiningController : MonoBehaviour
    {
        private const float MiningRange = 1.5f;
        private const float ReplanDistance = MiningRange * 0.75f;
        private const float TeleportDistance = MiningRange * 6f;
        private const float WaypointTolerance = 0.1f;

        private SkillManager skillManager;
        private Inventory.Inventory inventory;
        private Equipment equipment;
        private MiningSkill miningSkill;
        private PetFollower petFollower;
        private PetPathMover pathMover;
        private Rigidbody2D body;
        private Coroutine miningRoutine;
        private MineableRock currentRock;
        private PickaxeDefinition currentPickaxe;
        private Dictionary<string, ItemData> itemCache;
        private bool miningActive;
        private bool followerDisabledForMining;

        /// <summary>
        /// Initialises the mining controller with the owning companion components.
        /// </summary>
        /// <param name="ownerController">Controller that owns this component.</param>
        /// <param name="skills">Skill manager used for level checks.</param>
        /// <param name="inventoryComponent">Inventory wrapper providing access to the backpack.</param>
        public void Initialise(CompanionController ownerController, SkillManager skills, CompanionInventory inventoryComponent)
        {
            if (ownerController == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning("[Companion Mining] Initialise invoked without a companion controller reference.", this);

            if (skills == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning("[Companion Mining] Initialise received a null SkillManager reference.", this);

            skillManager = skills;
            inventory = inventoryComponent != null ? inventoryComponent.InventoryComponent : null;

            if (inventory == null && CompanionManager.EnableDebugLogging)
            {
                Debug.LogWarning("[Companion Mining] No inventory available for tool checks.", this);
            }

            equipment = GetComponent<Equipment>();
            if (equipment == null && inventory != null)
                equipment = inventory.GetComponent<Equipment>();

            miningSkill = GetComponent<MiningSkill>();
            if (miningSkill == null)
                miningSkill = gameObject.AddComponent<MiningSkill>();

            if (miningSkill != null)
            {
                miningSkill.OnStopMining -= HandleMiningStopped;
                miningSkill.OnStopMining += HandleMiningStopped;
            }
            else if (CompanionManager.EnableDebugLogging)
            {
                Debug.LogError("[Companion Mining] Failed to resolve MiningSkill component.", this);
            }

            petFollower = GetComponent<PetFollower>();
            pathMover = GetComponent<PetPathMover>();
            body = GetComponent<Rigidbody2D>();

            miningActive = false;
            followerDisabledForMining = false;
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied rock.
        /// </summary>
        /// <param name="rock">Rock that should be mined.</param>
        /// <returns>True when the command was accepted, otherwise false.</returns>
        public bool TryCommandMine(MineableRock rock)
        {
            if (rock == null || rock.IsDepleted)
                return false;

            if (miningSkill == null || skillManager == null)
                return false;

            if (!isActiveAndEnabled)
                return false;

            if (miningActive && currentRock == rock)
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Mining] Ignoring duplicate mine command for the current rock.", this);
                return false;
            }

            if (miningActive && currentRock != null && currentRock != rock)
                CancelMining(true);

            var personalNode = rock.GetComponent<PersonalOreNode>();
            if (personalNode != null && !personalNode.CanMine(miningSkill, out _))
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Mining] Personal node rejected mining request.", this);
                return false;
            }

            var rockDef = rock.RockDef;
            var oreDef = rockDef != null ? rockDef.Ore : null;
            if (oreDef == null)
                return false;

            // Confirm the companion meets the mining level requirement before proceeding.
            if (miningSkill.Level < oreDef.LevelRequirement)
            {
                var chat = ChatService.Instance;
                chat?.PublishCompanionMessage(
                    CompanionManager.GetCompanionDisplayName(),
                    "I don't have the correct mining level for that");
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Mining] Command blocked by mining level requirement.", this);
                return false;
            }

            // Ensure we have a definition cache to evaluate available pickaxes.
            var definitions = PickaxeDefinitionRegistry.GetAllDefinitions();
            if (definitions == null || definitions.Count == 0)
            {
                var selectors = FindObjectsOfType<PickaxeToUse>(true);
                for (int i = 0; i < selectors.Length; i++)
                {
                    var selector = selectors[i];
                    if (selector != null)
                        PickaxeDefinitionRegistry.RegisterDefinitions(selector.AllPickaxes);
                }

                definitions = PickaxeDefinitionRegistry.GetAllDefinitions();
            }

            if (definitions == null || definitions.Count == 0)
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Mining] No pickaxe definitions registered for selection.", this);
                return false;
            }

            PickaxeDefinition chosenPickaxe = null;
            int requiredTier = rockDef.RequiresToolTier;
            int miningLevel = miningSkill.Level;

            foreach (var definition in definitions)
            {
                if (definition == null)
                    continue;

                if (definition.LevelRequirement > miningLevel)
                    continue;

                if (definition.Tier < requiredTier)
                    continue;

                // Resolve the matching ItemData so we can check inventory and equipment ownership.
                var item = GatheringInventoryHelper.GetItemData(definition.Id, ref itemCache);
                bool ownsInInventory = inventory != null && item != null && inventory.GetItemCount(item) > 0;
                bool equippedTool = false;
                if (equipment != null && item != null)
                {
                    var entry = equipment.GetEquipped(EquipmentSlot.Weapon);
                    equippedTool = entry.item == item;
                }

                if (!ownsInInventory && !equippedTool)
                    continue;

                chosenPickaxe = definition;
                break;
            }

            if (chosenPickaxe == null)
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Mining] Unable to find a usable pickaxe for the command.", this);
                return false;
            }

            CancelMining(true);

            currentRock = rock;
            currentPickaxe = chosenPickaxe;
            miningRoutine = StartCoroutine(MineRoutine(rock, chosenPickaxe));
            miningActive = true;

            if (CompanionManager.EnableDebugLogging)
            {
                Debug.Log(
                    $"[Companion Mining] Command accepted for {rock.name} using {chosenPickaxe.DisplayName} (tier {chosenPickaxe.Tier}).",
                    this);
            }

            return true;
        }

        /// <summary>
        /// Stops the active mining routine and optionally restores the follower component.
        /// </summary>
        /// <param name="restoreFollower">Whether the companion follower should be re-enabled.</param>
        public void CancelMining(bool restoreFollower)
        {
            if (miningRoutine != null)
            {
                StopCoroutine(miningRoutine);
                miningRoutine = null;
            }

            if (miningSkill != null && miningSkill.IsMining)
                miningSkill.StopMining();

            CleanupAfterMining(restoreFollower);
        }

        private IEnumerator MineRoutine(MineableRock rock, PickaxeDefinition pickaxe)
        {
            if (petFollower != null)
            {
                followerDisabledForMining = petFollower.enabled;
                if (followerDisabledForMining)
                    petFollower.enabled = false;
            }
            else
            {
                followerDisabledForMining = false;
            }

            // Reset navigation state so the path mover can generate a clean route toward the rock.
            pathMover?.ResetAttackTracking();

            while (rock != null && !rock.IsDepleted)
            {
                if (!isActiveAndEnabled)
                    break;

                Vector3 rockPosition = rock.transform.position;
                float distance = Vector2.Distance(transform.position, rockPosition);

                if (distance > MiningRange)
                {
                    // Use pathfinding when available to respect world navigation data.
                    float moveSpeed = ResolveMoveSpeed();
                    float deltaTime = body != null
                        ? Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon)
                        : Mathf.Max(Time.deltaTime, Mathf.Epsilon);

                    bool navigationStepTaken = false;

                    if (pathMover != null && pathMover.isActiveAndEnabled)
                    {
                        Vector2 nextPosition;
                        Vector2 navVelocity;
                        bool teleported;
                        bool goalUnreachable;
                        navigationStepTaken = pathMover.TryStepAttack(
                            deltaTime,
                            moveSpeed,
                            MiningRange,
                            WaypointTolerance,
                            () => rock != null ? (Vector2)rock.transform.position : (Vector2)transform.position,
                            ReplanDistance,
                            TeleportDistance,
                            out nextPosition,
                            out navVelocity,
                            out teleported,
                            out goalUnreachable);

                        if (goalUnreachable)
                        {
                            if (CompanionManager.EnableDebugLogging)
                                Debug.Log("[Companion Mining] Navigation reported the rock as unreachable.", this);
                            break;
                        }

                        if (navigationStepTaken)
                        {
                            ApplyMovement(nextPosition, navVelocity, teleported);
                        }
                    }

                    if (!navigationStepTaken)
                    {
                        // Fall back to direct movement when navigation data is unavailable.
                        Vector3 startPosition = transform.position;
                        Vector3 nextPosition = Vector3.MoveTowards(startPosition, rockPosition, moveSpeed * deltaTime);
                        Vector2 velocity = deltaTime > Mathf.Epsilon
                            ? (Vector2)((nextPosition - startPosition) / deltaTime)
                            : Vector2.zero;
                        ApplyMovement(nextPosition, velocity, false);
                    }

                    if (miningSkill.IsMining && distance > MiningRange * 1.2f)
                        miningSkill.StopMining();
                }
                else
                {
                    if (body != null)
                        body.linearVelocity = Vector2.zero;

                    if (!miningSkill.IsMining)
                        miningSkill.StartMining(rock, pickaxe);

                    if (!miningSkill.IsMining)
                        break;
                }

                if (rock == null || rock.IsDepleted)
                    break;

                yield return null;
            }

            miningRoutine = null;
            miningActive = false;

            if (miningSkill != null && miningSkill.IsMining)
                miningSkill.StopMining();

            CleanupAfterMining(true);
        }

        private void ApplyMovement(Vector3 nextPosition, Vector2 velocity, bool teleported)
        {
            if (body != null)
            {
                if (teleported)
                {
                    body.position = nextPosition;
                    body.linearVelocity = Vector2.zero;
                }
                else
                {
                    body.MovePosition(nextPosition);
                    body.linearVelocity = velocity;
                }
            }
            else
            {
                transform.position = nextPosition;
            }
        }

        private float ResolveMoveSpeed()
        {
            return petFollower != null ? Mathf.Max(0.1f, petFollower.moveSpeed) : 5f;
        }

        private void CleanupAfterMining(bool restoreFollower)
        {
            if (restoreFollower && petFollower != null && followerDisabledForMining)
                petFollower.enabled = true;

            followerDisabledForMining = false;

            if (body != null)
                body.linearVelocity = Vector2.zero;

            pathMover?.ResetAttackTracking();

            currentRock = null;
            currentPickaxe = null;
            miningActive = false;
        }

        private void HandleMiningStopped()
        {
            CancelMining(true);
        }

        private void OnDisable()
        {
            CancelMining(true);
        }

        private void OnDestroy()
        {
            if (miningSkill != null)
                miningSkill.OnStopMining -= HandleMiningStopped;

            CancelMining(true);
        }
    }
}
