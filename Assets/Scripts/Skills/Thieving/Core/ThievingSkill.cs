using System;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Player.Movement;
using Player;
using Skills.Common;
using Skills.Thieving.Data;
using Skills.Outfits;
using Skills.Thieving.NpcPickpocketDialogue;
using UI;
using UI.Chat;
using Util;
using Random = UnityEngine.Random;
using Pets;

namespace Skills.Thieving.Core
{
    /// <summary>
    ///     Implements the core Old School RuneScape style thieving behaviour. Handles pickpocketing NPCs as well as stealing
    ///     from world objects using the shared 0.6s ticker cadence.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThievingSkill : DebuggableTickedSkillBehaviour
    {
        private const string RogueOutfitResourcePath = "Skills/Outfits/RogueOutfitDefinition";
        private const string CoinsItemId = "Gold Coins";
        private const string FailureFloatingText = "You fail to pick the pocket.";
        private const string OutOfRangeFloatingText = "You can't reach that.";
        private const float TileSize = 1f;
        private const float PickpocketRangeTiles = 1f;
        private const float PickpocketStopBufferTiles = 0.1f;

        private enum AttemptMode
        {
            None,
            PickpocketNpc,
            StealObject
        }

        [Header("Dependencies")]
        [SerializeField]
        private Inventory.Inventory inventory;

        [SerializeField]
        private PlayerMovementController movement;

        [SerializeField]
        private PlayerHitpoints hitpoints;

        [SerializeField]
        private SkillManager skillManager;

        [SerializeField]
        private Transform floatingTextAnchor;

        [SerializeField]
        private ThievingDefinitionDatabase database;

        [SerializeField, Tooltip("Definition describing the Rogue outfit so bonus rolls can be evaluated.")]
        private SkillingOutfitDefinition rogueOutfitDefinition;

        private readonly TickProgressTracker attemptProgress = new TickProgressTracker();
        private AttemptMode attemptMode;
        private NpcThievingTarget activeNpc;
        private ThievingObjectNode activeObject;
        private int attemptTicksRequired;
        private Vector3 attemptWorldPosition;
        private float stunEndTime;
        private bool isLocked;
        private int consecutiveNpcFailures;
        private SkillingOutfitProgress rogueOutfitProgress;
        private ItemData cachedCoinItem;
        private bool unfreezeScheduled;
        private bool isApproachingPickpocketTarget;
        private bool awaitingPickpocketApproachArrival;
        private NpcThievingTarget approachingPickpocketTarget;
        private float approachingPickpocketRange;

        internal Func<int> PickpocketRoll { get; set; } = () => Random.Range(0, 256);

        public static bool GlobalDebugLogging { get; private set; }

        /// <summary>
        ///     Raised when a pickpocket attempt begins.
        /// </summary>
        public event Action<NpcThievingTarget> PickpocketStarted;

        /// <summary>
        ///     Raised when a pickpocket attempt completes. The bool indicates success.
        /// </summary>
        public event Action<NpcThievingTarget, bool> PickpocketFinished;

        /// <summary>
        ///     Raised when stealing from a world object begins.
        /// </summary>
        public event Action<ThievingObjectNode> ObjectTheftStarted;

        /// <summary>
        ///     Raised when stealing from a world object completes. The bool indicates success.
        /// </summary>
        public event Action<ThievingObjectNode, bool> ObjectTheftFinished;

        /// <summary>
        ///     Raised when the player levels up.
        /// </summary>
        public event Action<int> LevelledUp;

        /// <summary>
        ///     Raised whenever an active attempt is cancelled by an external system.
        /// </summary>
        public event Action AttemptCancelled;

        /// <summary>
        ///     Indicates whether an attempt is currently in progress.
        /// </summary>
        public bool IsAttemptActive => attemptMode != AttemptMode.None;

        /// <summary>
        ///     Normalised 0..1 progress for the active attempt. Returns 0 when idle.
        /// </summary>
        public float AttemptProgressNormalized
        {
            get
            {
                if (!IsAttemptActive || attemptTicksRequired <= 0)
                    return 0f;

                return Mathf.Clamp01((float)attemptProgress.ProgressTicks / attemptTicksRequired);
            }
        }

        /// <summary>
        ///     Number of ticks required for the currently armed attempt.
        /// </summary>
        public int AttemptTicksRequired => attemptTicksRequired;

        /// <summary>
        ///     World anchor position associated with the current attempt.
        /// </summary>
        public Vector3 AttemptAnchorPosition => attemptWorldPosition;

        /// <summary>
        ///     Definition backing the active NPC pickpocket attempt.
        /// </summary>
        public ThievingNpcDefinition ActiveNpcDefinition => activeNpc != null ? activeNpc.Definition : null;

        /// <summary>
        ///     Definition backing the active object theft attempt.
        /// </summary>
        public ThievingObjectDefinition ActiveObjectDefinition => activeObject != null ? activeObject.Definition : null;

        /// <summary>
        ///     Currently targeted object node (null when not stealing from an object).
        /// </summary>
        public ThievingObjectNode ActiveObjectNode => activeObject;

        /// <summary>
        ///     Current thieving level.
        /// </summary>
        public int CurrentLevel => skillManager != null ? skillManager.GetLevel(SkillType.Thieving) : 1;

        /// <summary>
        ///     Current thieving XP.
        /// </summary>
        public float CurrentXp => skillManager != null ? skillManager.GetXp(SkillType.Thieving) : 0f;

        /// <summary>
        ///     Ensures the static debug flag mirrors the serialized value.
        /// </summary>
        public new bool EnableDebugLogging
        {
            get => base.EnableDebugLogging;
            set
            {
                base.EnableDebugLogging = value;
                GlobalDebugLogging = value;
            }
        }

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (movement == null)
                movement = GetComponent<PlayerMovementController>()
                    ?? GetComponentInParent<PlayerMovementController>();
            if (hitpoints == null)
                hitpoints = GetComponent<PlayerHitpoints>();
            if (skillManager == null)
                skillManager = GetComponent<SkillManager>();
            if (floatingTextAnchor == null)
                floatingTextAnchor = transform;
            if (database == null)
                database = Resources.Load<ThievingDefinitionDatabase>("Thieving/ThievingDefinitionDatabase");

            attemptProgress.TickAdvanced += HandleAttemptProgressAdvanced;
            attemptProgress.ProgressReset += HandleAttemptProgressReset;

            rogueOutfitProgress = SkillingOutfitInitializer.InitializeOutfitProgress(
                ref rogueOutfitDefinition,
                RogueOutfitResourcePath,
                nameof(ThievingSkill),
                this);

            if (rogueOutfitProgress != null && rogueOutfitProgress.owned == null)
                rogueOutfitProgress.owned = new HashSet<string>(StringComparer.Ordinal);

            cachedCoinItem = ItemDatabase.GetItem(CoinsItemId);
            GlobalDebugLogging = EnableDebugLogging;

            TrySubscribeToTicker();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            CancelPickpocketApproach(true);
        }

