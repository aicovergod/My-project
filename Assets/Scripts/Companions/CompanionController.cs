/// Feature: Added companion pickup routing through the shared path mover system.
using System;
using System.Collections;
using System.Reflection;
using Combat;
using Inventory;
using Inventory.GroundItems;
using MyGame.Drops;
using Pets;
using Skills;
using UI.Chat;
using UnityEngine;
using Util;

namespace Companions
{
    /// <summary>
    /// Coordinates the components that make up the companion entity, bridging follower movement,
    /// combat overrides, inventory access, and skill progression.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionController : MonoBehaviour
    {
        /// <summary>Runtime skill manager feeding combat calculations and stats UI.</summary>
        private SkillManager skillManager;

        /// <summary>In-memory save bridge that stores XP between play sessions.</summary>
        private CompanionSkillMemorySave skillSave;

        /// <summary>Inventory wrapper that builds the companion backpack UI.</summary>
        [SerializeField]
        [Tooltip("Inventory wrapper responsible for storing collected drops. Auto-created when missing.")]
        private CompanionInventory companionInventory;

        /// <summary>Controller that manages mining-specific behaviour for the companion.</summary>
        private CompanionMiningController miningController;

        /// <summary>Equipment component responsible for the companion gear window and state.</summary>
        private CompanionEquipment companionEquipment;

        /// <summary>Bridges pet combat calculations so the companion uses its own stats.</summary>
        private CompanionCombatBridge combatBridge;

        /// <summary>Adapter that enables ranged projectiles for the companion.</summary>
        private CompanionRangedCombatController rangedCombatController;

        /// <summary>Follower logic that keeps the companion next to the player.</summary>
        private PetFollower follower;

        /// <summary>Underlying pet combat controller reused for attack routines.</summary>
        private PetCombatController combatController;

        /// <summary>Tracks per-skill cooldown timers for gathering command throttling.</summary>
        private CompanionSkillCooldownTracker skillCooldownTracker;

        [Header("Pickup Commands")]
        [SerializeField]
        [Tooltip("Distance from the target drop before the companion stops to collect it.")]
        private float pickupStopDistance = 0.35f;

        [SerializeField]
        [Tooltip("Waypoint tolerance supplied to the path mover while approaching drops.")]
        private float pickupWaypointTolerance = 0.075f;

        [SerializeField]
        [Tooltip("Distance change required before the pickup path requests a fresh solution.")]
        private float pickupRepathDistance = 0.6f;

        [SerializeField]
        [Tooltip("Multiplier applied to the companion's follow speed while collecting drops.")]
        private float pickupMoveSpeedMultiplier = 1f;

        [SerializeField]
        [Tooltip("Seconds without progress before the pickup command gives up as obstructed.")]
        private float pickupStuckTimeoutSeconds = 2.5f;

        private Coroutine pickupRoutine;
        private PetPathMover pathMover;
        private Rigidbody2D body2D;
        private PetSpriteAnimator spriteAnimator;
        private bool followerDisabledByPickup;
        private WaitForFixedUpdate pickupFixedUpdateYield;

        private static readonly string[] CompanionInventoryFullResponses =
        {
            "My pack is full, I can't carry that.",
            "No more space—sort your stash!",
            "I need to drop something before grabbing that."
        };

        /// <summary>Raised when a skill level changes so the manager can refresh combat level text.</summary>
        public event Action<SkillType, int> SkillLevelChanged;

        /// <summary>Raised whenever the companion inventory opens or closes.</summary>
        public event Action<bool> InventoryVisibilityChanged;

        /// <summary>Raised when the controller is destroyed so the manager can clear cached state.</summary>
        public event Action<CompanionController> Despawned;

        /// <summary>Raised whenever the companion equipment window opens or closes.</summary>
        public event Action<bool> EquipmentVisibilityChanged;

        /// <summary>Exposes the runtime skill manager used for stats and combat calculations.</summary>
        public SkillManager SkillManager => skillManager;

        /// <summary>Provides access to the configured inventory wrapper.</summary>
        public CompanionInventory Inventory => companionInventory;

        /// <summary>Exposes the mining controller responsible for companion gathering commands.</summary>
        public CompanionMiningController MiningController => miningController;

        /// <summary>Provides access to the equipment component configured for the companion.</summary>
        public CompanionEquipment Equipment => companionEquipment;

        /// <summary>Provides access to the cooldown tracker used for skill command throttling.</summary>
        public CompanionSkillCooldownTracker SkillCooldowns => skillCooldownTracker;

        /// <summary>
        /// Indicates whether the companion has an active combat controller capable of fighting.
        /// </summary>
        public bool CanFight => combatController != null && combatController.CanFight;

        /// <summary>Pool of combat skills eligible for melee XP rolls.</summary>
        private static readonly SkillType[] MeleeXpSkills =
        {
            SkillType.Attack,
            SkillType.Strength,
            SkillType.Defence
        };

        /// <summary>
        /// Configures the companion by wiring the follower, skill manager, combat overrides, and inventory.
        /// </summary>
        /// <param name="player">Player transform used for follow behaviour.</param>
        public void Initialise(Transform player)
        {
            follower = GetComponent<PetFollower>();
            combatController = GetComponent<PetCombatController>();

            skillCooldownTracker = GetComponent<CompanionSkillCooldownTracker>();
            if (skillCooldownTracker == null)
                skillCooldownTracker = gameObject.AddComponent<CompanionSkillCooldownTracker>();

            ConfigureSkills(player);
            ConfigureInventory(player);
            ConfigureEquipment();
            ConfigureMining(player);
            ConfigureCombat();
            combatController?.BindCompanionController(this);
            ConfigurePickupMovementHelpers();
            RebindPlayer(player);
        }

        /// <summary>
        /// Rebinds the follower to the supplied player transform so the companion tracks the new instance
        /// after scene loads or respawns.
        /// </summary>
        /// <param name="player">Player transform to follow.</param>
        public void RebindPlayer(Transform player)
        {
            if (follower == null)
                follower = GetComponent<PetFollower>();

            if (follower != null)
                follower.SetPlayer(player);

            if (miningController != null)
                miningController.RebindPlayer(player);
        }

        /// <summary>
        /// Issues a direct attack command, respecting the pet combat controller's targeting rules.
        /// </summary>
        public void CommandAttack(CombatTarget target)
        {
            CancelActivePickupRoutine();
            // Cancel any active mining routines so direct attack orders stop both single-rock and area sweeps.
            miningController?.CancelMining(true);
            combatController?.CommandAttack(target, true);
        }

        /// <summary>
        /// Directs the companion to collect the supplied world drop using the custom pathing stack.
        /// </summary>
        /// <param name="targetDrop">Drop the companion should attempt to collect.</param>
        public void CommandPickup(WorldDrop targetDrop)
        {
            if (targetDrop == null)
                return;

            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                return;

            if (!targetDrop.IsAvailable)
                return;

            CancelActivePickupRoutine();
            miningController?.CancelMining(true);

            ConfigurePickupMovementHelpers();
            pickupRoutine = StartCoroutine(PickupRoutine(targetDrop));
        }

        /// <summary>Invoked when the companion should be hidden. Closes UI and disables the object.</summary>
        public void HandleStoreRequest()
        {
            companionInventory?.ForceClosed();
            companionEquipment?.ForceClosed();
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        /// <summary>Invoked when the companion should reappear beside the player.</summary>
        public void HandleSummonRequest()
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            var player = GameObject.FindGameObjectWithTag("Player");
            RebindPlayer(player != null ? player.transform : null);
        }

        /// <summary>
        /// Toggles the companion inventory UI and reports the resulting visibility state.
        /// </summary>
        public bool ToggleInventory()
        {
            bool opened = companionInventory != null && companionInventory.ToggleInventory();
            InventoryVisibilityChanged?.Invoke(opened);
            return opened;
        }

        /// <summary>
        /// Toggles the companion equipment UI and reports the resulting visibility.
        /// </summary>
        public bool ToggleEquipment()
        {
            bool opened = companionEquipment != null && companionEquipment.ToggleEquipment();
            return opened;
        }

        /// <summary>Indicates whether the equipment window is currently visible.</summary>
        public bool IsEquipmentVisible => companionEquipment != null && companionEquipment.IsOpen;

        /// <summary>
        /// Attempts to equip an entry removed from the player inventory into the companion gear slots.
        /// </summary>
        public CompanionEquipAttemptResult TryEquipFromPlayerInventory(InventoryEntry entry, Inventory.Inventory playerInventory)
        {
            if (companionEquipment == null)
                return CompanionEquipAttemptResult.NotHandled;

            return companionEquipment.TryEquipFromPlayerInventory(entry, playerInventory);
        }

        /// <summary>
        /// Routes combat XP to the companion using the same distribution formulas as the player controller.
        /// </summary>
        public void AwardCombatXp(int damage, CombatStyle style, DamageType type)
        {
            if (damage <= 0 || skillManager == null)
                return;

            float hitpointsXp = damage * 1.33f;
            skillManager.AddXP(SkillType.Hitpoints, hitpointsXp);
            if (CompanionManager.EnableDebugLogging)
                Debug.Log($"[Companion XP] Awarded {hitpointsXp:0.##} Hitpoints XP from {damage} damage ({type}).");

            if (type == DamageType.Magic)
            {
                float magicXp = 4f * damage;
                skillManager.AddXP(SkillType.Magic, magicXp);
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log($"[Companion XP] Awarded {magicXp:0.##} Magic XP from {damage} magic damage.");
                return;
            }

            if (type == DamageType.Ranged)
            {
                float total = 4f * damage;
                switch (style)
                {
                    case CombatStyle.Defensive:
                    case CombatStyle.Controlled:
                    case CombatStyle.Longrange:
                        float split = total * 0.5f;
                        skillManager.AddXP(SkillType.Ranged, split);
                        skillManager.AddXP(SkillType.Defence, split);
                        if (CompanionManager.EnableDebugLogging)
                            Debug.Log($"[Companion XP] Split ranged XP ({style}) -> {split:0.##} Ranged / {split:0.##} Defence from {damage} damage.");
                        break;
                    default:
                        skillManager.AddXP(SkillType.Ranged, total);
                        if (CompanionManager.EnableDebugLogging)
                            Debug.Log($"[Companion XP] Awarded {total:0.##} Ranged XP from {damage} ranged damage using {style} style.");
                        break;
                }

                return;
            }

            if (type == DamageType.Melee)
            {
                float combatXp = 4f * damage;
                int selectedIndex = UnityEngine.Random.Range(0, MeleeXpSkills.Length);
                SkillType awardedSkill = MeleeXpSkills[selectedIndex];
                skillManager.AddXP(awardedSkill, combatXp);
                if (CompanionManager.EnableDebugLogging)
                    Debug.Log($"[Companion XP] Random melee roll awarded {combatXp:0.##} XP to {awardedSkill} from {damage} damage (style {style}).");
                return;
            }

            switch (style)
            {
                case CombatStyle.Accurate:
                    float accurateXp = 4f * damage;
                    skillManager.AddXP(SkillType.Attack, accurateXp);
                    if (CompanionManager.EnableDebugLogging)
                        Debug.Log($"[Companion XP] Awarded {accurateXp:0.##} Attack XP from {damage} damage via Accurate style.");
                    break;
                case CombatStyle.Aggressive:
                    float aggressiveXp = 4f * damage;
                    skillManager.AddXP(SkillType.Strength, aggressiveXp);
                    if (CompanionManager.EnableDebugLogging)
                        Debug.Log($"[Companion XP] Awarded {aggressiveXp:0.##} Strength XP from {damage} damage via Aggressive style.");
                    break;
                case CombatStyle.Defensive:
                    float defensiveXp = 4f * damage;
                    skillManager.AddXP(SkillType.Defence, defensiveXp);
                    if (CompanionManager.EnableDebugLogging)
                        Debug.Log($"[Companion XP] Awarded {defensiveXp:0.##} Defence XP from {damage} damage via Defensive style.");
                    break;
                case CombatStyle.Controlled:
                    float total = 4f * damage;
                    int share = Mathf.FloorToInt(total / 3f);
                    int remainder = Mathf.RoundToInt(total - share * 3);
                    skillManager.AddXP(SkillType.Attack, share);
                    skillManager.AddXP(SkillType.Strength, share);
                    skillManager.AddXP(SkillType.Defence, share + remainder);
                    if (CompanionManager.EnableDebugLogging)
                        Debug.Log($"[Companion XP] Controlled style awarded {share} Attack, {share} Strength, {share + remainder} Defence XP from {damage} damage.");
                    break;
            }
        }

        private void ConfigureSkills(Transform player)
        {
            skillSave = gameObject.AddComponent<CompanionSkillMemorySave>();
            skillManager = gameObject.AddComponent<SkillManager>();

            SkillManager playerSkills = player != null ? player.GetComponent<SkillManager>() : null;
            var xpTable = playerSkills != null ? playerSkills.GetXpTable() : null;
            skillSave.ConfigureBaseline(xpTable);
            skillManager.ConfigureRuntime(xpTable, skillSave);
            skillManager.LevelChanged += OnSkillLevelChanged;
            skillManager.Load();

            // Ensure the companion starts with 10 hitpoints even if the source XP table is missing.
            skillManager.DebugSetLevel(SkillType.Hitpoints, 10);
        }

        private void ConfigureInventory(Transform player)
        {
            if (companionInventory == null)
                companionInventory = GetComponent<CompanionInventory>();

            if (companionInventory == null)
                companionInventory = gameObject.AddComponent<CompanionInventory>();

            companionInventory.Initialise();
            companionInventory.VisibilityChanged += OnInventoryVisibilityChanged;
        }

        /// <summary>
        /// Configures the equipment component so the companion can manage its own gear window.
        /// </summary>
        private void ConfigureEquipment()
        {
            companionEquipment = GetComponent<CompanionEquipment>();
            if (companionEquipment == null)
                companionEquipment = gameObject.AddComponent<CompanionEquipment>();
            companionEquipment.Initialise(companionInventory, skillManager);
            companionEquipment.VisibilityChanged += OnEquipmentVisibilityChanged;
            companionEquipment.ForceClosed();
        }

        private void ConfigureMining(Transform player)
        {
            miningController = gameObject.AddComponent<CompanionMiningController>();
            miningController.Initialise(this, skillManager, companionInventory, player, skillCooldownTracker);
        }

        private void ConfigureCombat()
        {
            combatBridge = gameObject.AddComponent<CompanionCombatBridge>();
            combatBridge.Initialise(this, skillManager);

            rangedCombatController = GetComponent<CompanionRangedCombatController>() ?? gameObject.AddComponent<CompanionRangedCombatController>();
            var floatingText = GetComponent<PetFloatingTextController>() ?? GetComponentInChildren<PetFloatingTextController>();
            Transform floatingAnchor = floatingText != null ? floatingText.FloatingTextAnchor : transform;
            GroundItemSpawner spawner = FindFirstObjectByType<GroundItemSpawner>();
            rangedCombatController.Initialise(combatController, companionEquipment, companionInventory, floatingAnchor, spawner);
        }

        /// <summary>
        /// Resolves the helper components used while issuing directed pickup commands.
        /// </summary>
        private void ConfigurePickupMovementHelpers()
        {
            pathMover ??= GetComponent<PetPathMover>();
            body2D ??= GetComponent<Rigidbody2D>();
            spriteAnimator ??= GetComponent<PetSpriteAnimator>() ?? GetComponentInChildren<PetSpriteAnimator>();
        }

        /// <summary>
        /// Coroutine that steers the companion toward the requested drop using the shared path mover.
        /// </summary>
        private IEnumerator PickupRoutine(WorldDrop targetDrop)
        {
            if (targetDrop == null)
                yield break;

            ConfigurePickupMovementHelpers();
            DisableFollowerForPickup();

            pathMover?.ResetAttackTracking();
            pathMover?.ResetCachedVelocity();

            float lastProgressSample = Time.unscaledTime;
            float lastDistance = float.MaxValue;

            try
            {
                while (enabled)
                {
                    if (targetDrop == null || !targetDrop.IsAvailable)
                        break;

                    Transform pickupTransform = targetDrop.PickupTransform;
                    if (pickupTransform == null)
                        break;

                    float stopDistance = Mathf.Max(0.05f, pickupStopDistance);
                    Vector3 targetPosition = pickupTransform.position;
                    Vector3 currentPosition = body2D != null ? (Vector3)body2D.position : transform.position;
                    float distance = Vector2.Distance(currentPosition, targetPosition);

                    if (distance <= stopDistance)
                        break;

                    if (distance <= lastDistance - 0.01f)
                    {
                        lastProgressSample = Time.unscaledTime;
                        lastDistance = distance;
                    }
                    else if (Time.unscaledTime - lastProgressSample >= Mathf.Max(0.1f, pickupStuckTimeoutSeconds))
                    {
                        if (CompanionManager.EnableDebugLogging)
                        {
                            Debug.Log("[Companion Pickup] Movement stalled while approaching the drop. Aborting command.", this);
                        }

                        break;
                    }

                    Vector3 nextPosition;
                    Vector2 velocity;
                    bool teleported;

                    if (!TryStepWithNavigation(targetDrop, stopDistance, out nextPosition, out velocity, out teleported))
                    {
                        StepDirectlyTowards(targetPosition, ResolvePickupMoveSpeed(), out nextPosition, out velocity);
                        teleported = false;
                    }
                    else if ((nextPosition - currentPosition).sqrMagnitude <= 0.0001f && velocity.sqrMagnitude <= 0.0001f)
                    {
                        // Navigation is still resolving a path; treat this as progress so the stuck timer does not abort early.
                        lastProgressSample = Time.unscaledTime;
                        lastDistance = distance;
                    }

                    ApplyPickupMovement(nextPosition, velocity, teleported);

                    if (body2D != null)
                    {
                        pickupFixedUpdateYield ??= new WaitForFixedUpdate();
                        yield return pickupFixedUpdateYield;
                    }
                    else
                    {
                        yield return null;
                    }
                }

                if (targetDrop != null && targetDrop.IsAvailable)
                {
                    Transform pickupTransform = targetDrop.PickupTransform;
                    float stopDistance = Mathf.Max(0.05f, pickupStopDistance);
                    if (pickupTransform != null)
                    {
                        Vector3 evaluationPosition = body2D != null ? (Vector3)body2D.position : transform.position;
                        if (Vector2.Distance(evaluationPosition, pickupTransform.position) <= stopDistance + 0.05f)
                        {
                            FacePickup(pickupTransform.position);
                            TryCollectDrop(targetDrop);
                        }
                    }
                }
            }
            finally
            {
                ResetPickupMovementState();
                pickupRoutine = null;
            }
        }

        /// <summary>
        /// Attempts to consume a navigation step via <see cref="PetPathMover"/> while pursuing the drop.
        /// </summary>
        private bool TryStepWithNavigation(
            WorldDrop targetDrop,
            float stopDistance,
            out Vector3 nextPosition,
            out Vector2 velocity,
            out bool teleported)
        {
            nextPosition = transform.position;
            velocity = Vector2.zero;
            teleported = false;

            if (pathMover == null || !pathMover.isActiveAndEnabled)
                return false;

            if (!pathMover.HasActiveNavigationGrid)
                return false;

            float deltaTime = body2D != null
                ? Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon)
                : Mathf.Max(Time.deltaTime, Mathf.Epsilon);

            Vector2 navNext;
            Vector2 navVelocity;
            bool navTeleported;
            bool goalUnreachable;

            bool stepped = pathMover.TryStepAttack(
                deltaTime,
                ResolvePickupMoveSpeed(),
                stopDistance,
                Mathf.Max(0.01f, pickupWaypointTolerance),
                () => targetDrop != null && targetDrop.PickupTransform != null
                    ? (Vector2)targetDrop.PickupTransform.position
                    : (Vector2)transform.position,
                Mathf.Max(stopDistance * 0.75f, pickupRepathDistance),
                float.PositiveInfinity,
                out navNext,
                out navVelocity,
                out navTeleported,
                out goalUnreachable);

            if (goalUnreachable)
            {
                if (CompanionManager.EnableDebugLogging)
                {
                    Debug.Log("[Companion Pickup] Navigation reported the drop as unreachable. Falling back to direct steering.", this);
                }

                return false;
            }

            if (stepped)
            {
                nextPosition = new Vector3(navNext.x, navNext.y, transform.position.z);
                velocity = navVelocity;
                teleported = navTeleported;
                return true;
            }

            // Navigation is active but has not produced a waypoint yet (likely waiting for a path response).
            // Hold position so the coroutine can wait for the navigation data instead of reverting to direct steering.
            Vector3 currentPosition = body2D != null ? (Vector3)body2D.position : transform.position;
            nextPosition = new Vector3(currentPosition.x, currentPosition.y, transform.position.z);
            velocity = Vector2.zero;
            teleported = false;
            return true;
        }

