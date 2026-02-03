using System;
using System.Collections;
using System.Collections.Generic;
using Inventory;
using Pets;
using Skills;
using Skills.Common;
using Skills.Woodcutting;
using UI.Chat;
using UnityEngine;
using Companions.Equipment;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions
{
    /// <summary>
    /// Enumerates the possible outcomes when issuing a woodcutting command to the companion.
    /// </summary>
    public enum CompanionWoodcuttingCommandResult
    {
        /// <summary>Command accepted and woodcutting has started.</summary>
        Accepted,
        /// <summary>Companion backpack cannot hold additional logs.</summary>
        InventoryFull,
        /// <summary>Command rejected because requirements (levels, ownership, etc.) were not met.</summary>
        RequirementsNotMet,
        /// <summary>Command blocked because the player is interacting with the tree.</summary>
        BlockedByPlayer,
        /// <summary>Companion lacks a valid axe.</summary>
        NoAxe,
        /// <summary>Target tree cannot be reached or interacted with.</summary>
        Unreachable,
        /// <summary>Companion is already working on the requested tree.</summary>
        AlreadyChopping,
        /// <summary>Command declined because the companion is observing a cooldown.</summary>
        Declined
    }

    /// <summary>
    /// Handles companion-directed woodcutting commands by approaching trees, validating requirements,
    /// and delegating the actual woodcutting routine to <see cref="WoodcuttingSkill"/> once in range.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionWoodcuttingController : CompanionGatheringControllerBase<TreeNode, CompanionWoodcuttingCommandResult>
    {
        private SkillManager skillManager;
        private RuntimeInventory inventory;
        private CompanionEquipment companionEquipment;
        private WoodcuttingSkill woodcuttingSkill;
        private Coroutine woodcuttingRoutine;
        private TreeNode currentTree;
        private AxeDefinition currentAxe;
        private Dictionary<string, ItemData> itemCache;
        private bool woodcuttingActive;
        private bool suppressWoodcuttingStopCallback;
        private readonly HashSet<TreeNode> playerProtectedSingleLog = new HashSet<TreeNode>();

        private WoodcuttingSkill playerWoodcuttingSkill;
        private Transform playerTransform;

        /// <summary>
        /// True while the woodcutting controller has an active routine or the woodcutting skill reports chopping activity.
        /// Enables UI surfaces and chat commands to reflect the current action accurately.
        /// </summary>
        public bool IsWoodcutting => woodcuttingActive || (woodcuttingSkill != null && woodcuttingSkill.IsChopping);

        /// <summary>
        /// Initialises the woodcutting controller with the owning companion components.
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

            ConfigureGatheringSkill<WoodcuttingSkill>(
                ownerController,
                skills,
                inventoryComponent,
                companionEquipment,
                cooldownTracker,
                "Companion Woodcutting",
                skill =>
                {
                    woodcuttingSkill = skill;
                    woodcuttingSkill.OnStopChopping -= HandleWoodcuttingStopped;
                    woodcuttingSkill.OnStopChopping += HandleWoodcuttingStopped;
                    woodcuttingSkill.ConfigureCompanionChat(CompanionManager.GetCompanionDisplayName);
                },
                () =>
                {
                    woodcuttingActive = false;
                    suppressWoodcuttingStopCallback = false;
                    playerProtectedSingleLog.Clear();
                },
                out resolvedInventory,
                out resolvedEquipment);

            inventory = resolvedInventory;
            companionEquipment = resolvedEquipment;

            RebindPlayer(player);
        }

        /// <summary>
        /// Rebinds the controller to a new player transform so navigation and player woodcutting hooks stay in sync.
        /// </summary>
        /// <param name="player">Player transform to follow.</param>
        public void RebindPlayer(Transform player)
        {
            playerTransform = player;
            BindToPlayerWoodcuttingSkill(playerTransform);
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied tree.
        /// </summary>
        /// <param name="tree">Tree that should be mined.</param>
        /// <returns>True when the command was accepted, otherwise false.</returns>
        public bool TryCommandChop(TreeNode tree)
        {
            return TryCommandAllowingInventoryFull(tree);
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied tree while preserving
        /// follower hold state when a new woodcutting routine is about to take ownership.
        /// </summary>
        /// <param name="tree">Tree that should be mined.</param>
        /// <param name="preserveFollowerHold">
        /// When <c>true</c>, existing follower holds remain intact during the hand-off so
        /// systems like log-golem automation can transition without briefly enabling the follower.
        /// </param>
        /// <returns>True when woodcutting started or the inventory was full.</returns>
        public bool TryCommandChop(TreeNode tree, bool preserveFollowerHold)
        {
            return TryCommandAllowingInventoryFull(tree, preserveFollowerHold);
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied tree and reports the resulting status.
        /// </summary>
        /// <param name="tree">Tree that should be mined.</param>
        /// <param name="result">Detailed result describing whether the command was accepted.</param>
        /// <returns>True when woodcutting started, otherwise false.</returns>
        public bool TryCommandChop(TreeNode tree, out CompanionWoodcuttingCommandResult result)
        {
            return TryCommandWithResult(tree, out result);
        }

        /// <summary>
        /// Attempts to command the companion to mine the supplied tree and reports the resulting status.
        /// </summary>
        /// <param name="tree">Tree that should be mined.</param>
        /// <param name="result">Detailed result describing whether the command was accepted.</param>
        /// <param name="preserveFollowerHold">
        /// When <c>true</c>, existing follower holds remain intact so automation can transfer control
        /// without briefly re-enabling the follower component.
        /// </param>
        /// <returns>True when woodcutting started, otherwise false.</returns>
        public bool TryCommandChop(
            TreeNode tree,
            out CompanionWoodcuttingCommandResult result,
            bool preserveFollowerHold)
        {
            return TryCommandWithResult(tree, out result, preserveFollowerHold);
        }

        /// <inheritdoc />
        protected override CommandAttempt PerformGatheringCommand(TreeNode tree, bool preserveFollowerHold)
        {
            var attempt = new CommandAttempt
            {
                Accepted = false,
                Result = CompanionWoodcuttingCommandResult.RequirementsNotMet
            };

            if (CompanionSkillCooldownTimers.ShouldDecline(
                skillCooldownTracker,
                SkillType.Woodcutting,
                CompanionWoodcuttingCommandResult.Accepted,
                CompanionWoodcuttingCommandResult.Declined,
                out var cooldownResult))
            {
                attempt.Result = cooldownResult;
                return attempt;
            }

            if (!TryPrepareWoodcuttingCommand(tree, out var axe, out var validationResult))
            {
                attempt.Result = validationResult;
                return attempt;
            }

            CancelAreaWoodcuttingInternal(true, preserveFollowerHold);
            followerDisabledForGathering = preserveFollowerHold ? HasActiveFollowerHold : false;
            BeginWoodcutting(tree, axe);

            attempt.Accepted = true;
            attempt.Result = CompanionWoodcuttingCommandResult.Accepted;
            CompanionSkillCooldownTimers.ClearCooldown(skillCooldownTracker, SkillType.Woodcutting);
            return attempt;
        }

        /// <inheritdoc />
        protected override bool ShouldTreatInventoryFullAsSuccess(CompanionWoodcuttingCommandResult result)
        {
            return result == CompanionWoodcuttingCommandResult.InventoryFull;
        }

        /// <inheritdoc />
        protected override bool IsNodeDepleted(TreeNode node)
        {
            return node == null || node.IsDepleted;
        }

        /// <summary>
        /// Initiates an area woodcutting routine that scans nearby rocks and mines them sequentially.
        /// </summary>
        /// <param name="radius">Scan radius in Unity units (tiles).</param>
        /// <returns>True when area woodcutting started successfully.</returns>
        public bool TryStartAreaWoodcutting(float radius)
        {
            return TryStartAreaWoodcutting(radius, out _);
        }

        /// <summary>
        /// Initiates an area woodcutting routine that scans nearby rocks and mines them sequentially.
        /// </summary>
        /// <param name="radius">Scan radius in Unity units (tiles).</param>
        /// <param name="failureReason">Detailed reason describing why the command failed when <c>false</c> is returned.</param>
        /// <returns>True when area woodcutting started successfully.</returns>
        public bool TryStartAreaWoodcutting(float radius, out CompanionWoodcuttingCommandResult failureReason)
        {
            failureReason = CompanionWoodcuttingCommandResult.RequirementsNotMet;

            if (!isActiveAndEnabled || woodcuttingSkill == null || skillManager == null)
                return false;

            if (CompanionSkillCooldownTimers.ShouldDecline(
                skillCooldownTracker,
                SkillType.Woodcutting,
                CompanionWoodcuttingCommandResult.Accepted,
                CompanionWoodcuttingCommandResult.Declined,
                out failureReason))
                return false;

            bool started = TryStartAreaGathering(
                radius,
                out failureReason,
                CompanionWoodcuttingCommandResult.Accepted,
                clampedRadius =>
                {
                    bool success = BuildAreaCandidateList(clampedRadius, out var buildFailure);
                    return (success, buildFailure);
                },
                PublishAreaWoodcuttingFailureMessage,
                AreaWoodcuttingRoutine,
                () => CompanionSkillCooldownTimers.ClearCooldown(skillCooldownTracker, SkillType.Woodcutting),
                "Companion Woodcutting",
                StopActiveWoodcuttingRoutine,
                preserveFollowerLocks: false);

            return started;
        }

        /// <summary>
        /// Stops the active woodcutting routine and optionally restores the follower component.
        /// </summary>
        /// <param name="restoreFollower">Whether the companion follower should be re-enabled.</param>
        public void CancelWoodcutting(bool restoreFollower)
        {
            CancelAreaWoodcuttingInternal(false);
            CleanupAfterWoodcutting(restoreFollower);
            BindToPlayerWoodcuttingSkill(playerTransform);
            ResetStuckHistory();
        }

        /// <summary>
        /// Cancels the running area woodcutting routine and optionally restores the follower.
        /// </summary>
        /// <param name="restoreFollower">True when the follower should resume immediately.</param>
        public void CancelAreaWoodcutting(bool restoreFollower)
        {
            CancelAreaWoodcuttingInternal(restoreFollower);
            BindToPlayerWoodcuttingSkill(playerTransform);
            ResetStuckHistory();
        }

        private void BeginWoodcutting(TreeNode tree, AxeDefinition axe)
        {
            StopActiveWoodcuttingRoutine();

            currentTree = tree;
            currentAxe = axe;
            woodcuttingActive = true;
            woodcuttingRoutine = StartCoroutine(ChopRoutine(tree, axe));

            if (CompanionManager.EnableDebugLogging)
            {
                Debug.Log($"[Companion Woodcutting] Command accepted for {tree.name} using {axe.DisplayName} (power {axe.Power}).", this);
            }
        }

        private void StopActiveWoodcuttingRoutine()
        {
            if (woodcuttingRoutine != null)
            {
                StopCoroutine(woodcuttingRoutine);
                woodcuttingRoutine = null;
            }

            if (woodcuttingSkill != null && woodcuttingSkill.IsChopping)
            {
                suppressWoodcuttingStopCallback = true;
                woodcuttingSkill.StopChopping();
                suppressWoodcuttingStopCallback = false;
            }

            currentTree = null;
            currentAxe = null;
            woodcuttingActive = false;
        }

        private IEnumerator ChopRoutine(TreeNode tree, AxeDefinition axe)
        {
            GatheringMovementRoutineResult routineResult = default;

            yield return CompanionGatheringMovementRoutine(new GatheringMovementRoutineParameters
            {
                GetTargetNode = () => tree,
                IsCommandActive = () => woodcuttingActive && currentTree != null && currentTree == tree,
                IsNodeValid = node => node != null && !node.IsDepleted,
                GetTargetPosition = node => node.transform.position,
                IsSkillActive = () => woodcuttingSkill != null && woodcuttingSkill.IsChopping,
                StartSkill = node =>
                {
                    if (!woodcuttingActive || currentTree == null || currentTree != node || woodcuttingSkill == null)
                        return;

                    woodcuttingSkill.StartChopping(node, axe);
                },
                StopSkill = () =>
                {
                    if (woodcuttingSkill != null)
                        woodcuttingSkill.StopChopping();
                },
                OnProgressStalled = node =>
                {
                    if (CompanionManager.EnableDebugLogging)
                        Debug.Log("[Companion Woodcutting] Movement stalled while approaching the tree.", this);
                },
                OnGoalUnreachableDetected = node =>
                {
                    if (CompanionManager.EnableDebugLogging)
                        Debug.Log("[Companion Woodcutting] Navigation reported the tree as unreachable.", this);
                },
                OnGoalUnreachable = HandleWoodcuttingStuck,
                OnStuck = HandleWoodcuttingStuck,
                OutOfRangeSkillStopMultiplier = 1.2f,
                OnRoutineComplete = result => routineResult = result,
            });

            if (routineResult.Stuck)
                yield break;

            woodcuttingRoutine = null;
            woodcuttingActive = false;

            if (woodcuttingSkill != null && woodcuttingSkill.IsChopping)
                woodcuttingSkill.StopChopping();

            CleanupAfterWoodcutting(true);
            ResetStuckHistory();

            if (areaRoutineActive)
            {
                // Allow the area routine to continue scanning for additional trees.
                yield break;
            }

            CancelAreaWoodcuttingInternal(false);
        }

        private void HandleWoodcuttingStuck(TreeNode tree)
        {
            ExecuteGatheringStuckRecovery(new GatheringStuckRecoveryParameters
            {
                Node = tree,
                DebugLabel = "Companion Woodcutting",
                BuildDebugMessage = target =>
                {
                    string treeName = target != null ? target.name : "<null>";
                    return $"[Companion Woodcutting] Detected a stuck state while targeting {treeName}.";
                },
                ShouldStopSkill = () => woodcuttingSkill != null && woodcuttingSkill.IsChopping,
                SetStopCallbackSuppressed = value => suppressWoodcuttingStopCallback = value,
                StopSkill = () =>
                {
                    if (woodcuttingSkill != null)
                        woodcuttingSkill.StopChopping();
                },
                CleanupCallback = () => CleanupAfterWoodcutting(true),
                AdditionalStateReset = () =>
                {
                    woodcuttingRoutine = null;
                    woodcuttingActive = false;
                },
                OnThresholdReached = (_, __) => CancelWoodcuttingDueToStuck(),
            });
        }

        private bool TryPrepareWoodcuttingCommand(
            TreeNode tree,
            out AxeDefinition axe,
            out CompanionWoodcuttingCommandResult result,
            bool suppressChat = false)
        {
            axe = null;
            result = CompanionWoodcuttingCommandResult.RequirementsNotMet;

            if (tree == null || tree.IsDepleted)
            {
                result = CompanionWoodcuttingCommandResult.Unreachable;
                return false;
            }

            float now = Time.time;
            PruneExpiredBlockedNodes();

            if (IsNodeTemporarilyBlocked(tree, now))
            {
                result = CompanionWoodcuttingCommandResult.Unreachable;
                return false;
            }

            if (woodcuttingSkill == null || skillManager == null || !isActiveAndEnabled)
            {
                result = CompanionWoodcuttingCommandResult.RequirementsNotMet;
                return false;
            }

            if (woodcuttingActive && currentTree == tree)
            {
                result = CompanionWoodcuttingCommandResult.AlreadyChopping;
                return false;
            }

            var treeDef = tree.def;
            if (treeDef == null)
            {
                result = CompanionWoodcuttingCommandResult.Unreachable;
                return false;
            }

            if (treeDef.DepletesAfterOneLog && playerProtectedSingleLog.Contains(tree))
            {
                if (!suppressChat)
                    PublishBlockedByPlayerMessage();
                result = CompanionWoodcuttingCommandResult.BlockedByPlayer;
                return false;
            }

            if (woodcuttingSkill.Level < treeDef.RequiredWoodcuttingLevel)
            {
                if (!suppressChat)
                {
                    var chat = ChatService.Instance;
                    if (chat != null)
                    {
                        string message = CompanionWoodcuttingDialogueLibrary.GetLevelRequirementLine(
                            treeDef.RequiredWoodcuttingLevel);
                        chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
                    }
                }

                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Woodcutting] Command blocked by woodcutting level requirement.", this);

                result = CompanionWoodcuttingCommandResult.RequirementsNotMet;
                return false;
            }

            axe = ResolveAxe(treeDef);
            if (axe == null)
            {
                if (!suppressChat)
                    PublishMissingAxeMessage();
                result = CompanionWoodcuttingCommandResult.NoAxe;
                return false;
            }

            if (!HasInventoryCapacityForLogsInternal(treeDef, suppressChat))
            {
                result = CompanionWoodcuttingCommandResult.InventoryFull;
                return false;
            }

            result = CompanionWoodcuttingCommandResult.Accepted;
            return true;
        }

        private AxeDefinition ResolveAxe(TreeDefinition treeDef)
        {
            if (treeDef == null)
                return null;

            int woodcuttingLevel = woodcuttingSkill != null ? woodcuttingSkill.Level : 1;

            return CompanionToolResolver.ResolveBestTool(
                AxeDefinitionRegistry.GetAllDefinitions,
                CompanionToolSelectorRegistry.RegisterAxesFromSelectors,
                inventory,
                companionEquipment,
                ref itemCache,
                definition => definition?.Id,
                definition => definition != null && definition.RequiredWoodcuttingLevel <= woodcuttingLevel);
        }

        /// <summary>
        /// Determines whether the companion inventory can accept the supplied log without
        /// emitting player-facing chat. External systems can call <see cref="HasInventoryCapacityForLogs(TreeDefinition)"/>
        /// when they need to validate storage silently before issuing a woodcutting command.
        /// </summary>
        /// <param name="log">Log that should be checked for capacity.</param>
        /// <param name="suppressChat">When <c>true</c>, prevents the inventory full message from being published.</param>
        /// <returns><c>true</c> when the log can be stored, otherwise <c>false</c>.</returns>
        private bool HasInventoryCapacityForLogsInternal(TreeDefinition logDefinition, bool suppressChat)
        {
            if (logDefinition == null || woodcuttingSkill == null)
                return true;

            if (woodcuttingSkill.CanAddLog(logDefinition))
                return true;

            if (!suppressChat)
                PublishInventoryFullMessage();

            if (CompanionManager.EnableDebugLogging)
                Debug.Log("[Companion Woodcutting] Command rejected because the companion inventory is full.", this);

            return false;
        }

        /// <summary>
        /// Exposes a silent inventory capacity check for external automation flows that need to
        /// determine whether a woodcutting command should be issued.
        /// </summary>
        /// <param name="log">Log that should be validated.</param>
        /// <returns><c>true</c> when the log fits in the companion inventory, otherwise <c>false</c>.</returns>
        public bool HasInventoryCapacityForLogs(TreeDefinition log)
        {
            return HasInventoryCapacityForLogsInternal(log, suppressChat: true);
        }

        private IEnumerator AreaWoodcuttingRoutine()
        {
            while (areaCandidates.Count > 0)
            {
                for (int i = 0; i < areaCandidates.Count; i++)
                {
                    var tree = areaCandidates[i];
                    if (tree == null || tree.IsDepleted)
                        continue;

                    if (tree.def != null && tree.def.DepletesAfterOneLog && playerProtectedSingleLog.Contains(tree))
                        continue;

                    if (IsNodeTemporarilyBlocked(tree, Time.time))
                        continue;

                    // Suppress internal chat lines because the branch below publishes the
                    // descriptive message when inventory space is exhausted. This keeps the
                    // companion from spamming duplicate "inventory full" chat entries.
                    if (!TryPrepareWoodcuttingCommand(tree, out var axe, out var result, suppressChat: true))
                    {
                        if (result == CompanionWoodcuttingCommandResult.InventoryFull)
                        {
                            PublishInventoryFullMessage();
                            CancelAreaWoodcuttingInternal(true);
                            yield break;
                        }

                        continue;
                    }

                    BeginWoodcutting(tree, axe);

                    while (woodcuttingActive && currentTree == tree)
                        yield return null;

                    if (!areaRoutineActive)
                        yield break;
                }

                if (!BuildAreaCandidateList(activeAreaRadius, out var rebuildFailure, suppressChat: true))
                {
                    if (areaAllCandidatesBlocked)
                    {
                        CancelWoodcuttingDueToStuck();
                        yield break;
                    }

                    PublishAreaWoodcuttingFailureMessage(rebuildFailure);
                    CancelAreaWoodcuttingInternal(true);
                    yield break;
                }

                yield return null;
            }

            PublishNoTreesMessage();
            CancelAreaWoodcuttingInternal(true);
        }

        private bool BuildAreaCandidateList(float radius, out CompanionWoodcuttingCommandResult failureReason, bool suppressChat = true)
        {
            var outcome = BuildAreaCandidates(
                radius,
                retrieveNodes: () => FindObjectsOfType<TreeNode>(),
                shouldSkipNode: tree =>
                {
                    if (tree == null)
                        return true;

                    return tree.def != null && tree.def.DepletesAfterOneLog && playerProtectedSingleLog.Contains(tree);
                },
                tryPrepareCommand: tree =>
                {
                    bool accepted = TryPrepareWoodcuttingCommand(tree, out var _, out var validationResult, suppressChat);
                    return (accepted, validationResult);
                },
                acceptedResultFactory: () => CompanionWoodcuttingCommandResult.Accepted,
                defaultFailureResultFactory: () => CompanionWoodcuttingCommandResult.Unreachable,
                isInventoryFullResult: result => result == CompanionWoodcuttingCommandResult.InventoryFull,
                isAcceptedResult: result => result == CompanionWoodcuttingCommandResult.Accepted);

            failureReason = outcome.failureReason;
            return outcome.success;
        }

        private void PublishAreaWoodcuttingFailureMessage(CompanionWoodcuttingCommandResult failureReason)
        {
            switch (failureReason)
            {
                case CompanionWoodcuttingCommandResult.InventoryFull:
                    PublishInventoryFullMessage();
                    break;
                case CompanionWoodcuttingCommandResult.NoAxe:
                    PublishMissingAxeMessage();
                    break;
                case CompanionWoodcuttingCommandResult.BlockedByPlayer:
                    PublishBlockedByPlayerMessage();
                    break;
                case CompanionWoodcuttingCommandResult.Declined:
                    // Cooldown messaging is emitted when the decline is detected.
                    break;
                default:
                    PublishNoTreesMessage();
                    break;
            }
        }

        private void CancelWoodcuttingDueToStuck()
        {
            if (CompanionManager.EnableDebugLogging)
                Debug.Log("[Companion Woodcutting] Cancelling woodcutting because the companion is stuck.", this);

            CancelAreaWoodcuttingInternal(true);
            PublishStuckApologyMessage();
            areaAllCandidatesBlocked = false;
            ResetStuckHistory();
        }

        private void PublishStuckApologyMessage()
        {
            PublishCompanionChatLine(CompanionWoodcuttingDialogueLibrary.GetRandomStuckApologyLine);
        }

        private void ResetStuckHistory()
        {
            ResetStuckHistoryInternal();
        }

        /// <summary>
        /// Publishes the standard companion chat line that indicates the inventory has run out of space.
        /// External callers (such as automation routines) use this when skipping woodcutting interactions so the
        /// player still receives feedback about the blocked action.
        /// </summary>
        public void PublishInventoryFullMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionWoodcuttingDialogueLibrary.GetRandomInventoryFullLine());
        }

        private void PublishMissingAxeMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionWoodcuttingDialogueLibrary.GetRandomMissingAxeLine());
        }

        private void PublishNoTreesMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionWoodcuttingDialogueLibrary.GetRandomNoTreesLine());
        }

        private void PublishBlockedByPlayerMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionWoodcuttingDialogueLibrary.GetRandomPlayerBusyLine());
        }

        private void CleanupAfterWoodcutting(bool restoreFollower, bool preserveFollowerLocks = false)
        {
            CleanupFollowerAfterGathering(restoreFollower, preserveFollowerLocks);

            if (body != null)
                body.linearVelocity = Vector2.zero;

            pathMover?.ResetAttackTracking();

            currentTree = null;
            currentAxe = null;
            woodcuttingActive = false;
        }

        private void HandleWoodcuttingStopped()
        {
            if (suppressWoodcuttingStopCallback)
                return;

            woodcuttingActive = false;
            CleanupAfterWoodcutting(true);
            ResetStuckHistory();
        }

        private void BindToPlayerWoodcuttingSkill(Transform player)
        {
            BindPlayerSkillEvents(
                player,
                ref playerWoodcuttingSkill,
                skill =>
                {
                    skill.OnStartChopping += OnPlayerStartChopping;
                    skill.OnStopChopping += OnPlayerStopChopping;
                },
                skill =>
                {
                    skill.OnStartChopping -= OnPlayerStartChopping;
                    skill.OnStopChopping -= OnPlayerStopChopping;
                },
                playerProtectedSingleLog.Clear);
        }

        private void OnPlayerStartChopping(TreeNode tree)
        {
            if (tree == null)
                return;

            if (tree.def != null && tree.def.DepletesAfterOneLog)
                playerProtectedSingleLog.Add(tree);

            if (woodcuttingActive && currentTree == tree && tree.def != null && tree.def.DepletesAfterOneLog)
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Woodcutting] Player started woodcutting the same single-log tree. Cancelling companion woodcutting.", this);

                StopActiveWoodcuttingRoutine();
                CleanupAfterWoodcutting(true);
            }
        }

        private void OnPlayerStopChopping()
        {
            playerProtectedSingleLog.Clear();
        }

        private void CancelAreaWoodcuttingInternal(bool restoreFollower, bool preserveFollowerLocks = false)
        {
            CancelAreaInternal(restoreFollower, preserveFollowerLocks, StopActiveWoodcuttingRoutine);
        }

        private void OnDisable()
        {
            HandleDisable(
                () => CancelWoodcutting(true),
                () => BindToPlayerWoodcuttingSkill(null),
                () => playerProtectedSingleLog.Clear());
        }

        private void OnDestroy()
        {
            HandleDestroy(
                () => CancelWoodcutting(true),
                () => BindToPlayerWoodcuttingSkill(null),
                () =>
                {
                    if (woodcuttingSkill != null)
                        woodcuttingSkill.OnStopChopping -= HandleWoodcuttingStopped;
                },
                () => playerProtectedSingleLog.Clear());
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
