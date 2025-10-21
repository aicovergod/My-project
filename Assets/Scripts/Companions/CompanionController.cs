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

            skillManager.AddXP(SkillType.Hitpoints, damage * 1.33f);

            if (type == DamageType.Magic)
            {
                skillManager.AddXP(SkillType.Magic, 4 * damage);
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
                        break;
                    default:
                        skillManager.AddXP(SkillType.Ranged, total);
                        break;
                }

                return;
            }

            switch (style)
            {
                case CombatStyle.Accurate:
                    skillManager.AddXP(SkillType.Attack, 4 * damage);
                    break;
                case CombatStyle.Aggressive:
                    skillManager.AddXP(SkillType.Strength, 4 * damage);
                    break;
                case CombatStyle.Defensive:
                    skillManager.AddXP(SkillType.Defence, 4 * damage);
                    break;
                case CombatStyle.Controlled:
                    float total = 4f * damage;
                    int share = Mathf.FloorToInt(total / 3f);
                    int remainder = Mathf.RoundToInt(total - share * 3);
                    skillManager.AddXP(SkillType.Attack, share);
                    skillManager.AddXP(SkillType.Strength, share);
                    skillManager.AddXP(SkillType.Defence, share + remainder);
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