        /// <summary>
        /// Provides a direct steering fallback when no navigation grid is available.
        /// </summary>
        private void StepDirectlyTowards(Vector3 targetPosition, float moveSpeed, out Vector3 nextPosition, out Vector2 velocity)
        {
            Vector3 currentPosition = body2D != null ? (Vector3)body2D.position : transform.position;
            float deltaTime = body2D != null
                ? Mathf.Max(Time.fixedDeltaTime, Mathf.Epsilon)
                : Mathf.Max(Time.deltaTime, Mathf.Epsilon);

            nextPosition = Vector3.MoveTowards(currentPosition, targetPosition, moveSpeed * deltaTime);
            nextPosition.z = currentPosition.z;

            Vector2 displacement = nextPosition - currentPosition;
            float clampedDelta = Mathf.Max(deltaTime, Mathf.Epsilon);
            velocity = displacement / clampedDelta;
        }

        /// <summary>
        /// Applies the computed movement step and updates sprite feedback.
        /// </summary>
        private void ApplyPickupMovement(Vector3 nextPosition, Vector2 velocity, bool teleported)
        {
            Vector3 currentPosition = body2D != null ? (Vector3)body2D.position : transform.position;
            Vector2 displacement = (Vector2)(nextPosition - currentPosition);

            if (body2D != null)
            {
                if (teleported)
                {
                    body2D.position = nextPosition;
                    body2D.linearVelocity = Vector2.zero;
                }
                else
                {
                    body2D.MovePosition(nextPosition);
                    body2D.linearVelocity = velocity;
                }
            }
            else
            {
                transform.position = nextPosition;
            }

            UpdatePickupMovementVisuals(displacement, teleported ? Vector2.zero : velocity);
        }

