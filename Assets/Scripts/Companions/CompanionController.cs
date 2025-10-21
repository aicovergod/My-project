using System;
using Combat;
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

        /// <summary>Exposes the runtime skill manager used for stats and combat calculations.</summary>
        public SkillManager SkillManager => skillManager;

        /// <summary>Provides access to the configured inventory wrapper.</summary>
        public CompanionInventory Inventory => companionInventory;

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
        }

        /// <summary>
        /// Issues a direct attack command, respecting the pet combat controller's targeting rules.
        /// </summary>
        public void CommandAttack(CombatTarget target)
        {
            combatController?.CommandAttack(target, true);
        }

        /// <summary>Invoked when the companion should be hidden. Closes UI and disables the object.</summary>
        public void HandleStoreRequest()
        {
            companionInventory?.ForceClosed();
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

        private void OnDestroy()
        {
            if (skillManager != null)
                skillManager.LevelChanged -= OnSkillLevelChanged;

            if (companionInventory != null)
                companionInventory.VisibilityChanged -= OnInventoryVisibilityChanged;

            Despawned?.Invoke(this);
        }
    }
}
