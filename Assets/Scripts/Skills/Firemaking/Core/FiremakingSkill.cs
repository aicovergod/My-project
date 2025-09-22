using System;
using System.Collections.Generic;
using BankSystem;
using Core.Save;
using Inventory;
using MyGame.Drops;
using Player;
using Skills.Common;
using Skills.Outfits;
using UI;
using UnityEngine;
using Util;
using Random = UnityEngine.Random;

namespace Skills.Firemaking
{
    /// <summary>
    ///     Implements the Firemaking skill by handling ignition attempts, tracking active bonfires,
    ///     and awarding XP through the shared gathering reward processors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FiremakingSkill : TickedSkillBehaviour
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Equipment equipment;
        [SerializeField] private Transform floatingTextAnchor;
        [SerializeField] private PlayerMover playerMover;
        [SerializeField] private LayerMask fireBlockingLayers;
        [SerializeField] private LayerMask existingFireLayers;
        [SerializeField] private GameObject defaultFirePrefab;
        [SerializeField] private GroundItemSpawner groundItemSpawner;
        [SerializeField] private string tinderboxItemId = "Tinderbox";
        [SerializeField] private float maxIgniteDistance = 1.6f;
        [SerializeField, Tooltip("Enables verbose logging while diagnosing Firemaking behaviour.")]
        private bool enableDebugLogging;
        [SerializeField] private bool allowTileSnapping = true;
        [SerializeField] private Vector2 tileSnapSize = Vector2.one;

        private SkillManager skills;
        private Dictionary<string, FiremakingLogDefinition> logLookup;
        private Dictionary<string, ItemData> itemCache;
        private FiremakingAttempt currentAttempt;
        private readonly List<FiremakingFire> activeFires = new();
        private SkillingOutfitProgress firemakingOutfit;
        private int attemptTicksElapsed;

        private struct FiremakingAttempt
        {
            public FiremakingLogDefinition definition;
            public Vector3 worldPosition;
            public FiremakingFire targetFire;
            public int inventorySlot;
            public int ticksRequired;
            public bool feedingExistingFire;
        }

        /// <summary>
        ///     True whenever the player is mid-ignition attempt.
        /// </summary>
        public bool IsLighting => currentAttempt.definition != null;

        /// <summary>
        ///     Normalised 0..1 representation of the current ignition progress.
        /// </summary>
        public float IgnitionProgressNormalized
        {
            get
            {
                if (!IsLighting || currentAttempt.ticksRequired <= 0)
                    return 0f;
                return Mathf.Clamp01((float)attemptTicksElapsed / currentAttempt.ticksRequired);
            }
        }

        /// <summary>
        ///     World position where the current ignition attempt is centred.
        /// </summary>
        public Vector3 CurrentAttemptPosition => currentAttempt.definition != null ? currentAttempt.worldPosition : transform.position;

        /// <summary>
        ///     Definition describing the logs currently being ignited.
        /// </summary>
        public FiremakingLogDefinition CurrentDefinition => currentAttempt.definition;

        /// <summary>
        ///     Identifier of the tinderbox item required to ignite logs.
        /// </summary>
        public string TinderboxItemId => tinderboxItemId;