        private void OnDestroy()
        {
            attemptProgress.TickAdvanced -= HandleAttemptProgressAdvanced;
            attemptProgress.ProgressReset -= HandleAttemptProgressReset;
            SkillingOutfitProgress.Unregister(rogueOutfitProgress);
            rogueOutfitProgress = null;
        }

        private void HandleAttemptProgressAdvanced(int progressTicks, int requiredTicks)
        {
            if (!EnableDebugLogging)
                return;

            Debug.Log($"[Thieving] Progress {progressTicks}/{requiredTicks}.", this);
        }

        private void HandleAttemptProgressReset(int requiredTicks)
        {
            if (!EnableDebugLogging)
                return;

            Debug.Log($"[Thieving] Attempt reset for {requiredTicks} ticks.", this);
        }

        /// <summary>
        ///     Monitors any active auto-walk towards a pickpocket target so requests are cancelled cleanly when
        ///     movement is interrupted or the target becomes invalid.
        /// </summary>
        private void Update()
        {
            UpdatePickpocketApproachState();
        }

        /// <summary>
        ///     Attempts to start a pickpocket against the supplied NPC target.
        /// </summary>
        public bool TryStartPickpocket(NpcThievingTarget target)
        {
            if (target == null || target.Definition == null)
                return false;
            if (!target.CanPickpocket)
                return false;
            if (IsAttemptActive || isLocked)
                return false;

            int level = CurrentLevel;
            ThievingNpcDefinition definition = target.Definition;
            if (level < definition.RequiredLevel)
            {
                string message = $"You need Thieving level {definition.RequiredLevel}.";
                bool displayed = GatheringFloatingTextService.TryShowAtAnchor(
                    message,
                    floatingTextAnchor);
                LogFloatingTextAttempt(
                    "PickpocketLevelRequirementFailed",
                    message,
                    floatingTextAnchor,
                    displayed,
                    target != null ? target.transform.position : transform.position);
                return false;
            }

            if (Time.time < stunEndTime)
                return false;

            if (!CanAcceptNpcLoot(definition, out string failureMessage))
            {
                bool displayed = GatheringFloatingTextService.TryShowAtAnchor(failureMessage, floatingTextAnchor);
                LogFloatingTextAttempt(
                    "PickpocketInventoryCheckFailed",
                    failureMessage,
                    floatingTextAnchor,
                    displayed,
                    target != null ? target.transform.position : transform.position);
                return false;
            }

            float interactionRange = ResolvePickpocketInteractionRange(target);
            if (!IsWithinPickpocketRange(target, interactionRange))
            {
                BeginPickpocketApproach(target, interactionRange);
                return false;
            }

            CancelPickpocketApproach(false);

            attemptMode = AttemptMode.PickpocketNpc;
            activeNpc = target;
            activeObject = null;
            attemptTicksRequired = Mathf.Max(1, definition.InteractionTicks);
            attemptWorldPosition = target.transform.position;
            attemptProgress.Reset(attemptTicksRequired);
            target.NotifyAttemptStarted();
            isLocked = true;
            movement?.FaceTarget(target.transform);

            PickpocketStarted?.Invoke(target);
            if (EnableDebugLogging)
            {
                Debug.Log($"[Thieving] Started pickpocketing {definition.DisplayName} for {attemptTicksRequired} ticks.", this);
            }

            return true;
        }

