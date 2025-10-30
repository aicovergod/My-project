/// Feature: Added companion pickup routing through the shared path mover system.
using System;
using Combat;
using Inventory;
using Inventory.GroundItems;
using MyGame.Drops;
using Pets;
using Skills;
using UnityEngine;
using Companions.Combat;
using Companions.Equipment;
using RuntimeInventory = global::Inventory.Inventory;

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

        /// <summary>Controller that manages fishing-specific behaviour for the companion.</summary>
        private CompanionFishingController fishingController;

        /// <summary>Controller that manages cooking-specific behaviour for the companion.</summary>
        private CompanionCookingController cookingController;

        /// <summary>Controller that manages woodcutting-specific behaviour for the companion.</summary>
        [SerializeField]
        private CompanionWoodcuttingController woodcuttingController;

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

        [SerializeField]
        [Tooltip("Dedicated component that coordinates directed pickup commands.")]
        private CompanionPickupController pickupController;

        private PetPathMover pathMover;
        private Rigidbody2D body2D;
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

        /// <summary>Exposes the fishing controller responsible for companion gathering commands.</summary>
        public CompanionFishingController FishingController => fishingController;

        /// <summary>Exposes the cooking controller responsible for companion commands.</summary>
        public CompanionCookingController CookingController => cookingController;

        /// <summary>Exposes the woodcutting controller responsible for companion gathering commands.</summary>
        public CompanionWoodcuttingController WoodcuttingController => woodcuttingController;

        /// <summary>Provides access to the equipment component configured for the companion.</summary>
        public CompanionEquipment Equipment => companionEquipment;

        /// <summary>Provides access to the dedicated pickup controller component.</summary>
        public CompanionPickupController PickupController => pickupController;

        /// <summary>
        /// Indicates whether any attached subsystem currently holds the follower disabled so combat
        /// logic can avoid re-enabling it prematurely.
        /// </summary>
        public bool HasActiveFollowerHold()
        {
            if (pickupController != null && pickupController.HasActiveFollowerHold)
                return true;

            if (cookingController != null && cookingController.HasActiveFollowerHold)
                return true;

            if (fishingController != null && fishingController.HasActiveFollowerHold)
                return true;

            if (miningController != null && miningController.HasActiveFollowerHold)
                return true;

            if (woodcuttingController != null && woodcuttingController.HasActiveFollowerHold)
                return true;

            return false;
        }

        /// <summary>Provides access to the cooldown tracker used for skill command throttling.</summary>
        public CompanionSkillCooldownTracker SkillCooldowns => skillCooldownTracker;

        /// <summary>True while the combat controller currently has an active target engaged.</summary>
        public bool IsInCombat => combatController != null && combatController.HasActiveTarget;

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
            ConfigureFishing(player);
            ConfigureCooking(player);
            ConfigureWoodcutting(player);
            ConfigureCombat();
            combatController?.BindCompanionController(this);
            ConfigurePickupController();
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

            if (fishingController != null)
                fishingController.RebindPlayer(player);

            if (cookingController != null)
                cookingController.RebindPlayer(player);

            if (woodcuttingController != null)
                woodcuttingController.RebindPlayer(player);
        }

        /// <summary>
        /// Attempts to resolve the most recent heading used while moving the companion.
        /// This supports systems that need a direction even when the Rigidbody velocity is near zero
        /// due to MovePosition integration used by <see cref="PetFollower"/>.
        /// </summary>
        /// <param name="heading">Normalized heading describing the last meaningful movement direction.</param>
        /// <returns><c>true</c> when heading data could be recovered.</returns>
        public bool TryGetMovementHeading(out Vector2 heading)
        {
            const float headingEpsilon = 0.0001f;

            body2D ??= GetComponent<Rigidbody2D>();
            if (body2D != null)
            {
                Vector2 bodyVelocity = body2D.linearVelocity;
                if (bodyVelocity.sqrMagnitude > headingEpsilon)
                {
                    heading = bodyVelocity.normalized;
                    return true;
                }
            }

            pathMover ??= GetComponent<PetPathMover>();
            if (pathMover != null)
            {
                Vector2 moverVelocity = pathMover.CurrentVelocity;
                if (moverVelocity.sqrMagnitude > headingEpsilon)
                {
                    heading = moverVelocity.normalized;
                    return true;
                }
            }

            follower ??= GetComponent<PetFollower>();
            if (follower != null)
            {
                Vector2 followerHeading = follower.LastKnownHeading;
                if (followerHeading.sqrMagnitude > headingEpsilon)
                {
                    heading = followerHeading.normalized;
                    return true;
                }
            }

            heading = Vector2.zero;
            return false;
        }

        /// <summary>
        /// Issues a direct attack command, respecting the pet combat controller's targeting rules.
        /// </summary>
        public void CommandAttack(CombatTarget target)
        {
            pickupController?.CancelActivePickup();
            // Cancel any active gathering routines so direct attack orders stop ongoing skill behaviour.
            miningController?.CancelMining(true);
            cookingController?.CancelCooking(true);
            woodcuttingController?.CancelWoodcutting(true);
            combatController?.CommandAttack(target, true);
        }

        /// <summary>
        /// Cancels the current combat engagement so the companion returns to follow behaviour immediately.
        /// </summary>
        public void CancelActiveCombat()
        {
            combatController?.CancelCombat();
        }

        /// <summary>
        /// Directs the companion to collect the supplied world drop using the custom pathing stack.
        /// </summary>
        /// <param name="targetDrop">Drop the companion should attempt to collect.</param>
        public void CommandPickup(WorldDrop targetDrop)
        {
            if (pickupController == null)
                return;

            if (targetDrop == null)
                return;

            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                return;

            if (!targetDrop.IsAvailable)
                return;

            pickupController.CancelActivePickup();
            miningController?.CancelMining(true);
            cookingController?.CancelCooking(true);
            woodcuttingController?.CancelWoodcutting(true);
            pickupController.CommandPickup(targetDrop);
        }

        /// <summary>Invoked when the companion should be hidden. Closes UI and disables the object.</summary>
        public void HandleStoreRequest()
        {
            pickupController?.CancelActivePickup();
            // Persist the companion's runtime inventory before closing any associated UI panels.
            var runtimeInventory = companionInventory?.InventoryComponent;
            if (runtimeInventory != null)
                runtimeInventory.Save();

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
        public CompanionEquipAttemptResult TryEquipFromPlayerInventory(InventoryEntry entry, RuntimeInventory playerInventory)
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
            miningController = miningController != null
                ? miningController
                : GetComponent<CompanionMiningController>();
            if (miningController == null)
                miningController = gameObject.AddComponent<CompanionMiningController>();
            miningController.Initialise(this, skillManager, companionInventory, player, skillCooldownTracker);
        }

        private void ConfigureFishing(Transform player)
        {
            fishingController = fishingController != null
                ? fishingController
                : GetComponent<CompanionFishingController>();
            if (fishingController == null)
                fishingController = gameObject.AddComponent<CompanionFishingController>();
            fishingController.Initialise(this, skillManager, companionInventory, player, skillCooldownTracker);
        }

        private void ConfigureCooking(Transform player)
        {
            cookingController = cookingController != null
                ? cookingController
                : GetComponent<CompanionCookingController>();
            if (cookingController == null)
                cookingController = gameObject.AddComponent<CompanionCookingController>();
            cookingController.Initialise(this, skillManager, companionInventory, companionEquipment, player, skillCooldownTracker);
        }

        private void ConfigureWoodcutting(Transform player)
        {
            woodcuttingController = woodcuttingController != null
                ? woodcuttingController
                : GetComponent<CompanionWoodcuttingController>();
            if (woodcuttingController == null)
                woodcuttingController = gameObject.AddComponent<CompanionWoodcuttingController>();
            woodcuttingController.Initialise(this, skillManager, companionInventory, player, skillCooldownTracker);
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
        /// Ensures the dedicated pickup controller exists and is initialised for commands.
        /// </summary>
        private void ConfigurePickupController()
        {
            pickupController = pickupController != null
                ? pickupController
                : GetComponent<CompanionPickupController>();

            if (pickupController == null)
                pickupController = gameObject.AddComponent<CompanionPickupController>();

            pickupController.Initialise(this, companionInventory);
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
            pickupController?.CancelActivePickup();
            miningController?.CancelMining(true);
            woodcuttingController?.CancelWoodcutting(true);

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
            woodcuttingController = null;
            companionEquipment = null;
            Despawned?.Invoke(this);
        }

        private void OnDisable()
        {
            pickupController?.CancelActivePickup();
        }
    }
}
