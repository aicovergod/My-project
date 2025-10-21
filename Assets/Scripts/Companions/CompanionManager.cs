using System;
using Combat;
using Inventory;
using Pets;
using Skills;
using Skills.Mining;
using UI.Chat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Companions
{
    /// <summary>
    /// Centralised runtime entry point that spawns, tracks, and coordinates the companion entity.
    /// Responsible for wiring combat/skill integration, guard mode behaviour, and context menu actions.
    /// </summary>
    public static class CompanionManager
    {
        /// <summary>Runtime reference to the live controller component created after spawning.</summary>
        private static CompanionController controller;

        /// <summary>Cached GameObject returned by the pet spawner so lifecycle can be managed.</summary>
        private static GameObject companionObject;

        /// <summary>Definition that supplied the current companion spawn (if any).</summary>
        private static PetDefinition activeDefinition;

        /// <summary>HUD instance bound to the companion entry inside the pet level bar.</summary>
        private static PetLevelBarHUD boundHud;

        /// <summary>Tracks whether guard mode is toggled so the player controller can forward commands.</summary>
        private static bool guardModeEnabled;

        /// <summary>True while the backpack UI is open so menu labels stay in sync.</summary>
        private static bool inventoryVisible;

        /// <summary>True when the companion was hidden automatically because a pet spawned.</summary>
        private static bool storedByPet;

        /// <summary>True when the player explicitly issued a Pick Up command.</summary>
        private static bool storedManually;

        /// <summary>Tracks whether the companion was active when the pet pipeline requested a hide.</summary>
        private static bool companionWasActiveBeforePetSpawn;

        /// <summary>Tracks whether the companion equipment window is currently visible.</summary>
        private static bool equipmentVisible;

        /// <summary>Cached handle to the active player inventory so equipment toggles can drive it.</summary>
        private static Inventory.Inventory cachedPlayerInventory;

        /// <summary>Chat line used when the companion cannot store additional resources.</summary>
        internal const string InventoryFullChatLine = "My inventory is full up.";

        /// <summary>Latest combat level computed from the companion's skills.</summary>
        private static int combatLevel = 1;

        /// <summary>Prevents automatic resummoning when a pet temporarily hides the companion.</summary>
        private static bool suppressRestoreAfterPet;

        /// <summary>Raised whenever the companion's computed combat level changes.</summary>
        public static event Action<int> CombatLevelChanged;

        /// <summary>Raised when guard mode toggles so UI surfaces can refresh their labels.</summary>
        public static event Action<bool> GuardModeChanged;

        /// <summary>Raised whenever the companion inventory opens or closes.</summary>
        public static event Action<bool> InventoryVisibilityChanged;

        /// <summary>Raised whenever the companion equipment window opens or closes.</summary>
        public static event Action<bool> EquipmentVisibilityChanged;

        /// <summary>Tracks whether verbose companion debug logging is enabled.</summary>
        private static bool enableDebugLogging;

        /// <summary>
        /// Toggle that allows QA to enable or disable verbose companion debug logging from the AdminF2 menu.
        /// </summary>
        public static bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set
            {
                if (enableDebugLogging == value)
                    return;

                enableDebugLogging = value;
                Debug.Log($"[Companion] Debug logging {(enableDebugLogging ? "enabled" : "disabled")}.");
            }
        }

        /// <summary>True when the companion exists and is currently visible in the world.</summary>
        public static bool HasActiveCompanion => controller != null && controller.gameObject != null && controller.gameObject.activeSelf;

        /// <summary>
        /// Determines whether the provided pet definition matches the currently active companion.
        /// </summary>
        /// <param name="definition">Definition to check against the active companion.</param>
        public static bool IsActiveCompanionDefinition(PetDefinition definition)
        {
            if (definition == null || activeDefinition == null)
                return false;

            if (!HasActiveCompanion || !activeDefinition.spawnAsCompanion)
                return false;

            return DefinitionsMatch(activeDefinition, definition);
        }

        /// <summary>True when the companion has been stored (picked up) but remains available to resummon.</summary>
        public static bool IsStored => storedByPet || storedManually || (controller != null && controller.gameObject != null && !controller.gameObject.activeSelf);

        /// <summary>Latest combat level calculated from the companion skill manager.</summary>
        public static int CombatLevel => combatLevel;

        /// <summary>Whether guard mode is currently toggled for the companion.</summary>
        public static bool GuardModeEnabled => guardModeEnabled;

        /// <summary>Exposes the bound skill manager used for stats UI integration.</summary>
        public static SkillManager CompanionSkills => controller != null ? controller.SkillManager : null;

        /// <summary>Provides access to the configured inventory wrapper.</summary>
        public static CompanionInventory CompanionInventory => controller != null ? controller.Inventory : null;

        /// <summary>Provides access to the configured equipment wrapper.</summary>
        public static CompanionEquipment CompanionEquipment => controller != null ? controller.Equipment : null;

        /// <summary>Exposes the spawned companion object for systems that need the instance handle.</summary>
        public static GameObject CompanionObject => companionObject;

        /// <summary>Ensures the companion spawns after each scene load so it persists across gameplay sessions.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialise()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (controller == null)
                return;

            // Locate the current player transform so the companion follower can resume tracking after
            // additive scene loads or respawns that destroy the original player instance.
            var player = GameObject.FindGameObjectWithTag("Player");
            controller.RebindPlayer(player != null ? player.transform : null);
        }

        /// <summary>
        /// Attempts to spawn the companion next to the current player if one is not already active.
        /// </summary>
        public static void TrySpawnCompanion()
        {
            TrySpawnCompanion(null);
        }

        /// <summary>
        /// Attempts to spawn the companion using the supplied definition when available.
        /// </summary>
        /// <param name="definitionOverride">Definition to use for visuals/combat data. Falls back to runtime asset when null.</param>
        public static void TrySpawnCompanion(PetDefinition definitionOverride)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return;

            var resolvedDefinition = definitionOverride != null
                ? definitionOverride
                : CompanionRuntimeAssets.ResolveDefinition();

            if (resolvedDefinition == null)
                return;

            if (controller != null)
            {
                controller.RebindPlayer(player.transform);

                bool definitionsMatch = DefinitionsMatch(activeDefinition, resolvedDefinition);
                if (definitionsMatch)
                {
                    if (!controller.gameObject.activeSelf)
                        SetStored(false);
                    return;
                }

                TearDownExistingCompanion();
            }

            Vector3 spawnPosition = player.transform.position;
            companionObject = PetSpawner.Spawn(resolvedDefinition, spawnPosition, player.transform);
            if (companionObject == null)
                return;

            GameObject.DontDestroyOnLoad(companionObject);
            CleanupPetSpecificComponents(companionObject);

            controller = companionObject.AddComponent<CompanionController>();
            controller.Initialise(player.transform);
            controller.SkillLevelChanged += HandleSkillLevelChanged;
            controller.InventoryVisibilityChanged += HandleInventoryVisibilityChanged;
            controller.EquipmentVisibilityChanged += HandleEquipmentVisibilityChanged;
            controller.Despawned += HandleControllerDespawned;
            if (companionObject.GetComponent<CompanionClickable>() == null)
                companionObject.AddComponent<CompanionClickable>();

            activeDefinition = resolvedDefinition;
            guardModeEnabled = false;
            inventoryVisible = false;
            equipmentVisible = false;
            storedByPet = false;
            storedManually = false;
            companionWasActiveBeforePetSpawn = false;
            suppressRestoreAfterPet = false;

            UpdateCombatLevel();
            EnsureHud();
            UpdateEquipmentVisibility(false);
        }

        /// <summary>
        /// Determines whether the supplied definition matches the currently active companion definition.
        /// </summary>
        private static bool DefinitionsMatch(PetDefinition current, PetDefinition desired)
        {
            if (current == null || desired == null)
                return false;

            if (ReferenceEquals(current, desired))
                return true;

            return string.Equals(current.id, desired.id, StringComparison.Ordinal);
        }

        /// <summary>
        /// Destroys the existing companion controller so a new definition can be spawned immediately.
        /// </summary>
        private static void TearDownExistingCompanion()
        {
            DespawnCompanion(true);
        }

        /// <summary>
        /// Destroys the active companion instance, optionally clearing the tracked definition.
        /// </summary>
        /// <param name="clearDefinition">True when switching companions, false when temporarily hiding the current one.</param>
        private static void DespawnCompanion(bool clearDefinition)
        {
            if (controller == null && companionObject == null)
            {
                if (clearDefinition)
                {
                    activeDefinition = null;
                    storedByPet = false;
                    storedManually = false;
                    companionWasActiveBeforePetSpawn = false;
                    combatLevel = 1;
                }

                PetLevelBarHUD.DestroyInstance();
                boundHud = null;
                return;
            }

            var objectToDestroy = companionObject;

            if (controller != null)
            {
                controller.SkillLevelChanged -= HandleSkillLevelChanged;
                controller.InventoryVisibilityChanged -= HandleInventoryVisibilityChanged;
                controller.Despawned -= HandleControllerDespawned;
                controller.HandleStoreRequest();
            }

            controller = null;
            companionObject = null;

            guardModeEnabled = false;
            GuardModeChanged?.Invoke(false);

            inventoryVisible = false;
            InventoryVisibilityChanged?.Invoke(false);

            if (clearDefinition)
            {
                activeDefinition = null;
                storedByPet = false;
                storedManually = false;
                companionWasActiveBeforePetSpawn = false;
                combatLevel = 1;
            }

            PetLevelBarHUD.DestroyInstance();
            boundHud = null;

            if (objectToDestroy != null)
                UnityEngine.Object.Destroy(objectToDestroy);
        }

        /// <summary>
        /// Destroys components that belong exclusively to the pet pipeline so the companion can
        /// insert its own inventory/click behaviour.
        /// </summary>
        private static void CleanupPetSpecificComponents(GameObject target)
        {
            if (target == null)
                return;

            var exp = target.GetComponent<PetExperience>();
            if (exp != null)
                UnityEngine.Object.Destroy(exp);

            var storage = target.GetComponent<PetStorage>();
            if (storage != null)
                UnityEngine.Object.Destroy(storage);

            var clickable = target.GetComponent<PetClickable>();
            if (clickable != null)
                UnityEngine.Object.Destroy(clickable);
        }

        /// <summary>
        /// Ensures the shared HUD exists and is bound to the companion instance.
        /// </summary>
        private static void EnsureHud()
        {
            if (controller == null)
                return;

            if (boundHud == null || boundHud.Equals(null))
                boundHud = PetLevelBarHUD.CreateForCompanion();

            boundHud?.BindToCompanion();
        }

        /// <summary>
        /// Unbinds the HUD when destroyed so future spawns can rebuild it without dangling references.
        /// </summary>
        /// <param name="hud">HUD instance that is being torn down.</param>
        internal static void UnbindHud(PetLevelBarHUD hud)
        {
            if (boundHud == hud)
                boundHud = null;
        }

        /// <summary>
        /// Stores or restores the companion, mirroring the Pick Up / Summon menu behaviour.
        /// </summary>
        public static void SetStored(bool stored, bool triggeredByPet = false)
        {
            bool hadActiveCompanion = HasActiveCompanion;

            if (stored)
            {
                if (!hadActiveCompanion)
                {
                    if (!triggeredByPet)
                    {
                        storedByPet = false;
                        companionWasActiveBeforePetSpawn = false;
                    }
                    return;
                }

                if (!triggeredByPet && activeDefinition != null && activeDefinition.pickupItem != null)
                {
                    if (!InventoryBridge.AddItem(activeDefinition.pickupItem, 1))
                        return;
                }

                DespawnCompanion(false);

                storedByPet = triggeredByPet && hadActiveCompanion;
                storedManually = !triggeredByPet && hadActiveCompanion;
                if (!triggeredByPet)
                {
                    companionWasActiveBeforePetSpawn = false;
                    suppressRestoreAfterPet = false;
                }
                else
                {
                    companionWasActiveBeforePetSpawn = storedByPet;
                }
            }
            else
            {
                if (!IsStored)
                {
                    if (hadActiveCompanion)
                        EnsureHud();
                    return;
                }

                storedByPet = false;
                storedManually = false;
                companionWasActiveBeforePetSpawn = false;
                suppressRestoreAfterPet = false;

                TrySpawnCompanion(activeDefinition);
            }

            if (inventoryVisible)
            {
                inventoryVisible = false;
                InventoryVisibilityChanged?.Invoke(false);
            }

            if (equipmentVisible)
            {
                UpdateEquipmentVisibility(false);
            }
        }

        /// <summary>
        /// Called by the pet system when a pet despawns so the companion can automatically return
        /// if it was only hidden due to the temporary pet summon.
        /// </summary>
        public static void HandlePetDespawned()
        {
            if (suppressRestoreAfterPet)
            {
                suppressRestoreAfterPet = false;
                return;
            }
            if (storedByPet)
            {
                storedByPet = false;
                companionWasActiveBeforePetSpawn = false;
                TrySpawnCompanion(activeDefinition);
                return;
            }

            companionWasActiveBeforePetSpawn = false;

            if (HasActiveCompanion)
                EnsureHud();
        }

        /// <summary>
        /// Ensures the companion is hidden before a pet spawns, satisfying the single-entity rule.
        /// </summary>
        /// <param name="allowAutoRestore">
        ///     True when the companion should automatically return after the temporary pet despawns.
        ///     Pass false to keep the companion stored until the player explicitly summons it again.
        /// </param>
        public static void HandlePrePetSpawn(bool allowAutoRestore)
        {
            companionWasActiveBeforePetSpawn = HasActiveCompanion;
            if (!companionWasActiveBeforePetSpawn)
                return;

            suppressRestoreAfterPet = true;
            storedManually = false;
            DespawnCompanion(false);

            if (allowAutoRestore)
            {
                storedByPet = companionWasActiveBeforePetSpawn;
                storedManually = false;
            }
            else
            {
                storedByPet = false;
                storedManually = companionWasActiveBeforePetSpawn;
            }
        }

        /// <summary>
        /// Called by the combat pipeline when guard mode should prompt the companion to attack.
        /// </summary>
        /// <param name="target">Combat target selected by the player.</param>
        public static void CommandGuardAttack(CombatTarget target)
        {
            if (!guardModeEnabled || controller == null || target == null)
                return;

            if (!controller.gameObject.activeSelf)
                return;

            controller.CommandAttack(target);
        }

        /// <summary>
        /// Attempts to issue a direct attack command when guard mode is disabled, mirroring pet manual targeting.
        /// </summary>
        /// <param name="target">Combat target selected by the player.</param>
        /// <returns>True when the companion received the command.</returns>
        public static bool TryCommandAttack(CombatTarget target)
        {
            if (guardModeEnabled || controller == null || target == null)
                return false;

            if (!controller.gameObject.activeSelf)
                return false;

            if (!controller.CanFight)
                return false;

            controller.CommandAttack(target);
            return true;
        }

        /// <summary>
        /// Toggles guard mode and notifies listeners so menu labels stay in sync.
        /// </summary>
        public static void ToggleGuardMode()
        {
            guardModeEnabled = !guardModeEnabled;
            GuardModeChanged?.Invoke(guardModeEnabled);
        }

        /// <summary>
        /// Opens the companion inventory, matching the behaviour of the player backpack.
        /// </summary>
        public static void ToggleInventory()
        {
            if (controller == null)
                return;

            if (controller.IsEquipmentVisible)
            {
                controller.Equipment?.ForceClosed();
            }

            controller.ToggleInventory();
        }

        /// <summary>
        /// Toggles the companion equipment window and reports the resulting state.
        /// </summary>
        public static bool ToggleEquipment()
        {
            if (controller == null)
                return false;

            bool wasOpen = controller.IsEquipmentVisible;

            if (!wasOpen && inventoryVisible)
            {
                controller.Inventory?.ForceClosed();
            }

            bool opened = controller.ToggleEquipment();
            UpdateEquipmentVisibility(opened);
            return opened;
        }

        /// <summary>
        /// Opens the companion stats window using the shared UI manager lifecycle.
        /// </summary>
        public static void OpenStats()
        {
            if (CompanionStatsWindow.Instance != null)
            {
                CompanionStatsWindow.Instance.BindSkills(CompanionSkills);
                CompanionStatsWindow.Instance.Open();
            }
        }

        /// <summary>
        /// Exposes the current inventory visibility without allowing external systems to mutate it directly.
        /// </summary>
        public static bool IsInventoryVisible()
        {
            return inventoryVisible;
        }

        /// <summary>Returns whether the companion equipment UI is currently open.</summary>
        public static bool IsEquipmentVisible()
        {
            return equipmentVisible;
        }

        /// <summary>
        /// Attempts to equip an item into the companion equipment using the player's inventory entry.
        /// </summary>
        public static bool TryEquipItemFromPlayerInventory(Inventory.Inventory playerInventory, InventoryEntry entry)
        {
            if (controller == null || controller.Equipment == null)
                return false;

            if (!equipmentVisible)
                return false;

            return controller.TryEquipFromPlayerInventory(entry, playerInventory);
        }

        /// <summary>
        /// Allows the HUD or context menus to re-open after a reload without duplicating instances.
        /// </summary>
        /// <param name="hud">HUD instance that should become the active binding.</param>
        internal static void RegisterHud(PetLevelBarHUD hud)
        {
            boundHud = hud;
        }

        /// <summary>
        /// Displays the shared context menu at the supplied screen position.
        /// </summary>
        /// <param name="screenPosition">Pointer position in screen coordinates.</param>
        public static void ShowContextMenu(Vector2 screenPosition)
        {
            EnsureHud();
            if (boundHud != null)
                PetLevelBarMenu.Show(boundHud, screenPosition);
        }

        /// <summary>
        /// Exposes the current display name for chat output and context messages.
        /// </summary>
        public static string GetCompanionDisplayName() => ResolveCompanionName();

        /// <summary>
        /// Routes a mining command to the active companion when available.
        /// </summary>
        /// <param name="rock">Rock that should be mined.</param>
        /// <returns>True when the companion accepted the command, otherwise false.</returns>
        public static bool TryCommandMine(MineableRock rock)
        {
            if (rock == null)
                return false;

            if (controller == null || controller.MiningController == null)
                return false;

            if (!HasActiveCompanion)
                return false;

            bool accepted = controller.MiningController.TryCommandMine(rock);
            if (CompanionManager.EnableDebugLogging)
            {
                if (accepted)
                    Debug.Log("[Companion] Forwarded mining command to companion controller.");
                else
                    Debug.Log("[Companion] Companion mining command was rejected by the controller.");
            }

            return accepted;
        }

        /// <summary>
        /// Reports combat damage dealt by the companion so XP can be routed through the player formulas.
        /// </summary>
        /// <param name="damage">Damage applied to the target.</param>
        /// <param name="style">Combat style used for the attack.</param>
        /// <param name="type">Damage type associated with the attack.</param>
        public static void AwardCombatExperience(int damage, CombatStyle style, DamageType type)
        {
            if (controller == null || damage <= 0)
                return;

            controller.AwardCombatXp(damage, style, type);
        }

        /// <summary>
        /// Called whenever the companion skill manager reports a level change so combat level can be recomputed.
        /// </summary>
        private static void HandleSkillLevelChanged(SkillType skill, int level)
        {
            UpdateCombatLevel();
            PublishCompanionLevelUpMessage(skill, level);
        }

        /// <summary>
        /// Broadcasts a companion-channel chat message whenever the active companion levels a skill.
        /// </summary>
        /// <param name="skill">Skill that gained a level.</param>
        /// <param name="level">Resulting companion level.</param>
        private static void PublishCompanionLevelUpMessage(SkillType skill, int level)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string companionName = GetCompanionDisplayName();
            string pronoun = ResolveCompanionPronoun();
            string skillName = SkillNameUtility.GetSentenceName(skill);
            string message = $"Just levelled up {pronoun} {skillName} to level {level}!";
            chat.PublishCompanionMessage(companionName, message);
        }

        /// <summary>
        /// Resolves the companion name used for chat output, falling back to a generic label when
        /// no runtime definition is available.
        /// </summary>
        private static string ResolveCompanionName()
        {
            if (activeDefinition != null && !string.IsNullOrWhiteSpace(activeDefinition.displayName))
                return activeDefinition.displayName;

            return "Companion";
        }

        /// <summary>
        /// Determines which possessive pronoun the current companion prefers for chat messaging.
        /// Defaults to a neutral pronoun so messages remain grammatically correct when definitions
        /// do not provide explicit data.
        /// </summary>
        private static string ResolveCompanionPronoun()
        {
            if (activeDefinition != null && !string.IsNullOrWhiteSpace(activeDefinition.possessivePronoun))
                return activeDefinition.possessivePronoun.ToLowerInvariant();

            return "their";
        }

        /// <summary>
        /// Handles inventory visibility callbacks from the controller.
        /// </summary>
        private static void HandleInventoryVisibilityChanged(bool visible)
        {
            inventoryVisible = visible;
            InventoryVisibilityChanged?.Invoke(visible);
        }

        /// <summary>
        /// Mirrors equipment visibility updates from the controller so menus stay in sync.
        /// </summary>
        private static void HandleEquipmentVisibilityChanged(bool visible)
        {
            UpdateEquipmentVisibility(visible);

            if (visible)
                EnsurePlayerInventoryOpen();
        }

        /// <summary>
        /// Clears cached state when the controller is destroyed (e.g. on shutdown).
        /// </summary>
        private static void HandleControllerDespawned(CompanionController destroyed)
        {
            if (controller != destroyed)
                return;

            controller.SkillLevelChanged -= HandleSkillLevelChanged;
            controller.InventoryVisibilityChanged -= HandleInventoryVisibilityChanged;
            controller.EquipmentVisibilityChanged -= HandleEquipmentVisibilityChanged;
            controller.Despawned -= HandleControllerDespawned;
            controller = null;
            companionObject = null;
            activeDefinition = null;
            inventoryVisible = false;
            storedByPet = false;
            storedManually = false;
            companionWasActiveBeforePetSpawn = false;
            guardModeEnabled = false;
            combatLevel = 1;
            InventoryVisibilityChanged?.Invoke(false);
            UpdateEquipmentVisibility(false);
            GuardModeChanged?.Invoke(false);
            PetLevelBarHUD.DestroyInstance();
            boundHud = null;
        }

        /// <summary>
        /// Recomputes the combat level from the companion skills and notifies listeners when it changes.
        /// </summary>
        private static void UpdateCombatLevel()
        {
            int previous = combatLevel;
            combatLevel = controller != null
                ? CombatLevelUtility.CalculateCombatLevel(controller.SkillManager)
                : 1;

            if (combatLevel != previous)
                CombatLevelChanged?.Invoke(combatLevel);
        }

        /// <summary>
        /// Updates the cached equipment visibility flag and notifies listeners when the state changes.
        /// </summary>
        private static void UpdateEquipmentVisibility(bool visible)
        {
            if (equipmentVisible == visible)
                return;

            equipmentVisible = visible;
            EquipmentVisibilityChanged?.Invoke(visible);
        }

        /// <summary>
        /// Ensures the player's own inventory window is visible whenever the companion equipment opens.
        /// </summary>
        private static void EnsurePlayerInventoryOpen()
        {
            var playerInventory = ResolvePlayerInventory();
            if (playerInventory == null)
                return;

            if (!playerInventory.IsOpen)
                playerInventory.OpenUI();
        }

        /// <summary>
        /// Resolves the active player inventory, caching the result so repeated equipment toggles stay fast.
        /// </summary>
        private static Inventory.Inventory ResolvePlayerInventory()
        {
            if (cachedPlayerInventory != null)
                return cachedPlayerInventory;

            cachedPlayerInventory = null;

            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                cachedPlayerInventory = playerObject.GetComponent<Inventory.Inventory>() ??
                                        playerObject.GetComponentInChildren<Inventory.Inventory>();
                if (cachedPlayerInventory != null)
                    return cachedPlayerInventory;
            }

            var inventories = UnityEngine.Object.FindObjectsOfType<Inventory.Inventory>(true);
            foreach (var inventory in inventories)
            {
                if (inventory == null)
                    continue;

                if (inventory.GetComponent<CompanionInventory>() != null)
                    continue;

                if (inventory.GetComponent<Pets.PetStorage>() != null)
                    continue;

                cachedPlayerInventory = inventory;
                break;
            }

            return cachedPlayerInventory;
        }
    }
}
