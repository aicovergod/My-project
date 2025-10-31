using System;
using System.Collections;
using System.Collections.Generic;
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

            ConfigureGatheringSkill<FishingSkill>(
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

            int fishingLevel = fishingSkill != null ? fishingSkill.Level : 1;

            return CompanionToolResolver.ResolveBestTool(
                FishingToolDefinitionRegistry.GetAllDefinitions,
                CompanionToolSelectorRegistry.RegisterFishingToolsFromSelectors,
                inventory,
                companionEquipment,
                ref itemCache,
                definition => definition?.Id,
                definition => definition != null && definition.RequiredLevel <= fishingLevel,
                definition =>
                {
                    if (definition == null)
                        return false;

                    if (spotDef.AllowedTools == null || spotDef.AllowedTools.Count == 0)
                        return true;

                    return spotDef.AllowedTools.Contains(definition);
                });
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
            GatheringMovementRoutineResult routineResult = default;

            yield return CompanionGatheringMovementRoutine(new GatheringMovementRoutineParameters
            {
                GetTargetNode = () => spot,
                IsCommandActive = () => fishingActive && currentSpot != null && currentSpot == spot,
                IsNodeValid = node => node != null && !node.IsDepleted,
                GetTargetPosition = node => node.transform.position,
                IsSkillActive = () => fishingSkill != null && fishingSkill.IsFishing,
                StartSkill = node =>
                {
                    if (!fishingActive || currentSpot == null || currentSpot != node || fishingSkill == null)
                        return;

                    fishingSkill.StartFishing(node, tool);
                },
                StopSkill = () =>
                {
                    if (fishingSkill != null)
                        fishingSkill.StopFishing();
                },
                OnProgressStalled = node =>
                {
                    if (CompanionManager.EnableDebugLogging)
                        Debug.Log("[Companion Fishing] Movement stalled while approaching the spot.", this);
                },
                OnGoalUnreachableDetected = node =>
                {
                    if (CompanionManager.EnableDebugLogging)
                        Debug.Log("[Companion Fishing] Navigation reported the spot as unreachable.", this);
                },
                OnGoalUnreachable = HandleFishingStuck,
                OnStuck = HandleFishingStuck,
                OutOfRangeSkillStopMultiplier = 1.2f,
                OnRoutineComplete = result => routineResult = result,
            });

            if (routineResult.Stuck)
                yield break;

            fishingRoutine = null;
            fishingActive = false;

            if (fishingSkill != null && fishingSkill.IsFishing)
                fishingSkill.StopFishing();

            CleanupAfterFishing(true);
            ResetStuckHistory();

            if (areaRoutineActive)
                yield break;

            CancelAreaFishingInternal(false);
        }

        private void HandleFishingStuck(FishableSpot spot)
        {
            ExecuteGatheringStuckRecovery(new GatheringStuckRecoveryParameters
            {
                Node = spot,
                DebugLabel = "Companion Fishing",
                BuildDebugMessage = target =>
                {
                    string spotName = target != null ? target.name : "<null>";
                    return $"[Companion Fishing] Detected a stuck state while targeting {spotName}.";
                },
                ShouldStopSkill = () => fishingSkill != null && fishingSkill.IsFishing,
                SetStopCallbackSuppressed = value => suppressFishingStopCallback = value,
                StopSkill = () =>
                {
                    if (fishingSkill != null)
                        fishingSkill.StopFishing();
                },
                CleanupCallback = () =>
                {
                    CleanupAfterFishing(true);
                    PublishStuckApologyMessage();
                },
                AdditionalStateReset = () =>
                {
                    fishingRoutine = null;
                    fishingActive = false;
                },
                OnThresholdReached = (_, __) =>
                {
                    CancelAreaFishingInternal(true);
                    areaAllCandidatesBlocked = true;
                }
            });
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
            var outcome = BuildAreaCandidates(
                radius,
                retrieveNodes: () => FindObjectsOfType<FishableSpot>(),
                shouldSkipNode: spot =>
                {
                    if (spot == null)
                        return true;

                    return spot.IsBusy || spot == playerActiveSpot;
                },
                tryPrepareCommand: spot =>
                {
                    bool accepted = TryPrepareFishingCommand(spot, out var _, out var validationResult, suppressChat);
                    return (accepted, validationResult);
                },
                acceptedResultFactory: () => CompanionFishingCommandResult.Accepted,
                defaultFailureResultFactory: () => CompanionFishingCommandResult.Unreachable,
                isInventoryFullResult: result => result == CompanionFishingCommandResult.InventoryFull,
                isAcceptedResult: result => result == CompanionFishingCommandResult.Accepted);

            failureReason = outcome.failureReason;
            return outcome.success;
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
            PublishCompanionChatLine(CompanionFishingDialogueLibrary.GetRandomStuckApologyLine);
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
            BindPlayerSkillEvents(
                player,
                ref playerFishingSkill,
                skill =>
                {
                    skill.OnStartFishing += OnPlayerStartFishing;
                    skill.OnStopFishing += OnPlayerStopFishing;
                },
                skill =>
                {
                    skill.OnStartFishing -= OnPlayerStartFishing;
                    skill.OnStopFishing -= OnPlayerStopFishing;
                },
                () => playerActiveSpot = null);
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
            HandleDisable(
                () => CancelFishing(true),
                () => BindToPlayerFishingSkill(null),
                () => playerActiveSpot = null);
        }

        private void OnDestroy()
        {
            HandleDestroy(
                () => CancelFishing(true),
                () => BindToPlayerFishingSkill(null),
                () =>
                {
                    if (fishingSkill != null)
                        fishingSkill.OnStopFishing -= HandleFishingStopped;
                },
                () => playerActiveSpot = null);
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

    }
}