        /// <summary>
        ///     Attempts to start stealing from the supplied world object node.
        /// </summary>
        public bool TryStartObjectTheft(ThievingObjectNode node)
        {
            if (node == null)
                return false;
            if (node.IsDepleted)
                return false;
            if (node.Definition == null)
                return false;
            if (IsAttemptActive || isLocked)
                return false;
            if (Time.time < stunEndTime)
                return false;

            var definition = node.Definition;
            if (CurrentLevel < definition.RequiredLevel)
            {
                string message = $"You need Thieving level {definition.RequiredLevel} to steal from this.";
                bool displayed = GatheringFloatingTextService.TryShowAtAnchor(
                    message,
                    floatingTextAnchor);
                LogFloatingTextAttempt(
                    "ObjectLevelRequirementFailed",
                    message,
                    floatingTextAnchor,
                    displayed,
                    node != null ? node.transform.position : transform.position);
                return false;
            }

            if (!CanAcceptObjectLoot(node, out string failureMessage))
            {
                if (!string.IsNullOrEmpty(failureMessage))
                {
                    bool displayed = GatheringFloatingTextService.TryShowAtAnchor(failureMessage, floatingTextAnchor);
                    LogFloatingTextAttempt(
                        "ObjectInventoryCheckFailed",
                        failureMessage,
                        floatingTextAnchor,
                        displayed,
                        node != null ? node.transform.position : transform.position);
                }
                return false;
            }

            attemptMode = AttemptMode.StealObject;
            activeNpc = null;
            activeObject = node;
            attemptTicksRequired = Mathf.Max(1, definition.InteractionTicks);
            attemptWorldPosition = node.InteractionPoint;
            attemptProgress.Reset(attemptTicksRequired);
            isLocked = true;
            movement?.FaceTarget(node.transform);

            ObjectTheftStarted?.Invoke(node);
            if (EnableDebugLogging)
            {
                Debug.Log($"[Thieving] Started stealing from {definition.DisplayName} for {attemptTicksRequired} ticks.", this);
            }

            return true;
        }

        /// <summary>
        ///     Cancels the current attempt, if any.
        /// </summary>
        public void CancelAttempt()
        {
            if (!IsAttemptActive)
                return;

            if (EnableDebugLogging)
                Debug.Log("[Thieving] Attempt cancelled.", this);

            CleanupAttempt();
            AttemptCancelled?.Invoke();
        }

        protected override void HandleTick()
        {
            if (Time.time < stunEndTime)
                return;

            if (!IsAttemptActive)
                return;

            if (attemptProgress.Advance())
                ResolveAttempt();
        }

        private void ResolveAttempt()
        {
            switch (attemptMode)
            {
                case AttemptMode.PickpocketNpc:
                    ResolveNpcPickpocket();
                    break;
                case AttemptMode.StealObject:
                    ResolveObjectTheft();
                    break;
            }
        }