        /// <summary>
        ///     Runtime flag used by the admin menu to toggle verbose debug logging.
        /// </summary>
        public bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set => enableDebugLogging = value;
        }

        /// <summary>
        ///     Invoked when an ignition attempt starts so HUDs can attach to the action.
        /// </summary>
        public event Action<FiremakingLogDefinition, Vector3> IgnitionStarted;

        /// <summary>
        ///     Invoked when an ignition attempt ends for any reason.
        /// </summary>
        public event Action IgnitionStopped;

        /// <summary>
        ///     Raised whenever a bonfire gains fuel (either from a fresh log or by feeding an existing fire).
        /// </summary>
        public event Action<FiremakingFire> FireIgnited;

        /// <summary>
        ///     Raised when a tracked fire expires and burns to ashes.
        /// </summary>
        public event Action<FiremakingFire> FireExtinguished;

        /// <summary>
        ///     Raised when the player levels the Firemaking skill so other systems can react.
        /// </summary>
        public event Action<int> OnLevelUp;

        /// <summary>
        ///     Exposes the configured snapping logic so click handlers can mirror the placement rules.
        /// </summary>
        /// <param name="rawWorldPosition">Position gathered from player input.</param>
        /// <returns>Snapped world position that honours the skill configuration.</returns>
        public Vector3 SnapToIgnitionPoint(Vector3 rawWorldPosition)
        {
            if (!allowTileSnapping)
                return new Vector3(rawWorldPosition.x, rawWorldPosition.y, 0f);

            float cellX = Mathf.Approximately(tileSnapSize.x, 0f) ? 1f : tileSnapSize.x;
            float cellY = Mathf.Approximately(tileSnapSize.y, 0f) ? 1f : tileSnapSize.y;
            float snappedX = Mathf.Round(rawWorldPosition.x / cellX) * cellX;
            float snappedY = Mathf.Round(rawWorldPosition.y / cellY) * cellY;
            return new Vector3(snappedX, snappedY, 0f);
        }

        /// <summary>
        ///     Automatically fetches references and loads log definitions so ignition lookups are ready immediately.
        /// </summary>
        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            if (playerMover == null)
                playerMover = GetComponent<PlayerMover>();
            if (groundItemSpawner == null)
                groundItemSpawner = FindObjectOfType<GroundItemSpawner>();
            if (floatingTextAnchor == null)
                floatingTextAnchor = transform;

            skills = GetComponent<SkillManager>();
            logLookup = new Dictionary<string, FiremakingLogDefinition>(StringComparer.Ordinal);
            itemCache = null;

            LoadLogDefinitions();

            firemakingOutfit = new SkillingOutfitProgress(new[]
            {
                "Pyromancer Hood",
                "Pyromancer Garb",
                "Pyromancer Robes",
                "Pyromancer Boots"
            }, "FiremakingOutfitOwned");
        }

        /// <summary>
        ///     Ensures outfit progress is unregistered and fire listeners are torn down when the skill is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            SaveManager.Unregister(firemakingOutfit);
            ClearActiveFires();
        }

        /// <summary>
        ///     Routes ticker subscription logging through the runtime toggle.
        /// </summary>
        protected override bool LogTickerSubscription => enableDebugLogging;

        /// <summary>
        ///     Cleans up attempts and listeners when the component is disabled (e.g. on scene changes).
        /// </summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            CancelAttempt(true);
            ClearActiveFires();
        }

        /// <summary>
        ///     Retrieves the log definition associated with the supplied item identifier.
        /// </summary>
        /// <param name="itemId">Inventory item identifier to look up.</param>
        /// <returns>Matching <see cref="FiremakingLogDefinition"/> or <c>null</c> if not registered.</returns>
        public FiremakingLogDefinition GetDefinitionForItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return null;

            return logLookup != null && logLookup.TryGetValue(itemId, out var definition) ? definition : null;
        }

        /// <summary>
        ///     Validates and begins a new ignition attempt using the specified inventory slot and world position.
        /// </summary>
        /// <param name="inventorySlot">Slot index containing the selected logs.</param>
        /// <param name="worldPosition">Target position where the fire should be created.</param>
        /// <param name="existingFire">Optional fire to feed instead of creating a new one.</param>
        /// <param name="failureReason">Outputs a player-facing reason when the attempt cannot start.</param>
        /// <returns><c>true</c> when the attempt was started, otherwise <c>false</c>.</returns>
        public bool TryBeginLighting(int inventorySlot, Vector3 worldPosition, FiremakingFire existingFire, out string failureReason)
        {
            failureReason = string.Empty;

            if (IsLighting)
            {
                failureReason = "You are already trying to light a fire.";
                return false;
            }

            if (inventory == null)
            {
                failureReason = "You need an inventory to light fires.";
                return false;
            }

            var entry = inventory.GetSlot(inventorySlot);
            if (entry.item == null)
            {
                failureReason = "You need logs to light a fire.";
                return false;
            }

            var definition = GetDefinitionForItem(entry.item.id);
            if (definition == null)
            {
                failureReason = "You can't light those logs.";
                return false;
            }

            if (!string.Equals(entry.item.id, definition.logItemId, StringComparison.Ordinal))
            {
                failureReason = "You need to select the correct logs.";
                return false;
            }

            int level = skills != null ? skills.GetLevel(SkillType.Firemaking) : 1;
            if (level < definition.requiredLevel)
            {
                failureReason = $"You need Firemaking level {definition.requiredLevel}.";
                return false;
            }

            if (!inventory.HasItem(tinderboxItemId))
            {
                failureReason = "You need a tinderbox.";
                return false;
            }

            Vector3 snappedPosition = SnapToIgnitionPoint(worldPosition);
            bool feedingExisting = existingFire != null;

            if (feedingExisting)
            {
                if (existingFire.IsExtinguished)
                {
                    failureReason = "That fire has already burned out.";
                    return false;
                }

                Vector3 firePosition = existingFire.transform.position;
                if (Vector3.Distance(transform.position, firePosition) > maxIgniteDistance)
                {
                    failureReason = "You are too far away.";
                    return false;
                }

                snappedPosition = firePosition;
            }
            else
            {
                if (Vector3.Distance(transform.position, snappedPosition) > maxIgniteDistance)
                {
                    failureReason = "You are too far away.";
                    return false;
                }

                Vector2 checkSize = ResolveCollisionCheckSize();
                if (Physics2D.OverlapBox(snappedPosition, checkSize, 0f, fireBlockingLayers))
                {
                    failureReason = "You can't light a fire there.";
                    return false;
                }

                if (Physics2D.OverlapBox(snappedPosition, checkSize * 0.9f, 0f, existingFireLayers))
                {
                    failureReason = "There is already a fire here.";
                    return false;
                }
            }

            EnsureFireTracked(existingFire);

            currentAttempt = new FiremakingAttempt
            {
                definition = definition,
                worldPosition = snappedPosition,
                targetFire = existingFire,
                inventorySlot = inventorySlot,
                ticksRequired = Mathf.Max(1, definition.GetIgnitionTicks(level)),
                feedingExistingFire = feedingExisting
            };
            attemptTicksElapsed = 0;

            IgnitionStarted?.Invoke(definition, snappedPosition);
            LogDebug($"Started lighting {definition.displayName} at {snappedPosition} (feeding existing: {feedingExisting}).");
            return true;
        }

        /// <summary>
        ///     Attempts to light the selected logs at the player's current tile without requiring a ground target component.
        /// </summary>
        /// <param name="logSlot">Inventory slot that contains the logs to ignite.</param>
        /// <param name="failureReason">Outputs a player-facing message when the attempt cannot start.</param>
        /// <returns><c>true</c> when the ignition attempt was started, otherwise <c>false</c>.</returns>
        public bool BeginLightingFromInventory(int logSlot, out string failureReason)
        {
            failureReason = string.Empty;
            Vector3 anchorPosition = transform != null ? transform.position : Vector3.zero;
            anchorPosition.z = 0f;
            Vector3 snapped = SnapToIgnitionPoint(anchorPosition);

            FiremakingFire targetFire = null;
            if (existingFireLayers.value != 0)
            {
                Vector2 checkSize = ResolveCollisionCheckSize();
                Collider2D fireCollider = Physics2D.OverlapBox(snapped, checkSize * 0.9f, 0f, existingFireLayers);
                if (fireCollider != null)
                    targetFire = fireCollider.GetComponentInParent<FiremakingFire>() ?? fireCollider.GetComponent<FiremakingFire>();
            }

            return TryBeginLighting(logSlot, snapped, targetFire, out failureReason);
        }

        /// <summary>
        ///     Cancels the active attempt, optionally informing the player via floating text.
        /// </summary>
        /// <param name="showMessage">True to display the default cancellation feedback message.</param>
        public void CancelAttempt(bool showMessage)
        {
            CancelAttemptInternal(showMessage, showMessage ? "You stop trying to light the logs." : null);
        }

        /// <summary>
        ///     Runs each tick of the ignition attempt, checking for cancellation conditions and resolving success rolls.
        /// </summary>
        protected override void HandleTick()
        {
            if (!IsLighting)
                return;

            var definition = currentAttempt.definition;
            if (definition == null)
            {
                CancelAttemptInternal(false, null);
                return;
            }

            var entry = inventory.GetSlot(currentAttempt.inventorySlot);
            if (entry.item == null || entry.item.id != definition.logItemId)
            {
                CancelAttemptInternal(true, "You ran out of logs.");
                return;
            }

            if (currentAttempt.feedingExistingFire)
            {
                if (currentAttempt.targetFire == null || currentAttempt.targetFire.IsExtinguished)
                {
                    CancelAttemptInternal(true, "The fire has already burned out.");
                    return;
                }
            }

            if (playerMover != null && playerMover.IsMoving)
            {
                CancelAttemptInternal(true, "You stop trying to light the logs.");
                return;
            }

            Vector3 anchor = currentAttempt.feedingExistingFire && currentAttempt.targetFire != null
                ? currentAttempt.targetFire.transform.position
                : currentAttempt.worldPosition;
            if (Vector3.Distance(transform.position, anchor) > maxIgniteDistance)
            {
                CancelAttemptInternal(true, "You are too far away.");
                return;
            }

            attemptTicksElapsed++;
            LogDebug($"Firemaking tick {attemptTicksElapsed}/{currentAttempt.ticksRequired}.");

            if (attemptTicksElapsed < currentAttempt.ticksRequired)
                return;

            attemptTicksElapsed = 0;
            int level = skills != null ? skills.GetLevel(SkillType.Firemaking) : 1;
            float chance = definition.GetSuccessChance(level, currentAttempt.feedingExistingFire);
            bool success = Random.value <= chance;

            if (success)
            {
                CompleteAttempt();
            }
            else
            {
                ShowFeedback("The logs fail to catch fire.", anchor);
                LogDebug($"Failed to light {definition.displayName} (chance {chance:P2}).");
            }
        }

        /// <summary>
        ///     Handles the success workflow after an ignition attempt finishes, including XP awards and fire creation.
        /// </summary>
        private void CompleteAttempt()
        {
            var definition = currentAttempt.definition;
            if (definition == null)
            {
                CancelAttemptInternal(false, null);
                return;
            }

            if (inventory == null || !inventory.RemoveItem(definition.logItemId))
            {
                CancelAttemptInternal(true, "You ran out of logs.");
                return;
            }

            int lifetime = definition.GetLifetimeContribution(currentAttempt.feedingExistingFire);
            var ashesItem = GatheringInventoryHelper.GetItemData(definition.ashesItemId, ref itemCache);

            FiremakingFire fire = currentAttempt.targetFire;
            bool createdNewFire = fire == null;

            if (createdNewFire)
            {
                GameObject prefab = definition.firePrefab != null ? definition.firePrefab : defaultFirePrefab;
                GameObject instance;
                if (prefab != null)
                {
                    instance = Instantiate(prefab, currentAttempt.worldPosition, Quaternion.identity);
                }
                else
                {
                    instance = new GameObject("FiremakingFire");
                    instance.transform.position = currentAttempt.worldPosition;
                }

                fire = instance.GetComponent<FiremakingFire>();
                if (fire == null)
                    fire = instance.AddComponent<FiremakingFire>();

                fire.SetOwner(this);
                fire.Initialise(definition, lifetime, definition.maxLifetimeTicks, ashesItem, groundItemSpawner, definition.igniteSound, definition.extinguishSound);
                EnsureFireTracked(fire);
            }
            else
            {
                fire.AddFuel(lifetime, definition.maxLifetimeTicks, definition.igniteSound);
            }

            FireIgnited?.Invoke(fire);

            string successMessage = currentAttempt.feedingExistingFire
                ? "You add a log to the fire."
                : "The fire catches alight.";
            ShowFeedback(successMessage, currentAttempt.worldPosition);

            var context = GatheringRewardContextBuilder.BuildContext(new GatheringRewardContextBuilder.ContextArgs
            {
                Runner = this,
                Skills = skills,
                SkillType = SkillType.Firemaking,
                Inventory = inventory,
                Item = null,
                RewardDisplayName = definition.displayName,
                Quantity = 1,
                XpPerItem = definition.xp,
                PetAssistExtraQuantity = 0,
                FloatingTextAnchor = floatingTextAnchor,
                FallbackAnchor = transform,
                Equipment = equipment,
                EquipmentXpBonusEvaluator = data => data != null ? data.firemakingXpBonusMultiplier : 0f,
                CustomAddItemHandler = _ => true,
                ShowItemFloatingText = false,
                OnSuccess = _ =>
                {
                    if (definition.phoenixPetRoll > 0 && fire != null)
                        SkillingPetRewarder.TryRollPet("firemaking", skills, fire.transform, definition.phoenixPetRoll);
                    TryAwardFiremakingOutfitPiece();
                },
                LevelUpFloatingTextFormatter = result => $"Firemaking level {result.NewLevel}",
                OnLevelUp = level => OnLevelUp?.Invoke(level)
            });

            GatheringRewardProcessor.Process(context);

            LogDebug($"Successfully lit {definition.displayName} at {currentAttempt.worldPosition}.");
            FinishAttempt();
        }

        /// <summary>
        ///     Handles cleanup when a tracked fire expires and burns to ashes.
        /// </summary>
        /// <param name="fire">Fire instance that triggered the event.</param>
        private void HandleFireExtinguished(FiremakingFire fire)
        {
            if (fire == null)
                return;

            fire.Extinguished -= HandleFireExtinguished;
            activeFires.Remove(fire);
            FireExtinguished?.Invoke(fire);
            LogDebug($"Fire at {fire.transform.position} burned to ashes.");
            ShowFeedback("Your fire burns to ashes.", fire.transform.position);
        }

        /// <summary>
        ///     Loads all Firemaking log definitions from Resources and builds the lookup dictionary.
        /// </summary>
        private void LoadLogDefinitions()
        {
            logLookup.Clear();
            var definitions = Resources.LoadAll<FiremakingLogDefinition>("Firemaking/Logs");
            if (definitions == null || definitions.Length == 0)
            {
                Debug.LogWarning("[FiremakingSkill] No Firemaking log definitions were found in Resources/Firemaking/Logs.");
                return;
            }

            foreach (var definition in definitions)
            {
                if (definition == null)
                    continue;

                if (string.IsNullOrEmpty(definition.logItemId))
                {
                    Debug.LogWarning($"[FiremakingSkill] Log definition '{definition.name}' is missing a log item id.");
                    continue;
                }

                if (logLookup.ContainsKey(definition.logItemId))
                    Debug.LogWarning($"[FiremakingSkill] Duplicate log item id '{definition.logItemId}' detected. Overwriting previous entry.");

                logLookup[definition.logItemId] = definition;
            }
        }

        /// <summary>
        ///     Helper used by other systems to attempt a pyromancer outfit roll.
        /// </summary>
        /// <returns>True when an outfit piece was awarded.</returns>
        private bool TryAwardFiremakingOutfitPiece()
        {
            return SkillingOutfitRewarder.TryAwardPiece(
                firemakingOutfit,
                inventory,
                BankUI.Instance,
                Random.Range,
                "Firemaking",
                "You receive a piece of the pyromancer outfit.",
                "A pyromancer outfit piece has been sent to your bank.");
        }

        /// <summary>
        ///     Displays floating text feedback at the supplied position.
        /// </summary>
        /// <param name="message">Text to show.</param>
        /// <param name="position">World position where the text should appear.</param>
        private void ShowFeedback(string message, Vector3 position)
        {
            if (string.IsNullOrEmpty(message))
                return;

            FloatingText.Show(message, position);
        }

        /// <summary>
        ///     Cancels the current attempt with a custom message, used internally by validation checks.
        /// </summary>
        /// <param name="showMessage">Whether to display floating text.</param>
        /// <param name="message">Optional message to show.</param>
        private void CancelAttemptInternal(bool showMessage, string message)
        {
            if (!IsLighting)
                return;

            if (showMessage && !string.IsNullOrEmpty(message))
                ShowFeedback(message, CurrentAttemptPosition);

            LogDebug(message != null ? $"Cancelled firemaking attempt: {message}" : "Cancelled firemaking attempt.");
            FinishAttempt();
        }

        /// <summary>
        ///     Resets attempt state and notifies listeners that Firemaking has stopped.
        /// </summary>
        private void FinishAttempt()
        {
            currentAttempt = default;
            attemptTicksElapsed = 0;
            IgnitionStopped?.Invoke();
        }

        /// <summary>
        ///     Computes the overlap size used to check for blocking colliders at the target location.
        /// </summary>
        /// <returns>Size vector appropriate for the configured grid.</returns>
        private Vector2 ResolveCollisionCheckSize()
        {
            if (!allowTileSnapping)
                return new Vector2(0.9f, 0.9f);

            float width = Mathf.Approximately(tileSnapSize.x, 0f) ? 1f : tileSnapSize.x;
            float height = Mathf.Approximately(tileSnapSize.y, 0f) ? 1f : tileSnapSize.y;
            return new Vector2(Mathf.Abs(width), Mathf.Abs(height));
        }

        /// <summary>
        ///     Ensures the supplied fire is tracked so extinction events can be handled consistently.
        /// </summary>
        /// <param name="fire">Fire instance to track.</param>
        private void EnsureFireTracked(FiremakingFire fire)
        {
            if (fire == null)
                return;

            if (!activeFires.Contains(fire))
            {
                fire.Extinguished += HandleFireExtinguished;
                fire.SetOwner(this);
                activeFires.Add(fire);
            }
        }

        /// <summary>
        ///     Removes event listeners from every tracked fire. Invoked when the skill is disabled or destroyed.
        /// </summary>
        private void ClearActiveFires()
        {
            foreach (var fire in activeFires)
            {
                if (fire != null)
                    fire.Extinguished -= HandleFireExtinguished;
            }

            activeFires.Clear();
        }

        /// <summary>
        ///     Emits a formatted debug message when verbose logging is enabled.
        /// </summary>
        /// <param name="message">Message to output to the Unity console.</param>
        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[FiremakingSkill] {message}");
        }
    }
}
