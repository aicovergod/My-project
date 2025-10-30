using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        private CompanionSkillCooldownTracker skillCooldownTracker;

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
            if (ownerController == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning("[Companion Woodcutting] Initialise invoked without a companion controller reference.", this);

            if (skills == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning("[Companion Woodcutting] Initialise received a null SkillManager reference.", this);

            skillManager = skills;
            inventory = inventoryComponent != null ? inventoryComponent.InventoryComponent : null;

            if (inventory == null && CompanionManager.EnableDebugLogging)
                Debug.LogWarning("[Companion Woodcutting] No inventory available for tool checks.", this);

            companionEquipment = ownerController != null ? ownerController.Equipment : null;

            woodcuttingSkill = GetComponent<WoodcuttingSkill>();
            if (woodcuttingSkill == null)
                woodcuttingSkill = gameObject.AddComponent<WoodcuttingSkill>();

            if (woodcuttingSkill != null)
            {
                woodcuttingSkill.OnStopChopping -= HandleWoodcuttingStopped;
                woodcuttingSkill.OnStopChopping += HandleWoodcuttingStopped;
                woodcuttingSkill.ConfigureCompanionChat(CompanionManager.GetCompanionDisplayName);
            }
            else if (CompanionManager.EnableDebugLogging)
            {
                Debug.LogError("[Companion Woodcutting] Failed to resolve WoodcuttingSkill component.", this);
            }

            InitialiseMovementComponents();
            ResetFollowerState();

            woodcuttingActive = false;
            followerDisabledForGathering = false;
            areaRoutineActive = false;
            activeAreaRadius = 0f;

            skillCooldownTracker = cooldownTracker;

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

            if (CompanionSkillCooldownTimers.ShouldDeclineWoodcuttingRequest(skillCooldownTracker, out var cooldownResult))
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
            CompanionSkillCooldownTimers.ClearWoodcuttingCooldown(skillCooldownTracker);
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

            if (CompanionSkillCooldownTimers.ShouldDeclineWoodcuttingRequest(skillCooldownTracker, out failureReason))
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
                () => CompanionSkillCooldownTimers.ClearWoodcuttingCooldown(skillCooldownTracker),
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
            UnsubscribeFromPlayerWoodcuttingSkill();
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
            UnsubscribeFromPlayerWoodcuttingSkill();
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
            var followerHold = EnterTemporaryFollowerHold();
            bool stuckTriggered = false;
            TreeNode stuckTree = null;
            float noProgressTimer = 0f;
            float lastRecordedDistance = 0f;
            float cumulativeDistanceClosed = 0f;
            bool hasDistanceSample = false;
            try
            {
                pathMover?.ResetAttackTracking();

                while (tree != null && !tree.IsDepleted)
                {
                    if (!isActiveAndEnabled)
                        break;

                    if (!woodcuttingActive || currentTree == null || currentTree != tree)
                        break;

                    Vector3 rockPosition = tree.transform.position;
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
                        bool activelyWoodcutting = woodcuttingSkill != null && woodcuttingSkill.IsChopping;

                        if (closedGap || effectivelyClose || activelyWoodcutting)
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
                            Debug.Log("[Companion Woodcutting] Movement stalled while approaching the tree.", this);
                        }

                        stuckTriggered = true;
                            stuckTree = tree;
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
                                    () => tree != null ? (Vector2)tree.transform.position : (Vector2)transform.position,
                                    ReplanDistance,
                                    teleportDetectionDistance,
                                    out nextPosition,
                                    out navVelocity,
                                    out teleported,
                                    out goalUnreachable);

                                if (goalUnreachable)
                                {
                                    if (CompanionManager.EnableDebugLogging)
                                        Debug.Log("[Companion Woodcutting] Navigation reported the tree as unreachable.", this);
                                    stuckTriggered = true;
                            stuckTree = tree;
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

                        if (woodcuttingSkill.IsChopping && distance > GatheringRange * 1.2f)
                            woodcuttingSkill.StopChopping();
                    }
                    else
                    {
                        if (body != null)
                            body.linearVelocity = Vector2.zero;

                        if (!woodcuttingActive || currentTree == null || currentTree != tree)
                            break;

                        if (!woodcuttingSkill.IsChopping)
                        {
                            if (!woodcuttingActive || currentTree == null || currentTree != tree)
                                break;

                            woodcuttingSkill.StartChopping(tree, axe);
                        }

                        if (!woodcuttingActive || currentTree == null || currentTree != tree)
                            break;

                        if (!woodcuttingSkill.IsChopping)
                            break;
                    }

                    if (tree == null || tree.IsDepleted)
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
                            HandleWoodcuttingStuck(stuckTree);
                yield break;
            }

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
            if (CompanionManager.EnableDebugLogging)
            {
                string treeName = tree != null ? tree.name : "<null>";
                Debug.Log($"[Companion Woodcutting] Detected a stuck state while targeting {treeName}.", this);
            }

            float now = Time.time;
            if (tree != null)
                MarkNodeBlocked(tree, now + stuckTimeoutSeconds);

            if (woodcuttingSkill != null && woodcuttingSkill.IsChopping)
            {
                suppressWoodcuttingStopCallback = true;
                woodcuttingSkill.StopChopping();
                suppressWoodcuttingStopCallback = false;
            }

            CleanupAfterWoodcutting(true);
            woodcuttingRoutine = null;
            woodcuttingActive = false;

            pathMover?.ResetFollowTracking();

            if (petFollower != null && playerTransform != null)
                petFollower.SetPlayer(playerTransform);

            if (tree != null)
            {
                if (tree == lastStuckNode)
                {
                    consecutiveStuckNodeCount++;
                }
                else
                {
                    lastStuckNode = tree;
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
                CancelWoodcuttingDueToStuck();
            }
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

            var definitions = AxeDefinitionRegistry.GetAllDefinitions();
            if (definitions == null || definitions.Count == 0)
            {
                RegisterAxesFromSelectors();
                definitions = AxeDefinitionRegistry.GetAllDefinitions();
            }

            if (definitions == null || definitions.Count == 0)
                return null;

            int woodcuttingLevel = woodcuttingSkill != null ? woodcuttingSkill.Level : 1;

            foreach (var definition in definitions)
            {
                if (definition == null)
                    continue;

                if (definition.RequiredWoodcuttingLevel > woodcuttingLevel)
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

        private void RegisterAxesFromSelectors()
        {
            var selectors = FindObjectsOfType<AxeToUse>(true);
            if (selectors == null || selectors.Length == 0)
                return;

            for (int i = 0; i < selectors.Length; i++)
            {
                var selector = selectors[i];
                if (selector == null)
                    continue;

                var axes = ReflectionAxeBuffer.ClearAndPopulate(selector);
                if (axes.Count > 0)
                    AxeDefinitionRegistry.RegisterDefinitions(axes);
            }
        }

        private static class ReflectionAxeBuffer
        {
            private static readonly FieldInfo AllAxesField = typeof(AxeToUse)
                .GetField("allAxes", BindingFlags.Instance | BindingFlags.NonPublic);

            private static readonly List<AxeDefinition> Buffer = new List<AxeDefinition>();

            public static List<AxeDefinition> ClearAndPopulate(AxeToUse selector)
            {
                Buffer.Clear();

                if (selector == null || AllAxesField == null)
                    return Buffer;

                if (AllAxesField.GetValue(selector) is IEnumerable<AxeDefinition> axes)
                {
                    foreach (var axe in axes)
                    {
                        if (axe != null && !Buffer.Contains(axe))
                            Buffer.Add(axe);
                    }
                }

                return Buffer;
            }
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
            areaCandidates.Clear();
            areaCandidateTileCenters.Clear();
            areaAllCandidatesBlocked = false;

            var trees = FindObjectsOfType<TreeNode>();
            float radiusSqr = radius * radius;

            Vector2 controllerPosition2D = (Vector2)transform.position;
            bool observedNonInventoryFailure = false;
            CompanionWoodcuttingCommandResult lastNonInventoryFailure = CompanionWoodcuttingCommandResult.Unreachable;
            int blockedByStuckCount = 0;

            float now = Time.time;
            PruneExpiredBlockedNodes();

            for (int i = 0; i < trees.Length; i++)
            {
                var tree = trees[i];
                if (tree == null || tree.IsDepleted)
                    continue;

                Vector2 rockPosition2D = (Vector2)tree.transform.position;
                if ((rockPosition2D - controllerPosition2D).sqrMagnitude > radiusSqr)
                    continue;

                if (tree.def != null && tree.def.DepletesAfterOneLog && playerProtectedSingleLog.Contains(tree))
                    continue;

                if (IsNodeTemporarilyBlocked(tree, now))
                {
                    blockedByStuckCount++;
                    continue;
                }

                if (!TryPrepareWoodcuttingCommand(tree, out var _, out var validationResult, suppressChat))
                {
                    if (validationResult == CompanionWoodcuttingCommandResult.InventoryFull)
                    {
                        failureReason = CompanionWoodcuttingCommandResult.InventoryFull;
                        return false;
                    }

                    if (validationResult != CompanionWoodcuttingCommandResult.InventoryFull &&
                        validationResult != CompanionWoodcuttingCommandResult.Accepted)
                    {
                        observedNonInventoryFailure = true;
                        lastNonInventoryFailure = validationResult;
                    }

                    continue;
                }

                areaCandidates.Add(tree);
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
                    : CompanionWoodcuttingCommandResult.Unreachable;
                return false;
            }

            failureReason = CompanionWoodcuttingCommandResult.Accepted;
            return true;
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
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionWoodcuttingDialogueLibrary.GetRandomStuckApologyLine());
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
            UnsubscribeFromPlayerWoodcuttingSkill();

            if (player == null)
                return;

            RebindPlayerSkill(player, ref playerWoodcuttingSkill);
            if (playerWoodcuttingSkill == null)
                return;

            playerWoodcuttingSkill.OnStartChopping += OnPlayerStartChopping;
            playerWoodcuttingSkill.OnStopChopping += OnPlayerStopChopping;
        }

        private void UnsubscribeFromPlayerWoodcuttingSkill()
        {
            if (playerWoodcuttingSkill == null)
                return;

            playerWoodcuttingSkill.OnStartChopping -= OnPlayerStartChopping;
            playerWoodcuttingSkill.OnStopChopping -= OnPlayerStopChopping;
            playerWoodcuttingSkill = null;
            playerProtectedSingleLog.Clear();
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
            CancelWoodcutting(true);
            UnsubscribeFromPlayerWoodcuttingSkill();
            blockedNodes.Clear();
            blockedNodePruneBuffer.Clear();
            areaAllCandidatesBlocked = false;
            ResetStuckHistory();
        }

        private void OnDestroy()
        {
            if (woodcuttingSkill != null)
                woodcuttingSkill.OnStopChopping -= HandleWoodcuttingStopped;

            CancelWoodcutting(true);
            UnsubscribeFromPlayerWoodcuttingSkill();
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