        /// <summary>
        /// Refreshes sprite-facing feedback while the companion travels toward the drop.
        /// </summary>
        private void UpdatePickupMovementVisuals(Vector2 displacement, Vector2 velocity)
        {
            if (spriteAnimator != null)
            {
                spriteAnimator.UpdateVisuals(velocity);
            }
        }

        /// <summary>
        /// Temporarily disables the follower so the pickup routine can control movement directly.
        /// </summary>
        private void DisableFollowerForPickup()
        {
            if (follower == null)
                return;

            if (followerDisabledByPickup)
                return;

            if (!follower.enabled)
                return;

            follower.enabled = false;
            followerDisabledByPickup = true;
        }

        /// <summary>
        /// Resets pathing helpers, restores the follower, and clears velocities after a pickup attempt.
        /// </summary>
        private void ResetPickupMovementState()
        {
            pathMover?.ResetAttackTracking();
            pathMover?.ResetFollowTracking();
            pathMover?.ResetCachedVelocity();

            if (body2D != null)
            {
                body2D.linearVelocity = Vector2.zero;
                body2D.angularVelocity = 0f;
            }

            spriteAnimator?.UpdateVisuals(Vector2.zero);

            transform.rotation = Quaternion.identity;

            if (followerDisabledByPickup && follower != null && !follower.enabled)
            {
                follower.enabled = true;
            }

            followerDisabledByPickup = false;
        }

