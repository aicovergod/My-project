using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Inventory;
using Pets;
using Skills;
using Skills.Common;
using Skills.Fishing;
using UI.Chat;
using UnityEngine;
using Companions.Equipment;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions
{
    /// <summary>
    /// Enumerates the possible outcomes when issuing a fishing command to the companion.
    /// </summary>
    public enum CompanionFishingCommandResult
    {
        /// <summary>Command accepted and fishing has started.</summary>
        Accepted,
        /// <summary>Companion backpack cannot hold additional fish.</summary>
        InventoryFull,
        /// <summary>Command rejected because requirements (levels, ownership, etc.) were not met.</summary>
        RequirementsNotMet,
        /// <summary>Command blocked because the player is interacting with the spot.</summary>
        BlockedByPlayer,
        /// <summary>Companion lacks a valid fishing tool.</summary>
        NoTool,
        /// <summary>Companion cannot fish because bait requirements are not satisfied.</summary>
        NoBait,
        /// <summary>Target spot cannot be reached or interacted with.</summary>
        Unreachable,
        /// <summary>Companion is already working on the requested spot.</summary>
        AlreadyFishing,
        /// <summary>Command declined because the companion is observing a cooldown.</summary>
        Declined
    }

    /// <summary>
    /// Handles companion-directed fishing commands by approaching spots, validating requirements,
    /// and delegating the actual fishing routine to <see cref="FishingSkill"/> once in range.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionFishingController : CompanionGatheringControllerBase<FishableSpot, CompanionFishingCommandResult>
    {
        private const float BlockedSpotExpirySeconds = 4f;

        private SkillManager skillManager;
        private RuntimeInventory inventory;
        private CompanionEquipment companionEquipment;
        private FishingSkill fishingSkill;

        private Coroutine fishingRoutine;
        private FishableSpot currentSpot;
        private FishingToolDefinition currentTool;

        private readonly List<FishDefinition> eligibleFishBuffer = new List<FishDefinition>();
        private Dictionary<string, ItemData> itemCache;

        private bool fishingActive;
        private bool suppressFishingStopCallback;

        private Transform playerTransform;
        private FishingSkill playerFishingSkill;
        private FishableSpot playerActiveSpot;

        /// <summary>
        /// True while the fishing controller has an active routine or the underlying skill reports fishing activity.
        /// Allows external systems to determine whether "stop" commands should be offered.
        /// </summary>
        public bool IsFishing => fishingActive || (fishingSkill != null && fishingSkill.IsFishing);

        /// <summary>
        /// Initialises the fishing controller with the owning companion components.
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
                "Companion Fishing",
                skill =>
                {
                    fishingSkill = skill;
                    fishingSkill.OnStopFishing -= HandleFishingStopped;
                    fishingSkill.OnStopFishing += HandleFishingStopped;
                    fishingSkill.ConfigureCompanionChat(CompanionManager.GetCompanionDisplayName);
                },
                () =>
                {
                    fishingActive = false;
                    suppressFishingStopCallback = false;
                    itemCache = new Dictionary<string, ItemData>();
                },
                out resolvedInventory,
                out resolvedEquipment);

            inventory = resolvedInventory;
            companionEquipment = resolvedEquipment;

            RebindPlayer(player);
        }

        /// <summary>
        /// Rebinds the controller to a new player transform so navigation and player fishing hooks stay in sync.
        /// </summary>
        /// <param name="player">Player transform to follow.</param>
        public void RebindPlayer(Transform player)
        {
            playerTransform = player;
            BindToPlayerFishingSkill(playerTransform);
        }

        /// <summary>
        /// Attempts to command the companion to fish the supplied spot.
        /// </summary>
        /// <param name="spot">Spot that should be fished.</param>
        /// <returns>True when the command was accepted, otherwise false.</returns>
        public bool TryCommandFish(FishableSpot spot)
        {
            return TryCommandAllowingInventoryFull(spot);
        }

        /// <summary>
        /// Attempts to command the companion to fish the supplied spot while optionally preserving follower holds.
        /// </summary>
        /// <param name="spot">Spot that should be fished.</param>
        /// <param name="preserveFollowerHold">
        /// When <c>true</c>, existing follower holds remain intact during the hand-off so
        /// automation can maintain control of the follower state.
        /// </param>
        /// <returns>True when the command was accepted, otherwise false.</returns>
        public bool TryCommandFish(FishableSpot spot, bool preserveFollowerHold)
        {
            return TryCommandAllowingInventoryFull(spot, preserveFollowerHold);
        }

        /// <summary>
        /// Attempts to command the companion to fish the supplied spot while reporting the outcome.
        /// </summary>
        /// <param name="spot">Spot that should be fished.</param>
        /// <param name="result">Detailed outcome describing why the command failed when <c>false</c> is returned.</param>
        /// <returns>True when the command was accepted, otherwise false.</returns>
        public bool TryCommandFish(FishableSpot spot, out CompanionFishingCommandResult result)
        {
            return TryCommandWithResult(spot, out result);
        }

        /// <summary>
        /// Attempts to command the companion to fish the supplied spot while reporting the outcome and
        /// optionally preserving follower holds.
        /// </summary>
        private bool TryCommandFish(
            FishableSpot spot,
            out CompanionFishingCommandResult result,
            bool preserveFollowerHold)
        {
            return TryCommandWithResult(spot, out result, preserveFollowerHold);
        }

        /// <inheritdoc />
        protected override CommandAttempt PerformGatheringCommand(FishableSpot spot, bool preserveFollowerHold)
        {
            var attempt = new CommandAttempt
            {
                Accepted = false,
                Result = CompanionFishingCommandResult.RequirementsNotMet
            };

            if (!isActiveAndEnabled)
                return attempt;

            if (CompanionSkillCooldownTimers.ShouldDeclineFishingRequest(skillCooldownTracker, out var cooldownResult))
            {
                attempt.Result = cooldownResult;
                return attempt;
            }

            if (!TryPrepareFishingCommand(spot, out var tool, out var validationResult))
            {
                attempt.Result = validationResult;
                return attempt;
            }

            followerDisabledForGathering = preserveFollowerHold ? HasActiveFollowerHold : false;

            BeginFishing(spot, tool);
            CompanionSkillCooldownTimers.ClearFishingCooldown(skillCooldownTracker);

            attempt.Accepted = true;
            attempt.Result = CompanionFishingCommandResult.Accepted;
            return attempt;
        }

        /// <inheritdoc />
        protected override bool ShouldTreatInventoryFullAsSuccess(CompanionFishingCommandResult result)
        {
            return result == CompanionFishingCommandResult.InventoryFull;
        }

        /// <inheritdoc />
        protected override bool IsNodeDepleted(FishableSpot node)
        {
            return node == null || node.IsDepleted;
        }

        /// <summary>
        /// Initiates an area fishing routine that scans nearby spots and works through them sequentially.
        /// </summary>
        /// <param name="radius">Scan radius in Unity units (tiles).</param>
        /// <param name="failureReason">Detailed reason describing why the command failed when <c>false</c> is returned.</param>
        /// <returns>True when area fishing started successfully.</returns>
        public bool TryStartAreaFishing(float radius, out CompanionFishingCommandResult failureReason)
        {
            failureReason = CompanionFishingCommandResult.RequirementsNotMet;

            if (!isActiveAndEnabled || fishingSkill == null || skillManager == null)
                return false;

            if (CompanionSkillCooldownTimers.ShouldDeclineFishingRequest(skillCooldownTracker, out failureReason))
                return false;

            bool started = TryStartAreaGathering(
                radius,
                out failureReason,
                CompanionFishingCommandResult.Accepted,
                clampedRadius =>
                {
                    bool success = BuildAreaCandidateList(clampedRadius, out var buildFailure);
                    return (success, buildFailure);
                },
                PublishAreaFishingFailureMessage,
                AreaFishingRoutine,
                () => CompanionSkillCooldownTimers.ClearFishingCooldown(skillCooldownTracker),
                "Companion Fishing");

            return started;
        }

        /// <summary>
        /// Stops the active fishing routine and optionally restores the follower component.
        /// </summary>
        /// <param name="restoreFollower">Whether the companion follower should be re-enabled.</param>
        public void CancelFishing(bool restoreFollower)
        {
            CancelAreaFishingInternal(false);
            StopActiveFishingRoutine();
            CleanupAfterFishing(restoreFollower);
            UnsubscribeFromPlayerFishingSkill();
            BindToPlayerFishingSkill(playerTransform);
            ResetStuckHistory();
        }

        /// <summary>
        /// Cancels the running area fishing routine and optionally restores the follower.
        /// </summary>
        /// <param name="restoreFollower">True when the follower should resume immediately.</param>
        public void CancelAreaFishing(bool restoreFollower)
        {
            CancelAreaFishingInternal(restoreFollower);
            UnsubscribeFromPlayerFishingSkill();
            BindToPlayerFishingSkill(playerTransform);
            ResetStuckHistory();
        }

        private bool TryPrepareFishingCommand(
            FishableSpot spot,
            out FishingToolDefinition tool,
            out CompanionFishingCommandResult result,
            bool suppressChat = false)
        {
            tool = null;
            result = CompanionFishingCommandResult.RequirementsNotMet;

            if (spot == null)
            {
                result = CompanionFishingCommandResult.Unreachable;
                return false;
            }

            if (spot.IsDepleted)
            {
                result = CompanionFishingCommandResult.Unreachable;
                return false;
            }

            if (spot.IsBusy || spot == playerActiveSpot)
            {
                if (!suppressChat)
                    PublishBlockedByPlayerMessage();
                result = CompanionFishingCommandResult.BlockedByPlayer;
                return false;
            }

            float now = Time.time;
            PruneExpiredBlockedNodes();

            if (IsNodeTemporarilyBlocked(spot, now))
            {
                result = CompanionFishingCommandResult.Unreachable;
                return false;
            }

            if (fishingSkill == null || skillManager == null || !isActiveAndEnabled)
            {
                result = CompanionFishingCommandResult.RequirementsNotMet;
                return false;
            }

            if (fishingActive && currentSpot == spot)
            {
                result = CompanionFishingCommandResult.AlreadyFishing;
                return false;
            }

            var spotDef = spot.def;
            if (spotDef == null)
            {
                result = CompanionFishingCommandResult.Unreachable;
                return false;
            }

            eligibleFishBuffer.Clear();
            int minimumRequiredLevel = int.MaxValue;
            int fishingLevel = fishingSkill.Level;

            if (spotDef.AvailableFish != null)
            {
                for (int i = 0; i < spotDef.AvailableFish.Count; i++)
                {
                    var fish = spotDef.AvailableFish[i];
                    if (fish == null)
                        continue;

                    minimumRequiredLevel = Mathf.Min(minimumRequiredLevel, fish.RequiredLevel);
                    if (fishingLevel >= fish.RequiredLevel)
                        eligibleFishBuffer.Add(fish);
                }
            }

            if (eligibleFishBuffer.Count == 0)
            {
                if (!suppressChat)
                {
                    var chat = ChatService.Instance;
                    if (chat != null)
                    {
                        string message = minimumRequiredLevel == int.MaxValue
                            ? CompanionFishingDialogueLibrary.GetRandomNoSpotsLine()
                            : CompanionFishingDialogueLibrary.GetLevelRequirementLine(minimumRequiredLevel);
                        chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
                    }
                }

                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Fishing] Command blocked by fishing level requirement.", this);

                result = CompanionFishingCommandResult.RequirementsNotMet;
                return false;
            }

            tool = ResolveFishingTool(spotDef);
            if (tool == null)
            {
                if (!suppressChat)
                    PublishMissingToolMessage();
                result = CompanionFishingCommandResult.NoTool;
                return false;
            }

            if (fishingLevel < tool.RequiredLevel)
            {
                if (!suppressChat)
                {
                    var chat = ChatService.Instance;
                    if (chat != null)
                    {
                        string message = CompanionFishingDialogueLibrary.GetToolLevelRequirementLine(tool.RequiredLevel, tool.DisplayName);
                        chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
                    }
                }

                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Fishing] Command blocked by tool level requirement.", this);

                result = CompanionFishingCommandResult.RequirementsNotMet;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(spotDef.BaitItemId))
            {
                bool hasBait = inventory != null && inventory.HasItem(spotDef.BaitItemId);
                if (!hasBait)
                {
                    if (!suppressChat)
                        PublishMissingBaitMessage();
                    result = CompanionFishingCommandResult.NoBait;
                    return false;
                }
            }

            if (!HasInventoryCapacityForFishInternal(eligibleFishBuffer, suppressChat))
            {
                result = CompanionFishingCommandResult.InventoryFull;
                return false;
            }

            result = CompanionFishingCommandResult.Accepted;
            return true;
        }

        private FishingToolDefinition ResolveFishingTool(FishingSpotDefinition spotDef)
        {
            if (spotDef == null)
                return null;

            var definitions = FishingToolDefinitionRegistry.GetAllDefinitions();
            if (definitions == null || definitions.Count == 0)
            {
                RegisterToolsFromSelectors();
                definitions = FishingToolDefinitionRegistry.GetAllDefinitions();
            }

            if (definitions == null || definitions.Count == 0)
                return null;

            int fishingLevel = fishingSkill != null ? fishingSkill.Level : 1;

            foreach (var definition in definitions)
            {
                if (definition == null)
                    continue;

                if (spotDef.AllowedTools != null && spotDef.AllowedTools.Count > 0 && !spotDef.AllowedTools.Contains(definition))
                    continue;

                if (definition.RequiredLevel > fishingLevel)
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

        private void RegisterToolsFromSelectors()
        {
            var selectors = FindObjectsOfType<FishingToolToUse>(true);
            if (selectors == null || selectors.Length == 0)
                return;

            for (int i = 0; i < selectors.Length; i++)
            {
                var selector = selectors[i];
                if (selector == null)
                    continue;

                var tools = ReflectionToolBuffer.ClearAndPopulate(selector);
                if (tools.Count > 0)
                    FishingToolDefinitionRegistry.RegisterDefinitions(tools);
            }
        }

        private bool HasInventoryCapacityForFishInternal(IReadOnlyList<FishDefinition> fishOptions, bool suppressChat)
        {
            if (fishOptions == null || fishOptions.Count == 0 || fishingSkill == null)
                return true;

            for (int i = 0; i < fishOptions.Count; i++)
            {
                var fish = fishOptions[i];
                if (fish != null && fishingSkill.CanAddFish(fish))
                    return true;
            }

            if (!suppressChat)
                PublishInventoryFullMessage();

            if (CompanionManager.EnableDebugLogging)
                Debug.Log("[Companion Fishing] Command rejected because the companion inventory is full.", this);

            return false;
        }

        private void BeginFishing(FishableSpot spot, FishingToolDefinition tool)
        {
            StopActiveFishingRoutine();

            currentSpot = spot;
            currentTool = tool;
            fishingActive = true;
            fishingRoutine = StartCoroutine(FishingRoutine(spot, tool));

            if (CompanionManager.EnableDebugLogging)
            {
                string toolName = tool != null ? tool.DisplayName : "<unknown tool>";
                Debug.Log($"[Companion Fishing] Command accepted for {spot.name} using {toolName}.", this);
            }
        }

        private void StopActiveFishingRoutine()
        {
            if (fishingRoutine != null)
            {
                StopCoroutine(fishingRoutine);
                fishingRoutine = null;
            }

            if (fishingSkill != null && fishingSkill.IsFishing)
            {
                suppressFishingStopCallback = true;
                fishingSkill.StopFishing();
                suppressFishingStopCallback = false;
            }

            currentSpot = null;
            currentTool = null;
            fishingActive = false;
        }

        private IEnumerator FishingRoutine(FishableSpot spot, FishingToolDefinition tool)
        {
            var followerHold = EnterTemporaryFollowerHold();
            bool stuckTriggered = false;
            FishableSpot stuckSpot = null;
            float noProgressTimer = 0f;
            float lastRecordedDistance = 0f;
            float cumulativeDistanceClosed = 0f;
            bool hasDistanceSample = false;

            try
            {
                pathMover?.ResetAttackTracking();

                while (spot != null && !spot.IsDepleted)
                {
                    if (!isActiveAndEnabled)
                        break;

                    if (!fishingActive || currentSpot == null || currentSpot != spot)
                        break;

                    Vector3 targetPosition = spot.transform.position;
                    float distance = Vector2.Distance(transform.position, targetPosition);

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
                        bool activelyFishing = fishingSkill != null && fishingSkill.IsFishing;

                        if (closedGap || effectivelyClose || activelyFishing)
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
                            Debug.Log("[Companion Fishing] Movement stalled while approaching the spot.", this);

                        stuckTriggered = true;
                        stuckSpot = spot;
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
                                float teleportDetectionDistance = float.PositiveInfinity;

                                navigationStepTaken = pathMover.TryStepAttack(
                                    deltaTime,
                                    moveSpeed,
                                    GatheringRange,
                                    WaypointTolerance,
                                    () => spot != null ? (Vector2)spot.transform.position : (Vector2)transform.position,
                                    ReplanDistance,
                                    teleportDetectionDistance,
                                    out nextPosition,
                                    out navVelocity,
                                    out teleported,
                                    out goalUnreachable);

                                if (goalUnreachable)
                                {
                                    if (CompanionManager.EnableDebugLogging)
                                        Debug.Log("[Companion Fishing] Navigation reported the spot as unreachable.", this);
                                    stuckTriggered = true;
                                    stuckSpot = spot;
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
                                Vector3 nextPosition = Vector3.MoveTowards(startPosition, targetPosition, moveSpeed * deltaTime);
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

                        if (fishingSkill.IsFishing && distance > GatheringRange * 1.2f)
                            fishingSkill.StopFishing();
                    }
                    else
                    {
                        if (body != null)
                            body.linearVelocity = Vector2.zero;

                        if (!fishingActive || currentSpot == null || currentSpot != spot)
                            break;

                        if (!fishingSkill.IsFishing)
                        {
                            if (!fishingActive || currentSpot == null || currentSpot != spot)
                                break;

                            fishingSkill.StartFishing(spot, tool);
                        }

                        if (!fishingActive || currentSpot == null || currentSpot != spot)
                            break;

                        if (!fishingSkill.IsFishing)
                            break;
                    }

                    if (spot == null || spot.IsDepleted)
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
                HandleFishingStuck(stuckSpot);
                yield break;
            }

            fishingRoutine = null;
            fishingActive = false;

            if (fishingSkill != null && fishingSkill.IsFishing)
                fishingSkill.StopFishing();

            CleanupAfterFishing(true);
            ResetStuckHistory();

            if (areaRoutineActive)
            {
                yield break;
            }

            CancelAreaFishingInternal(false);
        }

        private void HandleFishingStuck(FishableSpot spot)
        {
            if (CompanionManager.EnableDebugLogging)
            {
                string spotName = spot != null ? spot.name : "<null>";
                Debug.Log($"[Companion Fishing] Detected a stuck state while targeting {spotName}.", this);
            }

            float now = Time.time;
            if (spot != null)
                MarkNodeBlocked(spot, now + BlockedSpotExpirySeconds);

            if (fishingSkill != null && fishingSkill.IsFishing)
                fishingSkill.StopFishing();

            CleanupAfterFishing(true);
            PublishStuckApologyMessage();

            if (lastStuckNode == spot)
            {
                consecutiveStuckNodeCount++;
                if (consecutiveStuckNodeCount >= ConsecutiveStuckCancelThreshold)
                {
                    CancelAreaFishingInternal(true);
                    areaAllCandidatesBlocked = true;
                }
            }
            else
            {
                lastStuckNode = spot;
                consecutiveStuckNodeCount = 1;
            }
        }

        private void ResetStuckHistory()
        {
            ResetStuckHistoryInternal();
        }

        private IEnumerator AreaFishingRoutine()
        {
            while (areaCandidates.Count > 0)
            {
                bool attemptedCommand = false;

                for (int i = 0; i < areaCandidates.Count; i++)
                {
                    var spot = areaCandidates[i];
                    if (spot == null || spot.IsDepleted)
                    {
                        RemoveAreaCandidateAt(i--);
                        continue;
                    }

                    if (spot.IsBusy || spot == playerActiveSpot)
                        continue;

                    if (IsNodeTemporarilyBlocked(spot, Time.time))
                        continue;

                    attemptedCommand = true;

                    if (TryCommandFish(spot, out var result, preserveFollowerHold: true))
                    {
                        while (fishingActive && currentSpot == spot)
                            yield return null;

                        if (fishingSkill != null && fishingSkill.IsFishing)
                            fishingSkill.StopFishing();
                    }
                    else
                    {
                        if (result == CompanionFishingCommandResult.InventoryFull)
                        {
                            PublishInventoryFullMessage();
                            CancelAreaFishingInternal(true);
                            yield break;
                        }

                        if (result == CompanionFishingCommandResult.BlockedByPlayer)
                            MarkNodeBlocked(spot, Time.time + BlockedSpotExpirySeconds);

                        if (result != CompanionFishingCommandResult.Declined && result != CompanionFishingCommandResult.Accepted)
                            MarkNodeBlocked(spot, Time.time + BlockedSpotExpirySeconds);
                    }

                    yield return null;
                }

                if (!attemptedCommand)
                    break;

                yield return null;
            }

            CancelAreaFishingInternal(true);
        }

        private bool BuildAreaCandidateList(float radius, out CompanionFishingCommandResult failureReason, bool suppressChat = true)
        {
            areaCandidates.Clear();
            areaCandidateTileCenters.Clear();
            areaAllCandidatesBlocked = false;

            var allSpots = FindObjectsOfType<FishableSpot>();
            if (allSpots == null || allSpots.Length == 0)
            {
                failureReason = CompanionFishingCommandResult.Unreachable;
                return false;
            }

            Vector3 origin = transform.position;
            float radiusSqr = radius * radius;
            bool anyReachable = false;
            CompanionFishingCommandResult lastNonInventoryFailure = CompanionFishingCommandResult.Unreachable;

            for (int i = 0; i < allSpots.Length; i++)
            {
                var spot = allSpots[i];
                if (spot == null || spot.IsDepleted)
                    continue;

                if (spot.IsBusy || spot == playerActiveSpot)
                    continue;

                float distanceSqr = (spot.transform.position - origin).sqrMagnitude;
                if (distanceSqr > radiusSqr)
                    continue;

                if (!TryPrepareFishingCommand(spot, out var _, out var validationResult, suppressChat))
                {
                    if (validationResult == CompanionFishingCommandResult.InventoryFull)
                    {
                        failureReason = CompanionFishingCommandResult.InventoryFull;
                        return false;
                    }

                    if (validationResult != CompanionFishingCommandResult.Accepted)
                        lastNonInventoryFailure = validationResult;

                    continue;
                }

                anyReachable = true;
                areaCandidates.Add(spot);
                areaCandidateTileCenters.Add(GetTileCentre(spot.transform.position));
            }

            if (!anyReachable)
            {
                failureReason = lastNonInventoryFailure;
                return false;
            }

            failureReason = CompanionFishingCommandResult.Accepted;
            return true;
        }

        private void PublishAreaFishingFailureMessage(CompanionFishingCommandResult failureReason)
        {
            switch (failureReason)
            {
                case CompanionFishingCommandResult.InventoryFull:
                    PublishInventoryFullMessage();
                    break;
                case CompanionFishingCommandResult.NoTool:
                    PublishMissingToolMessage();
                    break;
                case CompanionFishingCommandResult.BlockedByPlayer:
                    PublishBlockedByPlayerMessage();
                    break;
                case CompanionFishingCommandResult.NoBait:
                    PublishMissingBaitMessage();
                    break;
                case CompanionFishingCommandResult.Declined:
                    break;
                default:
                    PublishNoSpotsMessage();
                    break;
            }
        }

        private void CancelAreaFishingInternal(bool restoreFollower)
        {
            CancelAreaInternal(restoreFollower, true);
        }

        private void PublishInventoryFullMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionFishingDialogueLibrary.GetRandomInventoryFullLine());
        }

        private void PublishMissingToolMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionFishingDialogueLibrary.GetRandomMissingToolLine());
        }

        private void PublishMissingBaitMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionFishingDialogueLibrary.GetRandomMissingBaitLine());
        }

        private void PublishNoSpotsMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionFishingDialogueLibrary.GetRandomNoSpotsLine());
        }

        private void PublishBlockedByPlayerMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionFishingDialogueLibrary.GetRandomPlayerBusyLine());
        }

        private void PublishStuckApologyMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.PublishCompanionMessage(
                CompanionManager.GetCompanionDisplayName(),
                CompanionFishingDialogueLibrary.GetRandomStuckApologyLine());
        }

        private void CleanupAfterFishing(bool restoreFollower, bool preserveFollowerLocks = false)
        {
            CleanupFollowerAfterGathering(restoreFollower, preserveFollowerLocks);

            if (body != null)
                body.linearVelocity = Vector2.zero;

            pathMover?.ResetAttackTracking();

            currentSpot = null;
            currentTool = null;
            fishingActive = false;
        }

        private void HandleFishingStopped()
        {
            if (suppressFishingStopCallback)
                return;

            fishingActive = false;
            CleanupAfterFishing(true);
            ResetStuckHistory();
        }

        private void BindToPlayerFishingSkill(Transform player)
        {
            UnsubscribeFromPlayerFishingSkill();

            if (player == null)
                return;

            RebindPlayerSkill(player, ref playerFishingSkill);
            if (playerFishingSkill == null)
                return;

            playerFishingSkill.OnStartFishing += OnPlayerStartFishing;
            playerFishingSkill.OnStopFishing += OnPlayerStopFishing;
        }

        private void UnsubscribeFromPlayerFishingSkill()
        {
            if (playerFishingSkill == null)
                return;

            playerFishingSkill.OnStartFishing -= OnPlayerStartFishing;
            playerFishingSkill.OnStopFishing -= OnPlayerStopFishing;
            playerFishingSkill = null;
            playerActiveSpot = null;
        }

        private void OnPlayerStartFishing(FishableSpot spot)
        {
            playerActiveSpot = spot;

            if (fishingActive && currentSpot == spot)
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log("[Companion Fishing] Player started fishing the same spot. Cancelling companion fishing.", this);

                StopActiveFishingRoutine();
                CleanupAfterFishing(true);
            }
        }

        private void OnPlayerStopFishing()
        {
            playerActiveSpot = null;
        }

        private void OnDisable()
        {
            CancelFishing(true);
            UnsubscribeFromPlayerFishingSkill();
            blockedNodes.Clear();
            blockedNodePruneBuffer.Clear();
            areaAllCandidatesBlocked = false;
            ResetStuckHistory();
        }

        private void OnDestroy()
        {
            if (fishingSkill != null)
                fishingSkill.OnStopFishing -= HandleFishingStopped;

            CancelFishing(true);
            UnsubscribeFromPlayerFishingSkill();
        }

        private void OnDrawGizmosSelected()
        {
            if (!areaRoutineActive || activeAreaRadius <= 0f)
                return;

            Gizmos.color = new Color(0.2f, 0.8f, 0.9f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, activeAreaRadius);

            Gizmos.color = new Color(0.2f, 0.9f, 0.2f, 0.6f);
            for (int i = 0; i < areaCandidateTileCenters.Count; i++)
            {
                Vector3 center = areaCandidateTileCenters[i];
                Gizmos.DrawWireCube(center, new Vector3(1f, 1f, 0f));
            }
        }

        private static class ReflectionToolBuffer
        {
            private static readonly FieldInfo AllToolsField = typeof(FishingToolToUse)
                .GetField("allTools", BindingFlags.Instance | BindingFlags.NonPublic);

            private static readonly List<FishingToolDefinition> Buffer = new List<FishingToolDefinition>();

            public static List<FishingToolDefinition> ClearAndPopulate(FishingToolToUse selector)
            {
                Buffer.Clear();

                if (selector == null || AllToolsField == null)
                    return Buffer;

                if (AllToolsField.GetValue(selector) is IEnumerable<FishingToolDefinition> tools)
                {
                    foreach (var tool in tools)
                    {
                        if (tool != null && !Buffer.Contains(tool))
                            Buffer.Add(tool);
                    }
                }

                return Buffer;
            }
        }
    }
}
