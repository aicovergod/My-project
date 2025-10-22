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
    /// Enumerates the possible outcomes when issuing a mining command to the companion.
    /// </summary>
    public enum CompanionMiningCommandResult
    {
        /// <summary>Command accepted and mining has started.</summary>
        Accepted,
        /// <summary>Companion backpack cannot hold additional ore.</summary>
        InventoryFull,
        /// <summary>Command rejected because requirements (levels, ownership, etc.) were not met.</summary>
        RequirementsNotMet,
        /// <summary>Command blocked because the player is interacting with the rock.</summary>
        BlockedByPlayer,
        /// <summary>Companion lacks a valid pickaxe.</summary>
        NoPickaxe,
        /// <summary>Target rock cannot be reached or interacted with.</summary>
        Unreachable,
        /// <summary>Companion is already working on the requested rock.</summary>
        AlreadyMining
    }

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
        private CompanionEquipment companionEquipment;
        private MiningSkill miningSkill;
        private PetFollower petFollower;
        private PetPathMover pathMover;
        private Rigidbody2D body;
        private Coroutine miningRoutine;
        private Coroutine areaMiningRoutine;
        private MineableRock currentRock;
        private PickaxeDefinition currentPickaxe;
        private Dictionary<string, ItemData> itemCache;
        private bool miningActive;
        private bool followerDisabledForMining;
        private bool suppressMiningStopCallback;

        private readonly List<MineableRock> areaCandidates = new List<MineableRock>();
        private readonly List<Vector3> areaCandidateTileCenters = new List<Vector3>();
        private readonly HashSet<MineableRock> playerProtectedSingleOre = new HashSet<MineableRock>();

        private bool areaMiningActive;
        private float activeAreaRadius;
        private MiningSkill playerMiningSkill;
        private Transform playerTransform;

        /// <summary>
        /// Initialises the mining controller with the owning companion components.
        /// </summary>
        /// <param name="ownerController">Controller that owns this component.</param>
        /// <param name="skills">Skill manager used for level checks.</param>
        /// <param name="inventoryComponent">Inventory wrapper providing access to the backpack.</param>
        /// <param name="player">Player transform driving proximity logic.</param>
        public void Initialise(
            CompanionController ownerController,
            SkillManager skills,
            CompanionInventory inventoryComponent,
            Transform player)
        {
            if (ownerController == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning("[Companion Mining] Initialise invoked without a companion controller reference.", this);

            if (skills == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning("[Companion Mining] Initialise received a null SkillManager reference.", this);

            skillManager = skills;
            inventory = inventoryComponent != null ? inventoryComponent.InventoryComponent : null;

            if (inventory == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning("[Companion Mining] No inventory available for tool checks.", this);

            companionEquipment = ownerController != null ? ownerController.Equipment : null;

            miningSkill = GetComponent<MiningSkill>();
            if (miningSkill == null)
                miningSkill = gameObject.AddComponent<MiningSkill>();

            if (miningSkill != null)
            {
                miningSkill.OnStopMining -= HandleMiningStopped;
                miningSkill.OnStopMining += HandleMiningStopped;
                miningSkill.ConfigureCompanionChat(CompanionManager.GetCompanionDisplayName);
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
            areaMiningActive = false;
            activeAreaRadius = 0f;

            RebindPlayer(player);
        }

        /// <summary>
        /// Rebinds the controller to a new player transform so navigation and player mining hooks stay in sync.
        /// </summary>
        /// <param name="player">Player transform to follow.</param>
        public void RebindPlayer(Transform player)
        {
            playerTransform = player;
            BindToPlayerMiningSkill(playerTransform);
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied rock.
        /// </summary>
        /// <param name="rock">Rock that should be mined.</param>
        /// <returns>True when the command was accepted, otherwise false.</returns>
        public bool TryCommandMine(MineableRock rock)
        {
            bool accepted = TryCommandMine(rock, out var result);
            return accepted || result == CompanionMiningCommandResult.InventoryFull;
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied rock and reports the resulting status.
        /// </summary>
        /// <param name="rock">Rock that should be mined.</param>
        /// <param name="result">Detailed result describing whether the command was accepted.</param>
        /// <returns>True when mining started, otherwise false.</returns>
        public bool TryCommandMine(MineableRock rock, out CompanionMiningCommandResult result)
        {
            result = CompanionMiningCommandResult.RequirementsNotMet;

            if (!TryPrepareMiningCommand(rock, out var pickaxe, out result))
                return false;

            CancelAreaMiningInternal(true);
            BeginMining(rock, pickaxe);

            result = CompanionMiningCommandResult.Accepted;
            return true;
        }

        /// <summary>
        /// Initiates an area mining routine that scans nearby rocks and mines them sequentially.
        /// </summary>
        /// <param name="radius">Scan radius in Unity units (tiles).</param>
        /// <returns>True when area mining started successfully.</returns>
        public bool TryStartAreaMining(float radius)
        {
            return TryStartAreaMining(radius, out _);
        }

        /// <summary>
        /// Initiates an area mining routine that scans nearby rocks and mines them sequentially.
        /// </summary>
        /// <param name="radius">Scan radius in Unity units (tiles).</param>
        /// <param name="failureReason">Detailed reason describing why the command failed when <c>false</c> is returned.</param>
        /// <returns>True when area mining started successfully.</returns>
        public bool TryStartAreaMining(float radius, out CompanionMiningCommandResult failureReason)
        {
            failureReason = CompanionMiningCommandResult.RequirementsNotMet;

            if (!isActiveAndEnabled || miningSkill == null || skillManager == null)
                return false;

            float clampedRadius = Mathf.Max(0.1f, radius);

            CancelAreaMiningInternal(true);

            if (!BuildAreaCandidateList(clampedRadius, out failureReason))
            {
                PublishAreaMiningFailureMessage(failureReason);
                return false;
            }

            failureReason = CompanionMiningCommandResult.Accepted;

            activeAreaRadius = clampedRadius;
            areaMiningRoutine = StartCoroutine(AreaMiningRoutine());
            areaMiningActive = true;

            if (CompanionManager.EnableDebugLogging)
            {
                Debug.Log($"[Companion Mining] Area mining started with {areaCandidates.Count} candidates (radius {activeAreaRadius}).", this);
            }

            return true;
        }

        /// <summary>
        /// Stops the active mining routine and optionally restores the follower component.
        /// </summary>
        /// <param name="restoreFollower">Whether the companion follower should be re-enabled.</param>
        public void CancelMining(bool restoreFollower)
        {
            CancelAreaMiningInternal(false);
            StopActiveMiningRoutine();
            CleanupAfterMining(restoreFollower);
            UnsubscribeFromPlayerMiningSkill();
            BindToPlayerMiningSkill(playerTransform);
        }

        /// <summary>
        /// Cancels the running area mining routine and optionally restores the follower.
        /// </summary>
        /// <param name="restoreFollower">True when the follower should resume immediately.</param>
        public void CancelAreaMining(bool restoreFollower)
        {
            CancelAreaMiningInternal(restoreFollower);
            UnsubscribeFromPlayerMiningSkill();
            BindToPlayerMiningSkill(playerTransform);
        }

        private void BeginMining(MineableRock rock, PickaxeDefinition pickaxe)
        {
            StopActiveMiningRoutine();

            currentRock = rock;
            currentPickaxe = pickaxe;
            miningRoutine = StartCoroutine(MineRoutine(rock, pickaxe));
            miningActive = true;

            if (CompanionManager.EnableDebugLogging)
            {
                Debug.Log($"[Companion Mining] Command accepted for {rock.name} using {pickaxe.DisplayName} (tier {pickaxe.Tier}).", this);
            }
        }

        private void StopActiveMiningRoutine()
        {
            if (miningRoutine != null)
            {
                StopCoroutine(miningRoutine);
                miningRoutine = null;
            }

            if (miningSkill != null && miningSkill.IsMining)
            {
                suppressMiningStopCallback = true;
                miningSkill.StopMining();
                suppressMiningStopCallback = false;
            }

            currentRock = null;
            currentPickaxe = null;
            miningActive = false;
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

            pathMover?.ResetAttackTracking();

            while (rock != null && !rock.IsDepleted)
            {
                if (!isActiveAndEnabled)
                    break;

                Vector3 rockPosition = rock.transform.position;
                float distance = Vector2.Distance(transform.position, rockPosition);

                if (distance > MiningRange)
                {
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
                            ApplyMovement(nextPosition, navVelocity, teleported);
                    }

                    if (!navigationStepTaken)
                    {
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

            if (areaMiningActive)
            {
                // Allow the area routine to continue scanning for additional rocks.
                yield break;
            }

            CancelAreaMiningInternal(false);
        }

        private bool TryPrepareMiningCommand(
            MineableRock rock,
            out PickaxeDefinition pickaxe,
            out CompanionMiningCommandResult result,
            bool suppressChat = false)
        {
            pickaxe = null;
            result = CompanionMiningCommandResult.RequirementsNotMet;

            if (rock == null || rock.IsDepleted)
            {
                result = CompanionMiningCommandResult.Unreachable;
                return false;
            }

            if (miningSkill == null || skillManager == null || !isActiveAndEnabled)
            {
                result = CompanionMiningCommandResult.RequirementsNotMet;
                return false;
            }

            if (miningActive && currentRock == rock)
            {
                result = CompanionMiningCommandResult.AlreadyMining;
                return false;
            }

            var personalNode = rock.GetComponent<PersonalOreNode>();
            if (personalNode != null && !personalNode.CanMine(miningSkill, out _))
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Mining] Personal node rejected mining request.", this);
                result = CompanionMiningCommandResult.Unreachable;
                return false;
            }

            var rockDef = rock.RockDef;
            var oreDef = rockDef != null ? rockDef.Ore : null;
            if (oreDef == null)
            {
                result = CompanionMiningCommandResult.Unreachable;
                return false;
            }

            if (rockDef.DepleteAfterNOres == 1 && playerProtectedSingleOre.Contains(rock))
            {
                if (!suppressChat)
                    PublishBlockedByPlayerMessage();
                result = CompanionMiningCommandResult.BlockedByPlayer;
                return false;
            }

            if (miningSkill.Level < oreDef.LevelRequirement)
            {
                if (!suppressChat)
                {
                    var chat = ChatService.Instance;
                    chat?.PublishCompanionMessage(
                        CompanionManager.GetCompanionDisplayName(),
                        "I don't have the correct mining level for that");
                }

                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Mining] Command blocked by mining level requirement.", this);

                result = CompanionMiningCommandResult.RequirementsNotMet;
                return false;
            }

            pickaxe = ResolvePickaxe(rockDef);
            if (pickaxe == null)
            {
                if (!suppressChat)
                    PublishMissingPickaxeMessage();
                result = CompanionMiningCommandResult.NoPickaxe;
                return false;
            }

            if (!HasInventoryCapacityForOre(oreDef, suppressChat))
            {
                result = CompanionMiningCommandResult.InventoryFull;
                return false;
            }

            result = CompanionMiningCommandResult.Accepted;
            return true;
        }

        private PickaxeDefinition ResolvePickaxe(RockDefinition rockDef)
        {
            if (rockDef == null)
                return null;

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
                return null;

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

                var item = GatheringInventoryHelper.GetItemData(definition.Id, ref itemCache);
                bool ownsInInventory = inventory != null && item != null && inventory.GetItemCount(item) > 0;
                bool equippedTool = false;

                if (companionEquipment != null && item != null)
                {
                    var entry = companionEquipment.GetEquipped(EquipmentSlot.Weapon);
                    equippedTool = entry.item == item;
                }

                if (!ownsInInventory && !equippedTool)
                    continue;

                return definition;
            }

            return null;
        }

        private bool HasInventoryCapacityForOre(OreDefinition ore, bool suppressChat)
        {
            if (ore == null || miningSkill == null)
                return true;

            if (miningSkill.CanAddOre(ore))
                return true;

            if (!suppressChat)
                PublishInventoryFullMessage();

            if (CompanionManager.EnableDebugLogging)
                Debug.Log("[Companion Mining] Command rejected because the companion inventory is full.", this);

            return false;
        }

        private IEnumerator AreaMiningRoutine()
        {
            while (areaCandidates.Count > 0)
            {
                for (int i = 0; i < areaCandidates.Count; i++)
                {
                    var rock = areaCandidates[i];
                    if (rock == null || rock.IsDepleted)
                        continue;

                    if (rock.RockDef != null && rock.RockDef.DepleteAfterNOres == 1 && playerProtectedSingleOre.Contains(rock))
                        continue;

                    if (!TryPrepareMiningCommand(rock, out var pickaxe, out var result))
                    {
                        if (result == CompanionMiningCommandResult.InventoryFull)
                        {
                            PublishInventoryFullMessage();
                            CancelAreaMiningInternal(true);
                            yield break;
                        }

                        continue;
                    }

                    BeginMining(rock, pickaxe);

                    while (miningActive && currentRock == rock)
                        yield return null;

                    if (!areaMiningActive)
                        yield break;
                }

                if (!BuildAreaCandidateList(activeAreaRadius, out var rebuildFailure, suppressChat: true))
                {
                    PublishAreaMiningFailureMessage(rebuildFailure);
                    CancelAreaMiningInternal(true);
                    yield break;
                }

                yield return null;
            }

            PublishNoRocksMessage();
            CancelAreaMiningInternal(true);
        }

        private bool BuildAreaCandidateList(float radius, out CompanionMiningCommandResult failureReason, bool suppressChat = true)
        {
            areaCandidates.Clear();
            areaCandidateTileCenters.Clear();

            var rocks = FindObjectsOfType<MineableRock>();
            float radiusSqr = radius * radius;

            Vector2 controllerPosition2D = (Vector2)transform.position;
            bool observedNonInventoryFailure = false;
            CompanionMiningCommandResult lastNonInventoryFailure = CompanionMiningCommandResult.Unreachable;

            for (int i = 0; i < rocks.Length; i++)
            {
                var rock = rocks[i];
                if (rock == null || rock.IsDepleted)
                    continue;

                Vector2 rockPosition2D = (Vector2)rock.transform.position;
                if ((rockPosition2D - controllerPosition2D).sqrMagnitude > radiusSqr)
                    continue;

                if (rock.RockDef != null && rock.RockDef.DepleteAfterNOres == 1 && playerProtectedSingleOre.Contains(rock))
                    continue;

                if (!TryPrepareMiningCommand(rock, out var _, out var validationResult, suppressChat))
                {
                    if (validationResult == CompanionMiningCommandResult.InventoryFull)
                    {
                        failureReason = CompanionMiningCommandResult.InventoryFull;
                        return false;
                    }

                    if (validationResult != CompanionMiningCommandResult.InventoryFull &&
                        validationResult != CompanionMiningCommandResult.Accepted)
                    {
                        observedNonInventoryFailure = true;
                        lastNonInventoryFailure = validationResult;
                    }

                    continue;
                }

                areaCandidates.Add(rock);
            }

            areaCandidates.Sort((a, b) =>
            {
                if (a == null && b == null)
                    return 0;
                if (a == null)
                    return 1;
                if (b == null)
                    return -1;

                Vector2 aPosition2D = (Vector2)a.transform.position;
                Vector2 bPosition2D = (Vector2)b.transform.position;
                float da = (aPosition2D - controllerPosition2D).sqrMagnitude;
                float db = (bPosition2D - controllerPosition2D).sqrMagnitude;
                return da.CompareTo(db);
            });

            for (int i = 0; i < areaCandidates.Count; i++)
            {
                var candidate = areaCandidates[i];
                if (candidate == null)
                    continue;

                areaCandidateTileCenters.Add(GetTileCentre(candidate.transform.position));
            }

            if (areaCandidates.Count == 0)
            {
                failureReason = observedNonInventoryFailure
                    ? lastNonInventoryFailure
                    : CompanionMiningCommandResult.Unreachable;
                return false;
            }

            failureReason = CompanionMiningCommandResult.Accepted;
            return true;
        }

        private void PublishAreaMiningFailureMessage(CompanionMiningCommandResult failureReason)
        {
            switch (failureReason)
            {
                case CompanionMiningCommandResult.InventoryFull:
                    PublishInventoryFullMessage();
                    break;
                case CompanionMiningCommandResult.NoPickaxe:
                    PublishMissingPickaxeMessage();
                    break;
                case CompanionMiningCommandResult.BlockedByPlayer:
                    PublishBlockedByPlayerMessage();
                    break;
                default:
                    PublishNoRocksMessage();
                    break;
            }
        }

        private Vector3 GetTileCentre(Vector3 worldPosition)
        {
            float x = Mathf.Round(worldPosition.x);
            float y = Mathf.Round(worldPosition.y);
            return new Vector3(x, y, worldPosition.z);
        }

        private void PublishInventoryFullMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionManager.InventoryFullChatLine);
        }

        private void PublishMissingPickaxeMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                "I need a pickaxe to mine that");
        }

        private void PublishNoRocksMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                "I don't see any mineable rocks round here");
        }

        private void PublishBlockedByPlayerMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                "Looks like you're already mining that rock");
        }

        private float ResolveMoveSpeed()
        {
            return petFollower != null ? Mathf.Max(0.1f, petFollower.moveSpeed) : 5f;
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
            if (suppressMiningStopCallback)
                return;

            miningActive = false;
            CleanupAfterMining(true);
        }

        private void BindToPlayerMiningSkill(Transform player)
        {
            UnsubscribeFromPlayerMiningSkill();

            if (player == null)
                return;

            playerMiningSkill = player.GetComponent<MiningSkill>();
            if (playerMiningSkill == null)
                return;

            playerMiningSkill.OnStartMining += OnPlayerStartMining;
            playerMiningSkill.OnStopMining += OnPlayerStopMining;
        }

        private void UnsubscribeFromPlayerMiningSkill()
        {
            if (playerMiningSkill == null)
                return;

            playerMiningSkill.OnStartMining -= OnPlayerStartMining;
            playerMiningSkill.OnStopMining -= OnPlayerStopMining;
            playerMiningSkill = null;
            playerProtectedSingleOre.Clear();
        }

        private void OnPlayerStartMining(MineableRock rock)
        {
            if (rock == null)
                return;

            if (rock.RockDef != null && rock.RockDef.DepleteAfterNOres == 1)
                playerProtectedSingleOre.Add(rock);

            if (miningActive && currentRock == rock && rock.RockDef != null && rock.RockDef.DepleteAfterNOres == 1)
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Mining] Player started mining the same single-ore rock. Cancelling companion mining.", this);

                StopActiveMiningRoutine();
                CleanupAfterMining(true);
            }
        }

        private void OnPlayerStopMining()
        {
            playerProtectedSingleOre.Clear();
        }

        private void CancelAreaMiningInternal(bool restoreFollower)
        {
            if (areaMiningRoutine != null)
            {
                StopCoroutine(areaMiningRoutine);
                areaMiningRoutine = null;
            }

            areaMiningActive = false;
            activeAreaRadius = 0f;
            areaCandidates.Clear();
            areaCandidateTileCenters.Clear();

            StopActiveMiningRoutine();

            if (restoreFollower)
                CleanupAfterMining(true);
        }

        private void OnDisable()
        {
            CancelMining(true);
            UnsubscribeFromPlayerMiningSkill();
        }

        private void OnDestroy()
        {
            if (miningSkill != null)
                miningSkill.OnStopMining -= HandleMiningStopped;

            CancelMining(true);
            UnsubscribeFromPlayerMiningSkill();
        }

        private void OnDrawGizmosSelected()
        {
            if (!areaMiningActive || activeAreaRadius <= 0f)
                return;

            Gizmos.color = new Color(0.8f, 0.8f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, activeAreaRadius);

            Gizmos.color = new Color(0.2f, 0.9f, 0.9f, 0.6f);
            for (int i = 0; i < areaCandidateTileCenters.Count; i++)
            {
                Vector3 center = areaCandidateTileCenters[i];
                Gizmos.DrawWireCube(center, new Vector3(1f, 1f, 0f));
            }
        }
    }
}