        /// <summary>
        /// Resolves the movement speed used while walking toward drops.
        /// </summary>
        private float ResolvePickupMoveSpeed()
        {
            float baseSpeed = follower != null ? Mathf.Max(0.1f, follower.moveSpeed) : 5f;
            float multiplier = Mathf.Max(0.1f, pickupMoveSpeedMultiplier);
            return Mathf.Max(0.1f, baseSpeed * multiplier);
        }

        /// <summary>
        /// Attempts to add the drop to the companion inventory and despawn the pickup.
        /// </summary>
        private void TryCollectDrop(WorldDrop drop)
        {
            if (drop == null || companionInventory == null)
                return;

            if (!drop.IsAvailable)
                return;

            ItemStack stack = drop.Stack;
            if (!stack.IsValid)
                return;

            bool added = companionInventory.TryAddItem(stack);
            if (added)
            {
                drop.Despawn();
                TryPlayPickupAnimation();
            }
            else
            {
                PostInventoryFullMessage();
            }
        }

        /// <summary>
        /// Cancels any active pickup coroutine and restores the follower/path mover state.
        /// </summary>
        private void CancelActivePickupRoutine()
        {
            if (pickupRoutine != null)
            {
                StopCoroutine(pickupRoutine);
                pickupRoutine = null;
            }

            ResetPickupMovementState();
        }