        private void ResolveNpcPickpocket()
        {
            if (activeNpc == null || activeNpc.Definition == null)
            {
                CancelAttempt();
                return;
            }

            var definition = activeNpc.Definition;
            int level = CurrentLevel;
            int threshold = definition.GetSuccessThreshold(level);
            int roll = Mathf.Clamp(PickpocketRoll != null ? PickpocketRoll() : Random.Range(0, 256), 0, 255);
            bool success = roll <= threshold;

            if (EnableDebugLogging)
            {
                Debug.Log($"[Thieving] Pickpocket roll {roll} <= {threshold} (level {level}). Success: {success}.", this);
            }

            if (success)
            {
                consecutiveNpcFailures = 0;
                AwardNpcRewards(definition, activeNpc.transform.position);
                NpcPickpocketDialogueService.TryPublishDialogue(definition, activeNpc.DialogueAnchor, true);
                activeNpc.NotifyAttemptFinished(true, false);
                PickpocketFinished?.Invoke(activeNpc, true);
            }
            else
            {
                consecutiveNpcFailures++;
                HandlePickpocketFailure(definition);
                NpcPickpocketDialogueService.TryPublishDialogue(definition, activeNpc.DialogueAnchor, false);
                bool triggerLockout = consecutiveNpcFailures >= definition.FailuresBeforeCooldown;
                activeNpc.NotifyAttemptFinished(false, triggerLockout);
                if (triggerLockout)
                    consecutiveNpcFailures = 0;
                PickpocketFinished?.Invoke(activeNpc, false);
            }

            CleanupAttempt();
        }

        private void ResolveObjectTheft()
        {
            if (activeObject == null || activeObject.Definition == null)
            {
                CancelAttempt();
                return;
            }

            var definition = activeObject.Definition;
            // OSRS stalls succeed automatically provided the player meets the requirement.
            AwardObjectRewards(definition, activeObject.InteractionPoint);
            activeObject.OnStolen();
            ObjectTheftFinished?.Invoke(activeObject, true);
            CleanupAttempt();
        }

        /// <summary>
        ///     Resolves the interaction range required to begin a pickpocket attempt. Currently fixed to one tile but
        ///     exposed as a helper so future definitions can override the distance if needed.
        /// </summary>
        private float ResolvePickpocketInteractionRange(NpcThievingTarget target)
        {
            _ = target;
            return Mathf.Max(0f, PickpocketRangeTiles * TileSize);
        }

        /// <summary>
        ///     Calculates the stop distance fed into the auto-move routine so the player halts just inside the pickpocket
        ///     interaction radius.
        /// </summary>
        private float ResolvePickpocketStopDistance(float interactionRange)
        {
            float buffer = Mathf.Max(0f, PickpocketStopBufferTiles * TileSize);
            return Mathf.Max(0f, interactionRange - buffer);
        }

        /// <summary>
        ///     Determines whether the player is currently standing within the supplied pickpocket interaction range.
        /// </summary>
        private bool IsWithinPickpocketRange(NpcThievingTarget target, float interactionRange)
        {
            if (target == null)
                return false;

            float distance = Vector3.Distance(transform.position, target.transform.position);
            return distance <= interactionRange;
        }

        /// <summary>
        ///     Starts automatically moving the player towards the NPC so the pickpocket can begin as soon as the
        ///     interaction range is reached.
        /// </summary>
        private void BeginPickpocketApproach(NpcThievingTarget target, float interactionRange)
        {
            if (target == null)
                return;

            if (movement == null)
            {
                bool displayed = GatheringFloatingTextService.TryShowAtAnchor(OutOfRangeFloatingText, floatingTextAnchor);
                LogFloatingTextAttempt(
                    "PickpocketOutOfRange",
                    OutOfRangeFloatingText,
                    floatingTextAnchor,
                    displayed,
                    target.transform.position);
                return;
            }

            if (!target.CanPickpocket || target.Definition == null)
                return;

            CancelPickpocketApproach(false);

            isApproachingPickpocketTarget = true;
            approachingPickpocketTarget = target;
            approachingPickpocketRange = interactionRange;
            awaitingPickpocketApproachArrival = true;

            float stopDistance = ResolvePickpocketStopDistance(interactionRange);
            movement.MoveTo(target.transform, stopDistance, () => HandlePickpocketApproachArrived(target));
        }

        /// <summary>
        ///     Invoked once the auto-walk completes. Revalidates the target before either starting the pickpocket or
        ///     reissuing the approach if the player stopped short.
        /// </summary>
        private void HandlePickpocketApproachArrived(NpcThievingTarget target)
        {
            awaitingPickpocketApproachArrival = false;

            if (!isApproachingPickpocketTarget || approachingPickpocketTarget != target)
                return;

            if (target == null || target.Definition == null || !target.CanPickpocket)
            {
                CancelPickpocketApproach(false);
                return;
            }

            float interactionRange = approachingPickpocketRange;
            if (!IsWithinPickpocketRange(target, interactionRange))
            {
                BeginPickpocketApproach(target, interactionRange);
                return;
            }

            CancelPickpocketApproach(false);
            TryStartPickpocket(target);
        }

