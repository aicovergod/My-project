using System;
using Combat;
using Inventory;
using Pets;
using Skills;
using UnityEngine;

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
        private CompanionInventory companionInventory;

        /// <summary>Controller that manages mining-specific behaviour for the companion.</summary>
        private CompanionMiningController miningController;

        /// <summary>Equipment component responsible for the companion gear window and state.</summary>
        private CompanionEquipment companionEquipment;

        /// <summary>Bridges pet combat calculations so the companion uses its own stats.</summary>
        private CompanionCombatBridge combatBridge;

        /// <summary>Follower logic that keeps the companion next to the player.</summary>
        private PetFollower follower;

        /// <summary>Underlying pet combat controller reused for attack routines.</summary>
        private PetCombatController combatController;

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

            ConfigureSkills(player);
            ConfigureInventory(player);
            ConfigureEquipment();
            ConfigureMining(player);
            ConfigureCombat();
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
            // Cancel any active mining routines so direct attack orders stop both single-rock and area sweeps.
            miningController?.CancelMining(true);
            combatController?.CommandAttack(target, true);
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
            miningController.Initialise(this, skillManager, companionInventory, player);
        }

        private void ConfigureCombat()
        {
            combatBridge = gameObject.AddComponent<CompanionCombatBridge>();
            combatBridge.Initialise(this, skillManager);
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

            miningController = null;
            companionEquipment = null;
            Despawned?.Invoke(this);
        }
    }
}
