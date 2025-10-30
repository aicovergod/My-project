using System;
using System.Collections;
using System.Collections.Generic;
using Inventory;
using Pets;
using Skills;
using Skills.Common;
using Skills.Mining;
using UI.Chat;
using UnityEngine;
using Companions.Equipment;
using RuntimeInventory = global::Inventory.Inventory;

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
        AlreadyMining,
        /// <summary>Command declined because the companion is observing a cooldown.</summary>
        Declined
    }

    /// <summary>
    /// Handles companion-directed mining commands by approaching rocks, validating requirements,
    /// and delegating the actual mining routine to <see cref="MiningSkill"/> once in range.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionMiningController : CompanionGatheringControllerBase<MineableRock, CompanionMiningCommandResult>
    {
        private SkillManager skillManager;
        private RuntimeInventory inventory;
        private CompanionEquipment companionEquipment;
        private MiningSkill miningSkill;
        private Coroutine miningRoutine;
        private MineableRock currentRock;
        private PickaxeDefinition currentPickaxe;
        private Dictionary<string, ItemData> itemCache;
        private bool miningActive;
        private bool suppressMiningStopCallback;
        private readonly HashSet<MineableRock> playerProtectedSingleOre = new HashSet<MineableRock>();

        private MiningSkill playerMiningSkill;
        private Transform playerTransform;

        /// <summary>
        /// True while the mining controller has an active mining routine or the underlying skill reports mining activity.
        /// Exposed so UI layers and chat commands can determine whether a stop request should be surfaced.
        /// </summary>
        public bool IsMining => miningActive || (miningSkill != null && miningSkill.IsMining);

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
            Transform player,
            CompanionSkillCooldownTracker cooldownTracker)
        {
            skillManager = skills;
            RuntimeInventory resolvedInventory;
            CompanionEquipment resolvedEquipment;

            ConfigureGatheringSkill(
                ownerController,
                skills,
                inventoryComponent,
                companionEquipment,
                cooldownTracker,
                "Companion Mining",
                skill =>
                {
                    miningSkill = skill;
                    miningSkill.OnStopMining -= HandleMiningStopped;
                    miningSkill.OnStopMining += HandleMiningStopped;
                    miningSkill.ConfigureCompanionChat(CompanionManager.GetCompanionDisplayName);
                },
                () =>
                {
                    miningActive = false;
                    suppressMiningStopCallback = false;
                    playerProtectedSingleOre.Clear();
                },
                out resolvedInventory,
                out resolvedEquipment);

            inventory = resolvedInventory;
            companionEquipment = resolvedEquipment;

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
            return TryCommandAllowingInventoryFull(rock);
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied rock while preserving
        /// follower hold state when a new mining routine is about to take ownership.
        /// </summary>
        /// <param name="rock">Rock that should be mined.</param>
        /// <param name="preserveFollowerHold">
        /// When <c>true</c>, existing follower holds remain intact during the hand-off so
        /// systems like ore-golem automation can transition without briefly enabling the follower.
        /// </param>
        /// <returns>True when mining started or the inventory was full.</returns>
        public bool TryCommandMine(MineableRock rock, bool preserveFollowerHold)
        {
            return TryCommandAllowingInventoryFull(rock, preserveFollowerHold);
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied rock and reports the resulting status.
        /// </summary>
        /// <param name="rock">Rock that should be mined.</param>
        /// <param name="result">Detailed result describing whether the command was accepted.</param>
        /// <returns>True when mining started, otherwise false.</returns>
        public bool TryCommandMine(MineableRock rock, out CompanionMiningCommandResult result)
        {
            return TryCommandWithResult(rock, out result);
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied rock and reports the resulting status.
        /// </summary>
        /// <param name="rock">Rock that should be mined.</param>
        /// <param name="result">Detailed result describing whether the command was accepted.</param>
        /// <param name="preserveFollowerHold">
        /// When <c>true</c>, existing follower holds remain intact so automation can transfer control
        /// without briefly re-enabling the follower component.
        /// </param>
        /// <returns>True when mining started, otherwise false.</returns>
        public bool TryCommandMine(
            MineableRock rock,
            out CompanionMiningCommandResult result,
            bool preserveFollowerHold)
        {
            return TryCommandWithResult(rock, out result, preserveFollowerHold);
        }

        /// <inheritdoc />
        protected override CommandAttempt PerformGatheringCommand(MineableRock rock, bool preserveFollowerHold)
        {
            var attempt = new CommandAttempt
            {
                Accepted = false,
                Result = CompanionMiningCommandResult.RequirementsNotMet
            };

            if (CompanionSkillCooldownTimers.ShouldDeclineMiningRequest(skillCooldownTracker, out var cooldownResult))
            {
                attempt.Result = cooldownResult;
                return attempt;
            }

            if (!TryPrepareMiningCommand(rock, out var pickaxe, out var validationResult))
            {
                attempt.Result = validationResult;
                return attempt;
            }

            CancelAreaMiningInternal(true, preserveFollowerHold);
            followerDisabledForGathering = preserveFollowerHold ? HasActiveFollowerHold : false;
            BeginMining(rock, pickaxe);

            attempt.Accepted = true;
            attempt.Result = CompanionMiningCommandResult.Accepted;
            CompanionSkillCooldownTimers.ClearMiningCooldown(skillCooldownTracker);
            return attempt;
        }

        /// <inheritdoc />
        protected override bool ShouldTreatInventoryFullAsSuccess(CompanionMiningCommandResult result)
        {
            return result == CompanionMiningCommandResult.InventoryFull;
        }

        /// <inheritdoc />
        protected override bool IsNodeDepleted(MineableRock node)
        {
            return node == null || node.IsDepleted;
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

            if (CompanionSkillCooldownTimers.ShouldDeclineMiningRequest(skillCooldownTracker, out failureReason))
                return false;

            bool started = TryStartAreaGathering(
                radius,
                out failureReason,
                CompanionMiningCommandResult.Accepted,
                clampedRadius =>
                {
                    bool success = BuildAreaCandidateList(clampedRadius, out var buildFailure);
                    return (success, buildFailure);
                },
                PublishAreaMiningFailureMessage,
                AreaMiningRoutine,
                () => CompanionSkillCooldownTimers.ClearMiningCooldown(skillCooldownTracker),
                "Companion Mining",
                StopActiveMiningRoutine,
                preserveFollowerLocks: false);

            return started;
        }

        /// <summary>
        /// Stops the active mining routine and optionally restores the follower component.
        /// </summary>
        /// <param name="restoreFollower">Whether the companion follower should be re-enabled.</param>
        public void CancelMining(bool restoreFollower)
        {
            CancelAreaMiningInternal(false);
            CleanupAfterMining(restoreFollower);
            BindToPlayerMiningSkill(playerTransform);
            ResetStuckHistory();
        }

        /// <summary>
        /// Cancels the running area mining routine and optionally restores the follower.
        /// </summary>
        /// <param name="restoreFollower">True when the follower should resume immediately.</param>
        public void CancelAreaMining(bool restoreFollower)
        {
            CancelAreaMiningInternal(restoreFollower);
            BindToPlayerMiningSkill(playerTransform);
            ResetStuckHistory();
        }

        private void BeginMining(MineableRock rock, PickaxeDefinition pickaxe)
        {
            StopActiveMiningRoutine();

            currentRock = rock;
            currentPickaxe = pickaxe;
            miningActive = true;
            miningRoutine = StartCoroutine(MineRoutine(rock, pickaxe));

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
            var followerHold = EnterTemporaryFollowerHold();
            bool stuckTriggered = false;
            MineableRock stuckRock = null;
            float noProgressTimer = 0f;
            float lastRecordedDistance = 0f;
            float cumulativeDistanceClosed = 0f;
            bool hasDistanceSample = false;
            try
            {
                pathMover?.ResetAttackTracking();

                while (rock != null && !rock.IsDepleted)
                {
                    if (!isActiveAndEnabled)
                        break;

                    if (!miningActive || currentRock == null || currentRock != rock)
                        break;

                    Vector3 rockPosition = rock.transform.position;
                    float distance = Vector2.Distance(transform.position, rockPosition);

                    if (!hasDistanceSample)
                    {
                        hasDistanceSample = true;
                        lastRecordedDistance = distance;
                        cumulativeDistanceClosed = 0f;
                        noProgressTimer = 0f;
                    }
                    else
                    {
                        float delta = lastRecordedDistance - distance;
                        if (delta > 0f)
                        {
                            cumulativeDistanceClosed += delta;
                        }
                        else if (delta < 0f)
                        {
                            cumulativeDistanceClosed = 0f;
                        }

                        bool closedGap = cumulativeDistanceClosed >= ProgressResetThreshold;
                        bool effectivelyClose = distance <= GatheringRange * CloseEnoughDistanceMultiplier;
                        bool activelyMining = miningSkill != null && miningSkill.IsMining;

                        if (closedGap || effectivelyClose || activelyMining)
                        {
                            noProgressTimer = 0f;
                            cumulativeDistanceClosed = 0f;
                        }
                        else
                        {
                            noProgressTimer += Time.deltaTime;
                        }

                        lastRecordedDistance = distance;
                    }

                    if (noProgressTimer >= stuckTimeoutSeconds)
                    {
                        if (CompanionManager.EnableDebugLogging)
                        {
                            Debug.Log("[Companion Mining] Movement stalled while approaching the rock.", this);
                        }

                        stuckTriggered = true;
                        stuckRock = rock;
                        break;
                    }

                    if (distance > GatheringRange)
                    {
                        float moveSpeed = ResolveMoveSpeed();
                        float deltaTime = body != null
                            ? Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon)
                            : Mathf.Max(Time.deltaTime, Mathf.Epsilon);

                        bool navigationStepTaken = false;
                        bool navigationUnavailable = true;

                        if (pathMover != null && pathMover.isActiveAndEnabled)
                        {
                            navigationUnavailable = !pathMover.HasActiveNavigationGrid;

                            if (!navigationUnavailable)
                            {
                                Vector2 nextPosition;
                                Vector2 navVelocity;
                                bool teleported;
                                bool goalUnreachable;
                                // Use an infinite teleport detection threshold so distant rocks never trigger the
                                // teleport branch inside PetPathMover. This keeps the companion on its path and
                                // prevents accidental snaps across the nav grid when scanning large radii.
                                float teleportDetectionDistance = float.PositiveInfinity;

                                navigationStepTaken = pathMover.TryStepAttack(
                                    deltaTime,
                                    moveSpeed,
                                    GatheringRange,
                                    WaypointTolerance,
                                    () => rock != null ? (Vector2)rock.transform.position : (Vector2)transform.position,
                                    ReplanDistance,
                                    teleportDetectionDistance,
                                    out nextPosition,
                                    out navVelocity,
                                    out teleported,
                                    out goalUnreachable);

                                if (goalUnreachable)
                                {
                                    if (CompanionManager.EnableDebugLogging)
                                        Debug.Log("[Companion Mining] Navigation reported the rock as unreachable.", this);
                                    stuckTriggered = true;
                                    stuckRock = rock;
                                    break;
                                }

                                if (navigationStepTaken)
                                    ApplyMovement(nextPosition, navVelocity, teleported);
                            }
                        }

                        if (stuckTriggered)
                            break;

                        if (!navigationStepTaken)
                        {
                            if (navigationUnavailable)
                            {
                                Vector3 startPosition = transform.position;
                                Vector3 nextPosition = Vector3.MoveTowards(startPosition, rockPosition, moveSpeed * deltaTime);
                                Vector2 velocity = deltaTime > Mathf.Epsilon
                                    ? (Vector2)((nextPosition - startPosition) / deltaTime)
                                    : Vector2.zero;
                                ApplyMovement(nextPosition, velocity, false);
                            }
                            else if (body != null)
                            {
                                body.linearVelocity = Vector2.zero;
                            }
                        }

                        if (miningSkill.IsMining && distance > GatheringRange * 1.2f)
                            miningSkill.StopMining();
                    }
                    else
                    {
                        if (body != null)
                            body.linearVelocity = Vector2.zero;

                        if (!miningActive || currentRock == null || currentRock != rock)
                            break;

                        if (!miningSkill.IsMining)
                        {
                            if (!miningActive || currentRock == null || currentRock != rock)
                                break;

                            miningSkill.StartMining(rock, pickaxe);
                        }

                        if (!miningActive || currentRock == null || currentRock != rock)
                            break;

                        if (!miningSkill.IsMining)
                            break;
                    }

                    if (rock == null || rock.IsDepleted)
                        break;

                    yield return null;
                }
            }
            finally
            {
                followerHold.Dispose();
            }

            if (stuckTriggered)
            {
                HandleMiningStuck(stuckRock);
                yield break;
            }

            miningRoutine = null;
            miningActive = false;

            if (miningSkill != null && miningSkill.IsMining)
                miningSkill.StopMining();

            CleanupAfterMining(true);
            ResetStuckHistory();

            if (areaRoutineActive)
            {
                // Allow the area routine to continue scanning for additional rocks.
                yield break;
            }

            CancelAreaMiningInternal(false);
        }

        private void HandleMiningStuck(MineableRock rock)
        {
            if (CompanionManager.EnableDebugLogging)
            {
                string rockName = rock != null ? rock.name : "<null>";
                Debug.Log($"[Companion Mining] Detected a stuck state while targeting {rockName}.", this);
            }

            float now = Time.time;
            if (rock != null)
                MarkNodeBlocked(rock, now + stuckTimeoutSeconds);

            if (miningSkill != null && miningSkill.IsMining)
            {
                suppressMiningStopCallback = true;
                miningSkill.StopMining();
                suppressMiningStopCallback = false;
            }

            CleanupAfterMining(true);
            miningRoutine = null;
            miningActive = false;

            pathMover?.ResetFollowTracking();

            if (petFollower != null && playerTransform != null)
                petFollower.SetPlayer(playerTransform);

            if (rock != null)
            {
                if (rock == lastStuckNode)
                {
                    consecutiveStuckNodeCount++;
                }
                else
                {
                    lastStuckNode = rock;
                    consecutiveStuckNodeCount = 1;
                }
            }
            else
            {
                lastStuckNode = null;
                consecutiveStuckNodeCount = 0;
            }

            if (consecutiveStuckNodeCount >= ConsecutiveStuckCancelThreshold)
            {
                CancelMiningDueToStuck();
            }
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

            float now = Time.time;
            PruneExpiredBlockedNodes();

            if (IsNodeTemporarilyBlocked(rock, now))
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
                        CompanionMiningDialogueLibrary.GetLevelRequirementLine(oreDef.LevelRequirement));
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

            if (!HasInventoryCapacityForOreInternal(oreDef, suppressChat))
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

        /// <summary>
        /// Determines whether the companion inventory can accept the supplied ore without
        /// emitting player-facing chat. External systems can call <see cref="HasInventoryCapacityForOre(OreDefinition)"/>
        /// when they need to validate storage silently before issuing a mining command.
        /// </summary>
        /// <param name="ore">Ore that should be checked for capacity.</param>
        /// <param name="suppressChat">When <c>true</c>, prevents the inventory full message from being published.</param>
        /// <returns><c>true</c> when the ore can be stored, otherwise <c>false</c>.</returns>
        private bool HasInventoryCapacityForOreInternal(OreDefinition ore, bool suppressChat)
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

        /// <summary>
        /// Exposes a silent inventory capacity check for external automation flows that need to
        /// determine whether a mining command should be issued.
        /// </summary>
        /// <param name="ore">Ore that should be validated.</param>
        /// <returns><c>true</c> when the ore fits in the companion inventory, otherwise <c>false</c>.</returns>
        public bool HasInventoryCapacityForOre(OreDefinition ore)
        {
            return HasInventoryCapacityForOreInternal(ore, suppressChat: true);
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

                    if (IsNodeTemporarilyBlocked(rock, Time.time))
                        continue;

                    // Suppress internal chat lines because the branch below publishes the
                    // descriptive message when inventory space is exhausted. This keeps the
                    // companion from spamming duplicate "inventory full" chat entries.
                    if (!TryPrepareMiningCommand(rock, out var pickaxe, out var result, suppressChat: true))
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

                    if (!areaRoutineActive)
                        yield break;
                }

                if (!BuildAreaCandidateList(activeAreaRadius, out var rebuildFailure, suppressChat: true))
                {
                    if (areaAllCandidatesBlocked)
                    {
                        CancelMiningDueToStuck();
                        yield break;
                    }

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
            areaAllCandidatesBlocked = false;

            var rocks = FindObjectsOfType<MineableRock>();
            float radiusSqr = radius * radius;

            Vector2 controllerPosition2D = (Vector2)transform.position;
            bool observedNonInventoryFailure = false;
            CompanionMiningCommandResult lastNonInventoryFailure = CompanionMiningCommandResult.Unreachable;
            int blockedByStuckCount = 0;

            float now = Time.time;
            PruneExpiredBlockedNodes();

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

                if (IsNodeTemporarilyBlocked(rock, now))
                {
                    blockedByStuckCount++;
                    continue;
                }

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
                if (blockedByStuckCount > 0 && !observedNonInventoryFailure)
                    areaAllCandidatesBlocked = true;

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
                case CompanionMiningCommandResult.Declined:
                    // Cooldown messaging is emitted when the decline is detected.
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

        private void CancelMiningDueToStuck()
        {
            if (CompanionManager.EnableDebugLogging)
                Debug.Log("[Companion Mining] Cancelling mining because the companion is stuck.", this);

            CancelAreaMiningInternal(true);
            PublishStuckApologyMessage();
            areaAllCandidatesBlocked = false;
            ResetStuckHistory();
        }

        private void PublishStuckApologyMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionMiningDialogueLibrary.GetRandomStuckApologyLine());
        }

        private void ResetStuckHistory()
        {
            ResetStuckHistoryInternal();
        }

        /// <summary>
        /// Publishes the standard companion chat line that indicates the inventory has run out of space.
        /// External callers (such as automation routines) use this when skipping mining interactions so the
        /// player still receives feedback about the blocked action.
        /// </summary>
        public void PublishInventoryFullMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionChatLibrary.GetRandomInventoryFullLine());
        }

        private void PublishMissingPickaxeMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionMiningDialogueLibrary.GetRandomMissingPickaxeLine());
        }

        private void PublishNoRocksMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionChatLibrary.GetRandomNoRocksLine());
        }

        private void PublishBlockedByPlayerMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionMiningDialogueLibrary.GetRandomPlayerBusyLine());
        }

        private void CleanupAfterMining(bool restoreFollower, bool preserveFollowerLocks = false)
        {
            CleanupFollowerAfterGathering(restoreFollower, preserveFollowerLocks);

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
            ResetStuckHistory();
        }

        private void BindToPlayerMiningSkill(Transform player)
        {
            BindPlayerSkillEvents(
                player,
                ref playerMiningSkill,
                skill =>
                {
                    skill.OnStartMining += OnPlayerStartMining;
                    skill.OnStopMining += OnPlayerStopMining;
                },
                skill =>
                {
                    skill.OnStartMining -= OnPlayerStartMining;
                    skill.OnStopMining -= OnPlayerStopMining;
                },
                playerProtectedSingleOre.Clear);
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

        private void CancelAreaMiningInternal(bool restoreFollower, bool preserveFollowerLocks = false)
        {
            CancelAreaInternal(restoreFollower, preserveFollowerLocks, StopActiveMiningRoutine);
        }

        private void OnDisable()
        {
            HandleDisable(
                () => CancelMining(true),
                () => BindToPlayerMiningSkill(null),
                () => playerProtectedSingleOre.Clear());
        }

        private void OnDestroy()
        {
            HandleDestroy(
                () => CancelMining(true),
                () => BindToPlayerMiningSkill(null),
                () =>
                {
                    if (miningSkill != null)
                        miningSkill.OnStopMining -= HandleMiningStopped;
                },
                () => playerProtectedSingleOre.Clear());
        }

        private void OnDrawGizmosSelected()
        {
            if (!areaRoutineActive || activeAreaRadius <= 0f)
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