        /// <summary>
        ///     Clears any pending auto-move towards a pickpocket target and optionally halts the player's movement.
        /// </summary>
        private void CancelPickpocketApproach(bool stopMovement)
        {
            if (stopMovement && movement != null && movement.IsAutoMoving)
                movement.StopMovement();

            isApproachingPickpocketTarget = false;
            awaitingPickpocketApproachArrival = false;
            approachingPickpocketTarget = null;
            approachingPickpocketRange = 0f;
        }

        /// <summary>
        ///     Evaluates the current pickpocket auto-approach each frame to ensure it cancels when the target becomes
        ///     invalid or when the player manually interrupts movement.
        /// </summary>
        private void UpdatePickpocketApproachState()
        {
            if (!isApproachingPickpocketTarget)
                return;

            if (approachingPickpocketTarget == null || approachingPickpocketTarget.Definition == null || !approachingPickpocketTarget.CanPickpocket)
            {
                CancelPickpocketApproach(true);
                return;
            }

            if (movement == null)
            {
                CancelPickpocketApproach(true);
                return;
            }

            if (awaitingPickpocketApproachArrival && !movement.IsAutoMoving)
            {
                CancelPickpocketApproach(false);
                return;
            }

            if (!awaitingPickpocketApproachArrival && IsWithinPickpocketRange(approachingPickpocketTarget, approachingPickpocketRange))
            {
                var target = approachingPickpocketTarget;
                CancelPickpocketApproach(false);
                TryStartPickpocket(target);
            }
        }

        private void HandlePickpocketFailure(ThievingNpcDefinition definition)
        {
            if (definition.DamageOnFail > 0 && activeNpc != null)
                activeNpc.TriggerPickpocketCounterAttack(transform);

            if (hitpoints != null && definition.DamageOnFail > 0)
                hitpoints.OnEnemyDealtDamage(definition.DamageOnFail);

            if (movement != null)
            {
                movement.SetMovementFrozen(true);
                unfreezeScheduled = true;
            }

            float stunDuration = definition.StunTicks * Ticker.TickDuration;
            stunEndTime = Time.time + stunDuration;
            Invoke(nameof(ClearStunLock), stunDuration);
            isLocked = true;

            bool displayedAtAnchor = GatheringFloatingTextService.TryShowAtAnchor(FailureFloatingText, floatingTextAnchor);
            LogFloatingTextAttempt(
                "PickpocketFailure",
                FailureFloatingText,
                floatingTextAnchor,
                displayedAtAnchor,
                activeNpc != null ? activeNpc.transform.position : transform.position);

            if (!displayedAtAnchor)
            {
                Transform anchorTransform = floatingTextAnchor != null ? floatingTextAnchor : transform;
                if (EnableDebugLogging)
                {
                    Debug.Log(
                        $"[Thieving] Floating text fallback triggered for failure message. Anchor description: {DescribeAnchor(anchorTransform)}.",
                        this);
                }
                FloatingText.Show(FailureFloatingText, anchorTransform != null ? anchorTransform.position : transform.position);
            }
            ChatService.Instance?.PublishGameMessage("You fail to pick the pocket.");

            if (EnableDebugLogging)
            {
                Debug.Log($"[Thieving] Failure dealt {definition.DamageOnFail} damage and stunned for {definition.StunTicks} ticks.", this);
            }
        }

        private void ClearStunLock()
        {
            stunEndTime = 0f;
            isLocked = false;
            if (movement != null && unfreezeScheduled)
            {
                movement.SetMovementFrozen(false);
                unfreezeScheduled = false;
            }
        }

        private void CleanupAttempt()
        {
            attemptMode = AttemptMode.None;
            activeNpc = null;
            activeObject = null;
            attemptTicksRequired = 0;
            attemptWorldPosition = Vector3.zero;
            attemptProgress.ClearProgress();
            isLocked = false;
            CancelPickpocketApproach(false);
        }

