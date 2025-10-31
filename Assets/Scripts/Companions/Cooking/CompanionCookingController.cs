using System;
using System.Collections;
using System.Collections.Generic;
using Inventory;
using Pets;
using Skills;
using Skills.Cooking;
using Skills.Common;
using UI.Chat;
using UnityEngine;
using Util;
using Companions.Equipment;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions
{
    /// <summary>
    /// Possible outcomes when routing a cooking request through the companion controller.
    /// </summary>
    public enum CompanionCookingCommandResult
    {
        Accepted,
        InventoryFull,
        MissingIngredients,
        MissingTool,
        RequirementsNotMet,
        PlayerBusy,
        StationUnavailable,
        StationOccupied,
        AlreadyCooking,
        Declined,
        Unreachable
    }

    /// <summary>
    /// Drives companion-directed cooking interactions by steering the follower toward stations, validating
    /// inventory requirements, and delegating the actual skill execution to <see cref="CookingSkill"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionCookingController : CompanionSkillingControllerBase
    {
        private const float ApproachRange = 1.25f;
        private const float ReplanDistance = ApproachRange * 0.75f;
        private const float WaypointTolerance = 0.1f;
        [Header("Movement")]
        [SerializeField, Tooltip("Grace period before pathing is considered stuck.")]
        private float stuckTimeoutSeconds = 2.5f;

        [SerializeField, Tooltip("Optional list of item ids that count as valid cooking tools.")]
        private List<string> validCookingToolIds = new List<string>();

        private CompanionController ownerController;
        private SkillManager skillManager;
        private RuntimeInventory companionInventory;
        private RuntimeInventory playerInventory;
        private CompanionEquipment companionEquipment;
        private CookingSkill cookingSkill;
        private CompanionSkillCooldownTracker cooldownTracker;

        private Coroutine cookingRoutine;
        private CookingObject currentStation;
        private CookableRecipe currentRecipe;

        private bool followerDisabled;
        private bool suppressStopCallback;
        private bool stuckTriggered;

        private bool hasPublishedOutcomeChat;

        private Transform playerTransform;
        private CookingSkill playerCookingSkill;

        private readonly List<CookingObject> areaStations = new List<CookingObject>();
        private readonly Dictionary<string, ItemData> itemCache = new Dictionary<string, ItemData>();

        /// <summary>
        /// Indicates whether the controller currently has the companion performing an active cooking session.
        /// </summary>
        public bool IsCooking => cookingSkill != null && cookingSkill.IsCooking;

        /// <summary>
        /// Exposes whether the cooking controller is currently holding the follower disabled.
        /// Other systems can consult this to avoid re-enabling the follower prematurely.
        /// </summary>
        public bool HasActiveFollowerHold => followerDisableLockCount > 0;

        /// <summary>
        /// Initialises the controller by binding all required components. Invoked by <see cref="CompanionController"/>.
        /// </summary>
        public void Initialise(
            CompanionController controller,
            SkillManager skills,
            CompanionInventory inventoryWrapper,
            CompanionEquipment equipment,
            Transform player,
            CompanionSkillCooldownTracker tracker)
        {
            ownerController = controller;
            skillManager = skills;
            companionInventory = inventoryWrapper != null ? inventoryWrapper.InventoryComponent : null;
            companionEquipment = equipment;
            cooldownTracker = tracker;
            playerInventory = CompanionManager.GetPlayerInventory();

            cookingSkill = GetComponent<CookingSkill>();
            if (cookingSkill == null)
                cookingSkill = gameObject.AddComponent<CookingSkill>();

            if (cookingSkill != null)
            {
                cookingSkill.OnStopCooking -= HandleCookingStopped;
                cookingSkill.OnStopCooking += HandleCookingStopped;
                cookingSkill.OnStartCooking -= HandleCookingStarted;
                cookingSkill.OnStartCooking += HandleCookingStarted;
                cookingSkill.OnFoodCooked -= HandleFoodCooked;
                cookingSkill.OnFoodCooked += HandleFoodCooked;
                cookingSkill.OnLevelUp -= HandleCookingLevelUp;
                cookingSkill.OnLevelUp += HandleCookingLevelUp;
            }
            else if (CompanionManager.EnableDebugLogging)
            {
                Debug.LogError("[Companion Cooking] Failed to resolve CookingSkill component.", this);
            }

            InitialiseMovementComponents();
            ResetFollowerState();

            followerDisabled = false;
            stuckTriggered = false;

            RebindPlayer(player);
        }

        /// <summary>
        /// Rebinds the controller to a newly spawned player transform so distance checks stay accurate.
        /// </summary>
        public void RebindPlayer(Transform player)
        {
            playerTransform = player;
            RebindPlayerSkill(playerTransform, ref playerCookingSkill);

            playerInventory = CompanionManager.GetPlayerInventory();
        }

        /// <summary>
        /// Attempts to command the companion to cook using the supplied station and optional recipe.
        /// When the recipe is null the controller scans both inventories for the best candidate.
        /// </summary>
        public bool TryCommandCook(CookingObject station, CookableRecipe explicitRecipe, out CompanionCookingCommandResult result)
        {
            result = CompanionCookingCommandResult.RequirementsNotMet;

            hasPublishedOutcomeChat = false;

            if (CompanionSkillCooldownTimers.ShouldDeclineCookingRequest(cooldownTracker, out result))
            {
                NotifyCooldownFromTracker();
                return false;
            }

            if (!IsReadyForCommands(out result))
                return false;

            if (station == null)
            {
                result = CompanionCookingCommandResult.StationUnavailable;
                PublishStationUnavailableMessage();
                return false;
            }

            var recipe = explicitRecipe;
            RuntimeInventory sourceInventory;
            ItemData rawItem;
            int availableQuantity;
            bool usingPlayerInventory;
            string failureMessage;

            if (recipe == null)
            {
                recipe = ResolveRecipeFromInventories(
                    out sourceInventory,
                    out rawItem,
                    out availableQuantity,
                    out usingPlayerInventory,
                    out failureMessage);
            }
            else
            {
                recipe = ValidateRecipe(recipe, out sourceInventory, out rawItem, out availableQuantity, out usingPlayerInventory, out failureMessage);
            }

            if (recipe == null)
            {
                result = MapInventoryFailureToCommandResult(failureMessage);
                if (result == CompanionCookingCommandResult.InventoryFull)
                    PublishInventoryFullMessage();
                else
                    PublishMissingIngredientMessage();
                return false;
            }

            if (!HasRequiredCookingLevel(recipe))
            {
                result = CompanionCookingCommandResult.RequirementsNotMet;
                PublishLevelRequirementMessage(recipe.requiredLevel);
                return false;
            }

            if (!HasCookingTool())
            {
                result = CompanionCookingCommandResult.MissingTool;
                PublishMissingToolMessage();
                return false;
            }

            if (playerCookingSkill != null && playerCookingSkill.ActiveCookingObject == station)
            {
                result = CompanionCookingCommandResult.PlayerBusy;
                PublishPlayerBusyMessage();
                return false;
            }

            if (IsCooking && cookingSkill.ActiveCookingObject == station)
            {
                result = CompanionCookingCommandResult.AlreadyCooking;
                return true;
            }

            StartCookingRoutine(station, recipe, sourceInventory, rawItem, availableQuantity, usingPlayerInventory);
            result = CompanionCookingCommandResult.Accepted;
            return true;
        }

        /// <summary>
        /// Attempts to locate a nearby station within <paramref name="radius"/> and begin cooking automatically.
        /// </summary>
        public bool TryStartAreaCooking(float radius, out CompanionCookingCommandResult failureReason)
        {
            failureReason = CompanionCookingCommandResult.StationUnavailable;

            hasPublishedOutcomeChat = false;

            if (CompanionSkillCooldownTimers.ShouldDeclineCookingRequest(cooldownTracker, out failureReason))
            {
                NotifyCooldownFromTracker();
                return false;
            }

            if (!IsReadyForCommands(out failureReason))
                return false;

            var recipe = ResolveRecipeFromInventories(
                out var inventory,
                out var rawItem,
                out var available,
                out var usingPlayerInventory,
                out var failureMessage);

            if (recipe == null)
            {
                failureReason = MapInventoryFailureToCommandResult(failureMessage);
                if (failureReason == CompanionCookingCommandResult.InventoryFull)
                    PublishInventoryFullMessage();
                else
                    PublishMissingIngredientMessage();
                return false;
            }

            if (!HasCookingTool())
            {
                failureReason = CompanionCookingCommandResult.MissingTool;
                PublishMissingToolMessage();
                return false;
            }

            var station = FindBestStation(radius);
            if (station == null)
            {
                failureReason = CompanionCookingCommandResult.StationUnavailable;
                PublishStationUnavailableMessage();
                return false;
            }

            if (playerCookingSkill != null && playerCookingSkill.ActiveCookingObject == station)
            {
                failureReason = CompanionCookingCommandResult.PlayerBusy;
                PublishPlayerBusyMessage();
                return false;
            }

            StartCookingRoutine(station, recipe, inventory, rawItem, available, usingPlayerInventory);
            failureReason = CompanionCookingCommandResult.Accepted;
            return true;
        }

        /// <summary>
        /// Cancels the active cooking routine if one is running.
        /// </summary>
        public void CancelCooking(bool publishMessage)
        {
            if (!IsCooking && cookingRoutine == null)
                return;

            if (cookingRoutine != null)
            {
                StopCoroutine(cookingRoutine);
                cookingRoutine = null;
            }

            suppressStopCallback = true;
            cookingSkill?.StopCooking();
            ResetMovementState();
            EnableFollower();

            if (publishMessage)
                PublishStuckMessage();
        }

        private bool IsReadyForCommands(out CompanionCookingCommandResult failure)
        {
            failure = CompanionCookingCommandResult.RequirementsNotMet;

            if (ownerController == null || !CompanionManager.HasActiveCompanion)
            {
                failure = CompanionCookingCommandResult.RequirementsNotMet;
                return false;
            }

            if (cookingSkill == null)
            {
                failure = CompanionCookingCommandResult.RequirementsNotMet;
                return false;
            }

            if (ownerController.IsInCombat)
            {
                failure = CompanionCookingCommandResult.Declined;
                return false;
            }

            return true;
        }

        private void StartCookingRoutine(
            CookingObject station,
            CookableRecipe recipe,
            RuntimeInventory sourceInventory,
            ItemData rawItem,
            int availableQuantity,
            bool usingPlayerInventory)
        {
            if (cookingRoutine != null)
            {
                StopCoroutine(cookingRoutine);
                cookingRoutine = null;
            }

            currentStation = station;
            currentRecipe = recipe;
            stuckTriggered = false;

            cookingRoutine = StartCoroutine(CookRoutine(station, recipe, sourceInventory, rawItem, availableQuantity, usingPlayerInventory));
        }

        private IEnumerator CookRoutine(
            CookingObject station,
            CookableRecipe recipe,
            RuntimeInventory sourceInventory,
            ItemData rawItem,
            int availableQuantity,
            bool usingPlayerInventory)
        {
            DisableFollower();
            Vector3 stationPosition = station != null ? station.ApproachAnchor.position : transform.position;
            float stopDistance = Mathf.Max(0.1f, ApproachRange * 0.8f);

            float lastProgressSample = Time.unscaledTime;
            Vector3 lastProgressPosition = transform.position;

            while (enabled && station != null)
            {
                Vector3 currentPosition = transform.position;
                float distance = Vector2.Distance(currentPosition, stationPosition);
                if (distance <= stopDistance)
                    break;

                bool usedNavigation = TryMoveWithNavigation(stationPosition, stopDistance);
                if (!usedNavigation)
                    MoveDirectlyTowards(stationPosition);

                if (Vector3.SqrMagnitude(transform.position - lastProgressPosition) > 0.01f)
                {
                    lastProgressSample = Time.unscaledTime;
                    lastProgressPosition = transform.position;
                }
                else if (Time.unscaledTime - lastProgressSample >= Mathf.Max(0.5f, stuckTimeoutSeconds))
                {
                    stuckTriggered = true;
                    PublishStuckMessage();
                    CompanionSkillCooldownTimers.StartCookingCooldown(cooldownTracker);
                    ResetMovementState();
                    EnableFollower();
                    yield break;
                }

                yield return new WaitForFixedUpdate();
            }

            ResetMovementState();

            if (station == null || recipe == null)
            {
                PublishStationUnavailableMessage();
                ResetMovementState();
                EnableFollower();
                yield break;
            }

            int requestedQuantity = Mathf.Max(1, availableQuantity);
            if (usingPlayerInventory && playerInventory != null && rawItem != null)
            {
                int transferred = TransferIngredientsToCompanion(rawItem, requestedQuantity);
                if (transferred <= 0)
                {
                    PublishMissingIngredientMessage();
                    ResetMovementState();
                    EnableFollower();
                    yield break;
                }

                requestedQuantity = transferred;
            }

            if (!cookingSkill.TryStartCooking(station, recipe, requestedQuantity, out string failureMessage))
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.LogWarning($"[Companion Cooking] TryStartCooking failed: {failureMessage}", this);

                if (!string.IsNullOrEmpty(failureMessage) && failureMessage.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0)
                    PublishInventoryFullMessage();
                else
                    PublishStationUnavailableMessage();

                ResetMovementState();
                EnableFollower();
                yield break;
            }

            CompanionSkillCooldownTimers.ClearCookingCooldown(cooldownTracker);
            cookingRoutine = null;
        }

        /// <summary>
        /// Clears any residual velocity or cached navigation data so the companion remains stationary
        /// when arriving at a cooking station or aborting a command.
        /// </summary>
        private void ResetMovementState()
        {
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            if (pathMover != null)
            {
                pathMover.ResetAttackTracking();
                pathMover.ResetCachedVelocity();
            }
        }
        private bool TryMoveWithNavigation(Vector3 stationPosition, float stopDistance)
        {
            if (pathMover == null || !pathMover.isActiveAndEnabled || !pathMover.HasActiveNavigationGrid)
                return false;

            Vector2 nextPosition;
            Vector2 navVelocity;
            bool teleported;
            bool unreachable;

            bool stepped = pathMover.TryStepAttack(
                Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon),
                petFollower != null ? petFollower.moveSpeed : 4f,
                stopDistance,
                WaypointTolerance,
                () => stationPosition,
                ReplanDistance,
                float.PositiveInfinity,
                out nextPosition,
                out navVelocity,
                out teleported,
                out unreachable);

            if (unreachable)
            {
                if (CompanionManager.EnableDebugLogging)
                    Debug.LogWarning("[Companion Cooking] Navigation reported station unreachable.", this);
                stuckTriggered = true;
                return false;
            }

            if (!stepped)
                return false;

            ApplyMovement(nextPosition, navVelocity, teleported);
            return true;
        }

        private void MoveDirectlyTowards(Vector3 destination)
        {
            float deltaTime = Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon);
            float moveSpeed = petFollower != null ? petFollower.moveSpeed : 4f;
            Vector3 start = transform.position;
            Vector3 next = Vector3.MoveTowards(start, destination, moveSpeed * deltaTime);
            Vector2 velocity = deltaTime > Mathf.Epsilon ? (Vector2)((next - start) / deltaTime) : Vector2.zero;
            ApplyMovement(next, velocity, false);
        }
        private CookingObject FindBestStation(float radius)
        {
            areaStations.Clear();
            Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, radius);
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null)
                    continue;

                var station = collider.GetComponentInParent<CookingObject>();
                if (station == null || areaStations.Contains(station))
                    continue;

                areaStations.Add(station);
            }

            if (areaStations.Count == 0)
                return null;

            CookingObject best = null;
            float bestDistance = float.MaxValue;
            Vector3 origin = transform.position;

            for (int i = 0; i < areaStations.Count; i++)
            {
                var station = areaStations[i];
                if (station == null)
                    continue;

                float distance = Vector2.Distance(origin, station.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = station;
                }
            }

            return best;
        }

        private CookableRecipe ResolveRecipeFromInventories(
            out RuntimeInventory sourceInventory,
            out ItemData rawItem,
            out int quantity,
            out bool usingPlayerInventory,
            out string failureMessage)
        {
            sourceInventory = companionInventory;
            rawItem = null;
            quantity = 0;
            usingPlayerInventory = false;

            var companionSearch = CookingInventoryHelper.FindCookableRecipe(companionInventory, cookingSkill, -1);
            if (companionSearch.HasRecipe && companionSearch.CanCook)
            {
                rawItem = companionSearch.RawItem;
                quantity = companionSearch.Quantity;
                failureMessage = string.Empty;
                return companionSearch.Recipe;
            }

            if (companionSearch.HasRecipe && !companionSearch.HasRequiredQuantity)
            {
                failureMessage = companionSearch.FailureMessage;
                return null;
            }

            var playerSearch = CookingInventoryHelper.FindCookableRecipe(playerInventory, playerCookingSkill ?? cookingSkill, -1);
            if (playerSearch.HasRecipe && playerSearch.CanCook)
            {
                usingPlayerInventory = true;
                sourceInventory = playerInventory;
                rawItem = playerSearch.RawItem;
                quantity = playerSearch.Quantity;
                failureMessage = string.Empty;
                return playerSearch.Recipe;
            }

            failureMessage = !string.IsNullOrEmpty(playerSearch.FailureMessage)
                ? playerSearch.FailureMessage
                : companionSearch.FailureMessage;
            return null;
        }

        private CookableRecipe ValidateRecipe(
            CookableRecipe recipe,
            out RuntimeInventory sourceInventory,
            out ItemData rawItem,
            out int quantity,
            out bool usingPlayerInventory,
            out string failureMessage)
        {
            var resolved = ResolveRecipeFromInventories(out sourceInventory, out rawItem, out quantity, out usingPlayerInventory, out failureMessage);
            if (resolved == null)
                return null;

            if (resolved != recipe)
                return resolved;

            return recipe;
        }
        private int TransferIngredientsToCompanion(ItemData rawItem, int requestedQuantity)
        {
            if (playerInventory == null || companionInventory == null || rawItem == null)
                return 0;

            int available = playerInventory.GetItemCount(rawItem);
            if (available <= 0)
                return 0;

            int toMove = Mathf.Min(requestedQuantity, available);
            if (!playerInventory.RemoveItem(rawItem, toMove))
                return 0;

            companionInventory.AddItem(rawItem, toMove);
            return toMove;
        }

        private CompanionCookingCommandResult MapInventoryFailureToCommandResult(string failureMessage)
        {
            if (string.IsNullOrEmpty(failureMessage))
                return CompanionCookingCommandResult.MissingIngredients;

            string lower = failureMessage.ToLowerInvariant();
            if (lower.Contains("level"))
                return CompanionCookingCommandResult.RequirementsNotMet;
            if (lower.Contains("raw"))
                return CompanionCookingCommandResult.MissingIngredients;
            if (lower.Contains("full"))
                return CompanionCookingCommandResult.InventoryFull;

            return CompanionCookingCommandResult.MissingIngredients;
        }

        private bool HasRequiredCookingLevel(CookableRecipe recipe)
        {
            if (skillManager == null || recipe == null)
                return true;

            int level = skillManager.GetLevel(SkillType.Cooking);
            return level >= recipe.requiredLevel;
        }

        private bool HasCookingTool()
        {
            if (validCookingToolIds == null || validCookingToolIds.Count == 0)
                return true;

            for (int i = 0; i < validCookingToolIds.Count; i++)
            {
                string id = validCookingToolIds[i];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var item = ResolveItem(id);
                if (item == null)
                    continue;

                if (companionEquipment != null && EquipmentContainsItem(id))
                    return true;

                if (companionInventory != null && companionInventory.GetItemCount(item) > 0)
                    return true;

                if (playerInventory != null && playerInventory.GetItemCount(item) > 0)
                    return true;
            }

            return false;
        }

        private ItemData ResolveItem(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            if (itemCache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            var item = ItemDatabase.GetItem(id);
            itemCache[id] = item;
            return item;
        }

        private bool EquipmentContainsItem(string itemId)
        {
            if (companionEquipment == null || string.IsNullOrEmpty(itemId))
                return false;

            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (slot == EquipmentSlot.None)
                    continue;

                var entry = companionEquipment.GetEquipped(slot);
                if (entry.item != null && string.Equals(entry.item.id, itemId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        private void HandleCookingStarted(CookableRecipe recipe)
        {
            _ = recipe;
            suppressStopCallback = false;
            PublishCookingCommandStart();
        }

        private void HandleCookingStopped()
        {
            if (suppressStopCallback)
                return;

            ResetMovementState();
            EnableFollower();
            cookingRoutine = null;
            currentStation = null;
            currentRecipe = null;
        }

        private void HandleFoodCooked(string itemId, int quantity)
        {
            if (CompanionManager.EnableDebugLogging)
                Debug.Log($"[Companion Cooking] Cooked {itemId} x{quantity}.", this);
        }

        private void HandleCookingLevelUp(int newLevel)
        {
            _ = newLevel;
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomCookingLevelUpLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
        }

        private void DisableFollower()
        {
            followerDisableLockCount++;
            if (followerDisabled)
                return;

            followerDisabled = true;
            if (petFollower != null)
                petFollower.enabled = false;
        }

        private void EnableFollower()
        {
            followerDisableLockCount = Mathf.Max(0, followerDisableLockCount - 1);
            if (followerDisableLockCount > 0)
                return;

            if (!followerDisabled)
                return;

            followerDisabled = false;
            if (petFollower != null)
                petFollower.enabled = true;
        }

        private void PublishInventoryFullMessage()
        {
            PublishCookingCommandFailure(CompanionCookingCommandResult.InventoryFull);
        }

        private void PublishMissingIngredientMessage()
        {
            PublishCookingCommandFailure(CompanionCookingCommandResult.MissingIngredients);
        }

        private void PublishMissingToolMessage()
        {
            PublishCookingCommandFailure(CompanionCookingCommandResult.MissingTool);
        }

        private void PublishPlayerBusyMessage()
        {
            PublishCookingCommandFailure(CompanionCookingCommandResult.PlayerBusy);
        }

        private void PublishStationUnavailableMessage()
        {
            PublishCookingCommandFailure(CompanionCookingCommandResult.StationUnavailable);
        }

        private void PublishStuckMessage()
        {
            if (hasPublishedOutcomeChat)
                return;

            if (PublishChat(CompanionCookingDialogueLibrary.GetRandomStuckLine()))
                hasPublishedOutcomeChat = true;
        }

        private void PublishLevelRequirementMessage(int requiredLevel)
        {
            if (hasPublishedOutcomeChat)
                return;

            string line = CompanionCookingDialogueLibrary.GetLevelRequirementLine(requiredLevel);
            if (PublishChat(line))
                hasPublishedOutcomeChat = true;
        }

        private void PublishCooldownMessage(TimeSpan remaining)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string playerName = chat.ActiveUsername;
            int minutes = Mathf.Max(1, Mathf.CeilToInt((float)remaining.TotalMinutes));
            string line = CompanionCookingDialogueLibrary.GetCooldownLine(playerName, minutes);
            if (PublishChat(line))
                hasPublishedOutcomeChat = true;
        }

        public void PublishCookingCommandFailure(CompanionCookingCommandResult reason)
        {
            if (hasPublishedOutcomeChat)
                return;

            if (PublishCookingFailureLineInternal(reason))
                hasPublishedOutcomeChat = true;
        }

        public static void PublishCookingFailureLine(CompanionCookingCommandResult reason)
        {
            PublishCookingFailureLineInternal(reason);
        }

        public void PublishCookingCommandStart()
        {
            if (hasPublishedOutcomeChat)
                return;

            if (PublishCookingStartLineInternal())
                hasPublishedOutcomeChat = true;
        }

        public static void PublishCookingStartLine()
        {
            PublishCookingStartLineInternal();
        }

        private static bool PublishCookingFailureLineInternal(CompanionCookingCommandResult reason)
        {
            string message = ResolveFailureLine(reason);
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return PublishChat(message);
        }

        private static bool PublishCookingStartLineInternal()
        {
            string line = CompanionChatLibrary.GetRandomCompanionCookingStartLine();
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return PublishChat(line);
        }

        private static string ResolveFailureLine(CompanionCookingCommandResult reason)
        {
            switch (reason)
            {
                case CompanionCookingCommandResult.InventoryFull:
                    return CompanionCookingDialogueLibrary.GetRandomInventoryFullLine();
                case CompanionCookingCommandResult.MissingIngredients:
                    return CompanionCookingDialogueLibrary.GetRandomMissingIngredientLine();
                case CompanionCookingCommandResult.MissingTool:
                    return CompanionCookingDialogueLibrary.GetRandomMissingToolLine();
                case CompanionCookingCommandResult.PlayerBusy:
                    return CompanionCookingDialogueLibrary.GetRandomPlayerBusyLine();
                case CompanionCookingCommandResult.StationUnavailable:
                case CompanionCookingCommandResult.Unreachable:
                    return CompanionCookingDialogueLibrary.GetRandomStationUnavailableLine();
                case CompanionCookingCommandResult.StationOccupied:
                    return CompanionCookingDialogueLibrary.GetRandomStationOccupiedLine();
                default:
                    return string.Empty;
            }
        }

        private static bool PublishChat(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var chat = ChatService.Instance;
            if (chat == null)
                return false;

            chat.PublishCompanionMessage(CompanionManager.GetCompanionDisplayName(), message);
            return true;
        }

        private void NotifyCooldownFromTracker()
        {
            if (cooldownTracker == null)
                return;

            if (cooldownTracker.TryGetRemaining(SkillType.Cooking, out var remaining) && remaining > TimeSpan.Zero)
                PublishCooldownMessage(remaining);
        }

        /// <summary>
        /// Allows external callers (e.g., cooldown timers) to emit the standard cooking cooldown chat line.
        /// </summary>
        public void NotifyCooldownActive(TimeSpan remaining)
        {
            PublishCooldownMessage(remaining);
        }
    }
}