        /// <summary>
        /// Rotates the companion to face the collected item for a natural pickup motion.
        /// </summary>
        private void FacePickup(Vector3 worldPosition)
        {
            Vector3 direction = worldPosition - transform.position;
            direction.z = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            Vector2 planar = new Vector2(direction.x, direction.y);
            if (spriteAnimator != null)
            {
                Direction8 facing = Direction8Utility.FromVector(planar, allowDiagonals: true, fallback: Direction8.Down);
                spriteAnimator.SetFacing(facing);
                spriteAnimator.UpdateVisuals(Vector2.zero);
            }

            transform.rotation = Quaternion.identity;
        }

        /// <summary>
        /// Posts a random inventory-full chat line to the chatbox.
        /// </summary>
        private void PostInventoryFullMessage()
        {
            if (CompanionInventoryFullResponses == null || CompanionInventoryFullResponses.Length == 0)
                return;

            int index = UnityEngine.Random.Range(0, CompanionInventoryFullResponses.Length);
            ChatboxUI.PostSystemMessage(CompanionInventoryFullResponses[index]);
        }

        /// <summary>
        /// Attempts to trigger the optional pickup animation controller when present.
        /// </summary>
        private void TryPlayPickupAnimation()
        {
            const string controllerTypeName = "Companions.CompanionAnimationController";
            Type controllerType = Type.GetType(controllerTypeName) ??
                                    Type.GetType($"{controllerTypeName}, Assembly-CSharp");
            if (controllerType == null)
                return;

            Component component = GetComponent(controllerType) ?? GetComponentInChildren(controllerType, true);
            if (component == null)
                return;

            MethodInfo playPickup = controllerType.GetMethod("PlayPickup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            playPickup?.Invoke(component, null);
        }

        private void OnSkillLevelChanged(SkillType type, int level)
        {
            SkillLevelChanged?.Invoke(type, level);
        }

        private void OnInventoryVisibilityChanged(bool visible)
        {
            InventoryVisibilityChanged?.Invoke(visible);
        }

        /// <summary>
        /// Relays equipment window visibility so the manager can keep HUD labels in sync.
        /// </summary>
        private void OnEquipmentVisibilityChanged(bool visible)
        {
            EquipmentVisibilityChanged?.Invoke(visible);
        }

        private void OnDestroy()
        {
            CancelActivePickupRoutine();
            miningController?.CancelMining(true);

            if (skillManager != null)
                skillManager.LevelChanged -= OnSkillLevelChanged;

            if (companionInventory != null)
                companionInventory.VisibilityChanged -= OnInventoryVisibilityChanged;

            if (companionEquipment != null)
            {
                companionEquipment.VisibilityChanged -= OnEquipmentVisibilityChanged;
                companionEquipment.ForceClosed();
            }

            combatController?.BindCompanionController(null);
            miningController = null;
            companionEquipment = null;
            Despawned?.Invoke(this);
        }

        private void OnDisable()
        {
            CancelActivePickupRoutine();
        }
    }
}