        private void AwardNpcRewards(ThievingNpcDefinition definition, Vector3 worldPosition)
        {
            var rewards = ResolveLootRolls(definition.CoinRange, definition.LootTable, definition.BaseLootRolls);
            if (EvaluateRogueOutfitBonus())
            {
                var bonus = ResolveLootRolls(Vector2Int.zero, definition.LootTable, definition.RogueOutfitBonusRolls);
                rewards.AddRange(bonus);
            }

            ProcessRewards(rewards, definition.BaseXp, worldPosition);

            if (definition.PetRollDenominator > 0)
            {
                SkillingPetRewarder.TryRollPet("thieving", skillManager, floatingTextAnchor, definition.PetRollDenominator, transform);
            }
        }

        private void AwardObjectRewards(ThievingObjectDefinition definition, Vector3 worldPosition)
        {
            var rewards = ResolveLootRolls(definition.CoinRange, definition.LootTable, definition.BaseLootRolls);
            if (EvaluateRogueOutfitBonus())
            {
                var bonus = ResolveLootRolls(Vector2Int.zero, definition.LootTable, definition.RogueOutfitBonusRolls);
                rewards.AddRange(bonus);
            }

            ProcessRewards(rewards, definition.BaseXp, worldPosition);

            if (definition.PetRollDenominator > 0)
            {
                SkillingPetRewarder.TryRollPet("thieving", skillManager, floatingTextAnchor, definition.PetRollDenominator, transform);
            }
        }

        private void ProcessRewards(List<(ItemData item, int quantity)> rewards, float xp, Vector3 worldPosition)
        {
            if (inventory == null)
                return;

            var activePetStorage = PetDropSystem.ActivePetObject != null
                ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                : null;
            bool anyRewardSucceeded = false;
            bool inventoryBlocked = false;

            foreach (var reward in rewards)
            {
                if (reward.item == null || reward.quantity <= 0)
                    continue;

                string rewardName = !string.IsNullOrEmpty(reward.item.itemName)
                    ? reward.item.itemName
                    : reward.item.name;

                var context = GatheringRewardContextBuilder.BuildContext(new GatheringRewardContextBuilder.ContextArgs
                {
                    Runner = this,
                    Skills = skillManager,
                    SkillType = SkillType.Thieving,
                    Inventory = inventory,
                    PetStorage = activePetStorage,
                    Item = reward.item,
                    RewardDisplayName = rewardName,
                    Quantity = reward.quantity,
                    XpPerItem = 0f,
                    FloatingTextAnchor = floatingTextAnchor,
                    FallbackAnchor = transform,
                    ResourcePosition = worldPosition,
                    RewardMessageFormatter = quantity => $"+{quantity} {rewardName}",
                    ShowXpPopup = false
                });

                var result = GatheringRewardProcessor.Process(context);

                if (EnableDebugLogging)
                {
                    Debug.Log(
                        $"[Thieving] Processed reward {rewardName} x{reward.quantity}. Success: {result.Success}, InventoryFull: {result.InventoryFull}.",
                        this);
                }

                if (result.InventoryFull)
                {
                    inventoryBlocked = true;

                    if (floatingTextAnchor != null)
                    {
                        bool failureDisplayed = GatheringFloatingTextService.TryShowAtAnchor(FailureFloatingText, floatingTextAnchor);
                        LogFloatingTextAttempt(
                            "RewardInventoryFull",
                            FailureFloatingText,
                            floatingTextAnchor,
                            failureDisplayed,
                            worldPosition);
                    }

                    ChatService.Instance?.PublishGameMessage(FailureFloatingText);

                    if (EnableDebugLogging)
                    {
                        Debug.Log("[Thieving] Inventory full encountered while processing rewards. Aborting remaining rewards.", this);
                    }

                    break;
                }

                if (result.Success)
                    anyRewardSucceeded = true;
            }

            if (!anyRewardSucceeded)
            {
                if (EnableDebugLogging)
                {
                    string reason = inventoryBlocked ? "inventory was full" : "no valid rewards were generated";
                    Debug.Log($"[Thieving] Skipping XP grant because {reason}.", this);
                }

                return;
            }

            if (xp > 0f && skillManager != null)
            {
                if (EnableDebugLogging)
                {
                    Debug.Log($"[Thieving] Granting {xp} Thieving XP for the successful attempt.", this);
                }

                int previousLevel = skillManager.GetLevel(SkillType.Thieving);
                int newLevel = skillManager.AddXP(SkillType.Thieving, xp);
                if (EnableDebugLogging)
                {
                    Debug.Log(
                        $"[Thieving] Queueing XP popup for {xp} XP at anchor {DescribeAnchor(floatingTextAnchor)} with world position {worldPosition}.",
                        this);
                }

                GatheringFloatingTextService.QueueDelayedXpPopup(Mathf.RoundToInt(xp), floatingTextAnchor, worldPosition, 1f);

                if (newLevel > previousLevel)
                {
                    string levelMessage = $"Thieving level {newLevel}";
                    bool levelDisplayed = GatheringFloatingTextService.TryShowAtAnchor(levelMessage, floatingTextAnchor);
                    LogFloatingTextAttempt(
                        "LevelUp",
                        levelMessage,
                        floatingTextAnchor,
                        levelDisplayed,
                        worldPosition);
                    LevelledUp?.Invoke(newLevel);
                }
            }
            else if (EnableDebugLogging)
            {
                Debug.Log("[Thieving] XP grant skipped because XP value was non-positive or SkillManager was missing.", this);
            }
        }

        private void LogFloatingTextAttempt(
            string context,
            string message,
            Transform anchor,
            bool displayed,
            Vector3 worldPosition)
        {
            if (!EnableDebugLogging)
                return;

            Debug.Log(
                $"[Thieving] Floating text attempt '{context}' -> message='{message}' displayed={displayed} anchor={DescribeAnchor(anchor)} worldPosition={worldPosition}.",
                this);
        }

        private static string DescribeAnchor(Transform anchor)
        {
            if (anchor == null)
                return "null";

            Vector3 position = anchor.position;
            return $"{anchor.name} (InstanceID {anchor.GetInstanceID()}, position {position})";
        }

        private List<(ItemData item, int quantity)> ResolveLootRolls(
            Vector2Int coinRange,
            IReadOnlyList<ThievingLootTableEntry> lootTable,
            int rolls)
        {
            var rewards = new List<(ItemData item, int quantity)>();

            ItemData coinItem = EnsureCoinItem();

            if (coinItem != null && (coinRange.x > 0 || coinRange.y > 0))
            {
                int min = Mathf.Max(0, coinRange.x);
                int max = Mathf.Max(min, coinRange.y);
                int amount = Random.Range(min, max + 1);
                if (amount > 0)
                    rewards.Add((coinItem, amount));
            }

            if (lootTable == null || lootTable.Count == 0 || rolls <= 0)
                return rewards;

            var chanceEntries = new List<(ThievingLootTableEntry entry, ItemData item, float clampedChance)>();
            float totalChance = 0f;

            // Guaranteed entries are added once before percentage-based rolls.
            for (int i = 0; i < lootTable.Count; i++)
            {
                var entry = lootTable[i];
                if (entry.guaranteed)
                {
                    var item = ItemDatabase.GetItem(entry.itemId);
                    if (item == null)
                    {
                        if (EnableDebugLogging)
                        {
                            Debug.Log($"[Thieving] Skipping guaranteed loot entry '{entry.itemId}' because the ItemDatabase lookup failed.", this);
                        }

                        continue;
                    }

                    int quantity = ResolveQuantity(entry.quantityRange);
                    if (quantity <= 0)
                    {
                        if (EnableDebugLogging)
                        {
                            Debug.Log($"[Thieving] Guaranteed loot entry '{entry.itemId}' resolved to a non-positive quantity ({quantity}) and was skipped.", this);
                        }

                        continue;
                    }

                    rewards.Add((item, quantity));

                    if (EnableDebugLogging)
                    {
                        string guaranteedName = !string.IsNullOrEmpty(item.itemName) ? item.itemName : item.name;
                        Debug.Log($"[Thieving] Added guaranteed loot '{guaranteedName}' x{quantity}.", this);
                    }

                    continue;
                }

                float clampedChance = Mathf.Clamp(entry.dropChancePercent, 0f, 100f);
                if (clampedChance <= 0f)
                    continue;

                var nonGuaranteedItem = ItemDatabase.GetItem(entry.itemId);
                if (nonGuaranteedItem == null)
                {
                    if (EnableDebugLogging)
                    {
                        Debug.Log($"[Thieving] Skipping loot entry '{entry.itemId}' because the ItemDatabase lookup failed.", this);
                    }

                    continue;
                }

                chanceEntries.Add((entry, nonGuaranteedItem, clampedChance));
                totalChance += clampedChance;
            }

            if (chanceEntries.Count == 0)
                return rewards;

            if (EnableDebugLogging)
            {
                float clampedTotal = Mathf.Min(100f, totalChance);
                Debug.Log(
                    $"[Thieving] Prepared {chanceEntries.Count} loot entries totalling {clampedTotal:F2}% chance (raw {totalChance:F2}%).",
                    this);
            }

            for (int r = 0; r < rolls; r++)
            {
                float rollValue = Random.Range(0f, 100f);
                float cumulativeChance = 0f;
                bool awarded = false;

                for (int i = 0; i < chanceEntries.Count; i++)
                {
                    var candidate = chanceEntries[i];
                    cumulativeChance += candidate.clampedChance;

                    if (rollValue > cumulativeChance)
                        continue;

                    int quantity = ResolveQuantity(candidate.entry.quantityRange);
                    if (quantity <= 0)
                    {
                        if (EnableDebugLogging)
                        {
                            Debug.Log(
                                $"[Thieving] Loot roll {r + 1}/{rolls}: rolled {rollValue:F2}% and hit '{candidate.entry.itemId}' but the resolved quantity ({quantity}) was non-positive.",
                                this);
                        }

                        awarded = true;
                        break;
                    }

                    rewards.Add((candidate.item, quantity));

                    if (EnableDebugLogging)
                    {
                        string candidateName = !string.IsNullOrEmpty(candidate.item.itemName)
                            ? candidate.item.itemName
                            : candidate.item.name;
                        Debug.Log(
                            $"[Thieving] Loot roll {r + 1}/{rolls}: rolled {rollValue:F2}% -> '{candidateName}' x{quantity} (threshold {cumulativeChance:F2}%).",
                            this);
                    }

                    awarded = true;
                    break;
                }

                if (!awarded && EnableDebugLogging)
                {
                    float effectiveTotalChance = Mathf.Min(100f, totalChance);
                    Debug.Log(
                        $"[Thieving] Loot roll {r + 1}/{rolls}: rolled {rollValue:F2}% with total configured chance {effectiveTotalChance:F2}% -> no drop.",
                        this);
                }
            }

            return rewards;
        }

        private static int ResolveQuantity(Vector2Int range)
        {
            int min = range.x <= 0 ? 1 : range.x;
            int max = range.y <= 0 ? min : range.y;
            return Random.Range(min, max + 1);
        }

        private bool EvaluateRogueOutfitBonus()
        {
            if (rogueOutfitDefinition == null || rogueOutfitProgress == null)
                return false;

            var pieces = rogueOutfitDefinition.PieceItemIds;
            if (pieces == null || pieces.Count == 0)
                return false;

            if (rogueOutfitProgress.owned == null)
                return false;

            foreach (var piece in pieces)
            {
                if (string.IsNullOrWhiteSpace(piece))
                    continue;
                if (!rogueOutfitProgress.owned.Contains(piece))
                    return false;
            }

            return true;
        }

        private bool CanAcceptNpcLoot(ThievingNpcDefinition definition, out string failureMessage)
        {
            return CanAcceptPotentialLoot(definition.CoinRange, definition.LootTable, out failureMessage);
        }

        internal bool CanAcceptObjectLoot(ThievingObjectNode node, out string failureMessage)
        {
            if (node == null || node.Definition == null)
            {
                failureMessage = "There is nothing to steal.";
                return false;
            }

            return CanAcceptPotentialLoot(node.Definition.CoinRange, node.Definition.LootTable, out failureMessage);
        }

        private bool CanAcceptPotentialLoot(Vector2Int coinRange, IReadOnlyList<ThievingLootTableEntry> lootTable, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (inventory == null)
            {
                failureMessage = "Inventory missing.";
                return false;
            }

            ItemData coinItem = EnsureCoinItem();

            if (coinItem != null && (coinRange.x > 0 || coinRange.y > 0))
            {
                int min = Mathf.Max(1, coinRange.x);
                if (!inventory.CanAddItem(coinItem, min))
                {
                    failureMessage = "Your inventory is full";
                    return false;
                }
            }

            if (lootTable != null)
            {
                for (int i = 0; i < lootTable.Count; i++)
                {
                    var entry = lootTable[i];
                    if (!entry.guaranteed)
                        continue;

                    var item = ItemDatabase.GetItem(entry.itemId);
                    if (item == null)
                        continue;

                    int quantity = Mathf.Max(1, entry.quantityRange.x > 0 ? entry.quantityRange.x : entry.quantityRange.y);
                    if (!inventory.CanAddItem(item, quantity))
                    {
                        failureMessage = "Your inventory is full";
                        return false;
                    }
                }
            }

            return true;
        }

        private ItemData EnsureCoinItem()
        {
            if (cachedCoinItem == null)
                cachedCoinItem = ItemDatabase.GetItem(CoinsItemId);
            return cachedCoinItem;
        }
    }
}
