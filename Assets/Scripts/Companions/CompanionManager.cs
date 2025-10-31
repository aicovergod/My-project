/// Feature: Exposed active companion accessor for pickup service integration.
using System;
using System.Collections.Generic;
using BankSystem;
using Combat;
using Companions.Chat;
using Companions.Commands;
using Companions.Combat;
using Companions.Equipment;
using Companions.Inventory;
using Inventory;
using Pets;
using Skills;
using Skills.Cooking;
using Skills.Fishing;
using Skills.Mining;
using Skills.Woodcutting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Companions.UI;
using RuntimeInventory = global::Inventory.Inventory;

namespace Companions
{
    /// <summary>
    /// Describes the high-level activity the companion is currently performing so UI and chat logic can react consistently.
    /// </summary>
    public enum CompanionActiveAction
    {
        /// <summary>No notable action is currently in progress.</summary>
        None = 0,

        /// <summary>The companion is actively engaged in combat.</summary>
        Combat = 1,

        /// <summary>The companion is fishing at a nearby spot.</summary>
        Fishing = 2,

        /// <summary>The companion is mining a rock.</summary>
        Mining = 3,

        /// <summary>The companion is chopping a tree.</summary>
        Woodcutting = 4,

        /// <summary>The companion is cooking at a nearby station.</summary>
        Cooking = 5
    }

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
        private static RuntimeInventory cachedPlayerInventory;

        /// <summary>Latest combat level computed from the companion's skills.</summary>
        private static int combatLevel = 1;

        /// <summary>Prevents automatic resummoning when a pet temporarily hides the companion.</summary>
        private static bool suppressRestoreAfterPet;

        /// <summary>Shared mining command service instance.</summary>
        private static readonly CompanionMiningCommandService miningCommandService = new CompanionMiningCommandService();

        /// <summary>Shared fishing command service instance.</summary>
        private static readonly CompanionFishingCommandService fishingCommandService = new CompanionFishingCommandService();

        /// <summary>Shared cooking command service instance.</summary>
        private static readonly CompanionCookingCommandService cookingCommandService = new CompanionCookingCommandService();

        /// <summary>Shared woodcutting command service instance.</summary>
        private static readonly CompanionWoodcuttingCommandService woodcuttingCommandService = new CompanionWoodcuttingCommandService();

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

        static CompanionManager()
        {
            CompanionGuardModeService.GuardModeStateChanged += HandleGuardModeServiceStateChanged;
        }

        /// <summary>Handles chat-based inventory reactions for the active companion.</summary>
        private static CompanionChatInventoryResponder chatInventoryResponder;

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

        /// <summary>Provides direct access to the active companion controller instance.</summary>
        public static CompanionController ActiveCompanion => controller;

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
        public static bool GuardModeEnabled => CompanionGuardModeService.GuardModeEnabled;

        /// <summary>
        /// True when a combat-decline cooldown is preventing guard mode from being enabled.
        /// Exposed for UI surfaces that may wish to grey out the toggle.
        /// </summary>
        public static bool IsGuardModeLockedByCombatCooldown => CompanionGuardModeService.IsGuardModeLockedByCombatCooldown;

        /// <summary>Exposes the bound skill manager used for stats UI integration.</summary>
        public static SkillManager CompanionSkills => controller != null ? controller.SkillManager : null;

        /// <summary>Provides access to the configured inventory wrapper.</summary>
        public static CompanionInventory CompanionInventory => controller != null ? controller.Inventory : null;

        /// <summary>Provides access to the configured equipment wrapper.</summary>
        public static CompanionEquipment CompanionEquipment => controller != null ? controller.Equipment : null;

        /// <summary>Provides access to the directed pickup controller attached to the companion.</summary>
        public static CompanionPickupController CompanionPickupController => controller != null ? controller.PickupController : null;

        /// <summary>Provides access to the configured fishing controller.</summary>
        public static CompanionFishingController CompanionFishingController => controller != null ? controller.FishingController : null;

        /// <summary>Provides access to the configured cooking controller.</summary>
        public static CompanionCookingController CompanionCookingController => controller != null ? controller.CookingController : null;

        /// <summary>Safely exposes the cached player inventory used for companion transfers.</summary>
        public static RuntimeInventory GetPlayerInventory() => ResolvePlayerInventory();

        /// <summary>Exposes the spawned companion object for systems that need the instance handle.</summary>
        public static GameObject CompanionObject => companionObject;

        /// <summary>Definition that spawned the active companion, exposed for display helpers.</summary>
        public static PetDefinition ActiveDefinition => activeDefinition;

        /// <summary>Provides access to the cooldown tracker used for throttling companion skill commands.</summary>
        public static CompanionSkillCooldownTracker CompanionSkillCooldowns =>
            controller != null ? controller.SkillCooldowns : null;

        /// <summary>Resolves the companion's current high-level action so UI and chat logic can react appropriately.</summary>
        public static CompanionActiveAction GetActiveAction()
        {
            if (!HasActiveCompanion || controller == null)
                return CompanionActiveAction.None;

            if (controller.IsInCombat)
                return CompanionActiveAction.Combat;

            var fishing = controller.FishingController;
            if (fishing != null && fishing.IsFishing)
                return CompanionActiveAction.Fishing;

            var mining = controller.MiningController;
            if (mining != null && mining.IsMining)
                return CompanionActiveAction.Mining;

            var cooking = controller.CookingController;
            if (cooking != null && cooking.IsCooking)
                return CompanionActiveAction.Cooking;

            var woodcutting = controller.WoodcuttingController;
            if (woodcutting != null && woodcutting.IsWoodcutting)
                return CompanionActiveAction.Woodcutting;

            return CompanionActiveAction.None;
        }

        /// <summary>True while the companion is performing any cancellable action.</summary>
        public static bool HasActiveAction => GetActiveAction() != CompanionActiveAction.None;

        /// <summary>Returns a player-facing label describing the current companion action.</summary>
        public static string GetActiveActionDisplayName() => CompanionDisplayUtility.GetActionDisplayName(GetActiveAction());

        /// <summary>Translates an action enum value into a human-readable description.</summary>
        public static string GetActiveActionDisplayName(CompanionActiveAction action) => CompanionDisplayUtility.GetActionDisplayName(action);

        /// <summary>Returns the label that should be displayed on stop buttons for the current action.</summary>
        public static string GetStopActionLabel() => CompanionDisplayUtility.GetStopActionLabel(GetActiveAction());

        /// <summary>Returns the label that should be displayed on stop buttons for the supplied action.</summary>
        public static string GetStopActionLabel(CompanionActiveAction action) => CompanionDisplayUtility.GetStopActionLabel(action);

        /// <summary>Attempts to cancel whichever action the companion is currently performing.</summary>
        public static bool TryCancelCurrentAction()
        {
            var action = GetActiveAction();
            if (action == CompanionActiveAction.None)
                return false;

            switch (action)
            {
                case CompanionActiveAction.Combat:
                    controller?.CancelActiveCombat();
                    break;
                case CompanionActiveAction.Fishing:
                    controller?.FishingController?.CancelFishing(true);
                    break;
                case CompanionActiveAction.Mining:
                    controller?.MiningController?.CancelMining(true);
                    break;
                case CompanionActiveAction.Cooking:
                    controller?.CookingController?.CancelCooking(true);
                    break;
                case CompanionActiveAction.Woodcutting:
                    controller?.WoodcuttingController?.CancelWoodcutting(true);
                    break;
                default:
                    return false;
            }

            if (EnableDebugLogging)
                Debug.Log($"[Companion] Cancelled active {action} action via stop request.");

            return true;
        }

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
        /// <param name="suppressManualSpawnGreeting">True when automated spawns (loads/restores) should skip the manual greeting line.</param>
        public static void TrySpawnCompanion(PetDefinition definitionOverride, bool suppressManualSpawnGreeting = false)
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
                    ResetStorageFlags();

                    if (!controller.gameObject.activeSelf)
                    {
                        if (ReactivateExistingCompanionIfNeeded())
                            return;

                        TearDownExistingCompanion();
                    }
                    else
                    {
                        EnsureHud();
                        EnsureChatResponderInitialised();
                        return;
                    }
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

            CompanionGuardModeService.Bind(controller, controller.SkillCooldowns);
            activeDefinition = resolvedDefinition;
            inventoryVisible = false;
            equipmentVisible = false;
            storedByPet = false;
            storedManually = false;
            companionWasActiveBeforePetSpawn = false;
            suppressRestoreAfterPet = false;

            UpdateCombatLevel();
            EnsureHud();
            EnsureChatResponderInitialised();
            UpdateEquipmentVisibility(false);
            if (!suppressManualSpawnGreeting)
                CompanionChatEventService.PublishRandomManualSpawnMessage();
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
                CompanionGuardModeService.Unbind(controller);
            }

            controller = null;
            companionObject = null;

            DisposeChatResponder();

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
        /// Clears the storage tracking flags so subsequent summons treat the companion as active.
        /// </summary>
        private static void ResetStorageFlags()
        {
            storedByPet = false;
            storedManually = false;
            companionWasActiveBeforePetSpawn = false;
            suppressRestoreAfterPet = false;
        }

        /// <summary>
        /// Ensures the chat responder exists and is ready to process incoming messages.
        /// </summary>
        private static void EnsureChatResponderInitialised()
        {
            chatInventoryResponder ??= new CompanionChatInventoryResponder();
            chatInventoryResponder.Initialise();
        }

        /// <summary>
        /// Disposes of the chat responder so no callbacks fire while the companion is inactive.
        /// </summary>
        private static void DisposeChatResponder()
        {
            if (chatInventoryResponder == null)
                return;

            chatInventoryResponder.Dispose();
            chatInventoryResponder = null;
        }

        /// <summary>
        /// Reactivates the current companion instance when it already exists but is disabled.
        /// Ensures UI bindings refresh and verifies that the companion is visible before returning.
        /// </summary>
        /// <returns>True when the existing companion was successfully reactivated.</returns>
        private static bool ReactivateExistingCompanionIfNeeded()
        {
            if (controller == null || controller.gameObject == null)
                return false;

            if (controller.gameObject.activeSelf)
            {
                EnsureChatResponderInitialised();
                return HasActiveCompanion;
            }

            controller.HandleSummonRequest();
            controller.Inventory?.ForceClosed();
            controller.Equipment?.ForceClosed();

            EnsureHud();
            EnsureChatResponderInitialised();
            RefreshMenusAfterRestore();

            bool active = HasActiveCompanion;
            if (!active && enableDebugLogging)
            {
                Debug.LogWarning("[Companion] ReactivateExistingCompanionIfNeeded expected the companion to become active but it remained disabled.");
            }
            else if (active && enableDebugLogging)
            {
                Debug.Log("[Companion] Reactivated stored companion without respawning.");
            }

            return active;
        }

        /// <summary>
        /// Resets menu state after the companion is restored so UI overlays rebuild against the live instance.
        /// </summary>
        private static void RefreshMenusAfterRestore()
        {
            PetLevelBarMenu.HideActiveMenu();
            CompanionCommandMenu.Hide();

            if (inventoryVisible)
            {
                inventoryVisible = false;
                InventoryVisibilityChanged?.Invoke(false);
            }

            if (equipmentVisible)
                UpdateEquipmentVisibility(false);
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

                if (!triggeredByPet)
                {
                    // Inform the pet drop system that the companion is no longer active so it stops
                    // blocking re-drops of the pickup item and clears any saved spawn state.
                    PetDropSystem.HandleCompanionManuallyStored();
                }

                storedByPet = triggeredByPet && hadActiveCompanion;
                storedManually = !triggeredByPet && hadActiveCompanion;
                if (storedManually)
                    CompanionChatEventService.PublishRandomManualStoreMessage();
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

                ResetStorageFlags();

                bool restoredExisting = ReactivateExistingCompanionIfNeeded();

                if (!restoredExisting && !HasActiveCompanion)
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
            CompanionGuardModeService.CommandGuardAttack(target);
        }

        /// <summary>
        /// Attempts to issue a direct attack command when guard mode is disabled, mirroring pet manual targeting.
        /// </summary>
        /// <param name="target">Combat target selected by the player.</param>
        /// <returns>True when the companion received the command.</returns>
        public static bool TryCommandAttack(CombatTarget target)
        {
            return CompanionGuardModeService.TryCommandAttack(target);
        }

        /// <summary>
        /// Toggles guard mode and notifies listeners so menu labels stay in sync.
        /// </summary>
        public static void ToggleGuardMode()
        {
            CompanionGuardModeService.ToggleGuardMode();
        }

        /// <summary>
        /// Called whenever the combat-decline cooldown is (re)started so guard mode can be locked and
        /// forcibly disabled until the timer elapses.
        /// </summary>
        public static void HandleCombatDeclineCooldownStarted()
        {
            CompanionGuardModeService.HandleCombatDeclineCooldownStarted();
        }

        /// <summary>
        /// Called whenever the combat-decline cooldown ends or is cleared so guard mode toggles can
        /// resume functioning normally.
        /// </summary>
        public static void HandleCombatDeclineCooldownCleared()
        {
            CompanionGuardModeService.HandleCombatDeclineCooldownCleared();
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
        public static CompanionEquipAttemptResult TryEquipItemFromPlayerInventory(RuntimeInventory playerInventory, InventoryEntry entry)
        {
            if (controller == null || controller.Equipment == null)
                return CompanionEquipAttemptResult.NotHandled;

            if (!equipmentVisible)
                return CompanionEquipAttemptResult.NotHandled;

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
        /// Attempts to deposit every item currently stored in the companion inventory into the player's bank.
        /// </summary>
        /// <returns>True when at least one item was deposited successfully; otherwise false.</returns>
        public static bool TryDepositCompanionInventoryToBank()
        {
            return CompanionBankDepositService.TryDepositCompanionInventoryToBank(
                controller,
                EnableDebugLogging,
                GetCompanionDisplayName());
        }

        /// <summary>
        /// Publishes a greeting when the companion automatically respawns after a login.
        /// Helps returning players feel acknowledged without requiring manual interaction.
        /// </summary>
        public static void PublishAutoSpawnGreeting()
        {
            CompanionChatEventService.PublishAutoSpawnGreeting();
        }

        /// <summary>
        /// Forwards guard-mode state updates from the guard service to legacy listeners and handles flavour chat.
        /// </summary>
        /// <param name="enabled">New guard-mode enabled state.</param>
        /// <param name="publishChatLine">True when the transition should emit a companion chat line.</param>
        private static void HandleGuardModeServiceStateChanged(bool enabled, bool publishChatLine)
        {
            GuardModeChanged?.Invoke(enabled);

            if (!publishChatLine)
                return;

            if (enabled)
                CompanionChatEventService.PublishRandomGuardModeActivationMessage();
            else
                CompanionChatEventService.PublishRandomGuardModeDeactivationMessage();
        }

        /// <summary>
        /// Exposes the current display name for chat output and context messages.
        /// </summary>
        public static string GetCompanionDisplayName() => CompanionDisplayUtility.GetDisplayName(activeDefinition);

        /// <summary>
        /// Routes a mining command to the active companion when available.
        /// </summary>
        /// <param name="rock">Rock that should be mined.</param>
        /// <returns>True when the companion accepted the command, otherwise false.</returns>
        public static bool TryCommandMine(MineableRock rock)
        {
            return miningCommandService.TryCommandMine(controller, controller != null ? controller.MiningController : null, rock);
        }

        /// <summary>
        /// Attempts to command the companion to mine nearby rocks using the default scan radius.
        /// </summary>
        /// <param name="failureReason">Detailed failure reason when the command is rejected.</param>
        /// <returns>True when the area mining command started successfully.</returns>
        public static bool TryCommandMineNearby(out CompanionMiningCommandResult failureReason)
        {
            return TryCommandMineNearby(10f, out failureReason);
        }

        /// <summary>
        /// Attempts to command the companion to mine nearby rocks within the supplied radius.
        /// </summary>
        /// <param name="radius">Radius (in Unity units / tiles) to scan for rocks.</param>
        /// <returns>True when the area mining command started successfully.</returns>
        public static bool TryCommandMineNearby(float radius = 10f)
        {
            return TryCommandMineNearby(radius, out _);
        }

        /// <summary>
        /// Attempts to command the companion to mine nearby rocks within the supplied radius and reports the resulting status.
        /// </summary>
        /// <param name="radius">Radius (in Unity units / tiles) to scan for rocks.</param>
        /// <param name="failureReason">Detailed failure reason when the command is rejected.</param>
        /// <returns>True when the area mining command started successfully.</returns>
        public static bool TryCommandMineNearby(float radius, out CompanionMiningCommandResult failureReason)
        {
            return miningCommandService.TryCommandMineNearby(
                controller,
                controller != null ? controller.MiningController : null,
                radius,
                out failureReason);
        }

        /// <summary>
        /// Routes a fishing command to the active companion when available.
        /// </summary>
        /// <param name="spot">Spot that should be fished.</param>
        /// <returns>True when the companion accepted the command, otherwise false.</returns>
        public static bool TryCommandFish(FishableSpot spot)
        {
            return fishingCommandService.TryCommandFish(controller, controller != null ? controller.FishingController : null, spot);
        }

        /// <summary>
        /// Attempts to command the companion to fish nearby spots using the default scan radius.
        /// </summary>
        /// <param name="failureReason">Detailed failure reason when the command is rejected.</param>
        /// <returns>True when the area fishing command started successfully.</returns>
        public static bool TryCommandFishNearby(out CompanionFishingCommandResult failureReason)
        {
            return TryCommandFishNearby(10f, out failureReason);
        }

        /// <summary>
        /// Attempts to command the companion to fish nearby spots within the supplied radius.
        /// </summary>
        /// <param name="radius">Radius (in Unity units / tiles) to scan for spots.</param>
        /// <returns>True when the area fishing command started successfully.</returns>
        public static bool TryCommandFishNearby(float radius = 10f)
        {
            return TryCommandFishNearby(radius, out _);
        }

        /// <summary>
        /// Attempts to command the companion to fish nearby spots within the supplied radius and reports the resulting status.
        /// </summary>
        /// <param name="radius">Radius (in Unity units / tiles) to scan for spots.</param>
        /// <param name="failureReason">Detailed failure reason when the command is rejected.</param>
        /// <returns>True when the area fishing command started successfully.</returns>
        public static bool TryCommandFishNearby(float radius, out CompanionFishingCommandResult failureReason)
        {
            return fishingCommandService.TryCommandFishNearby(
                controller,
                controller != null ? controller.FishingController : null,
                radius,
                out failureReason);
        }

        /// <summary>
        /// Attempts to command the companion to cook at the supplied station.
        /// </summary>
        public static bool TryCommandCook(CookingObject station, CookableRecipe recipe, out CompanionCookingCommandResult result)
        {
            return cookingCommandService.TryCommandCook(
                controller,
                controller != null ? controller.CookingController : null,
                station,
                recipe,
                out result,
                TryDepositCompanionInventoryToBank);
        }

        /// <summary>
        /// Attempts to command the companion to cook at any nearby station within the default radius.
        /// </summary>
        public static bool TryCommandCookNearby(out CompanionCookingCommandResult failureReason)
        {
            return TryCommandCookNearby(10f, out failureReason);
        }

        /// <summary>
        /// Attempts to command the companion to cook at any nearby station within the supplied radius.
        /// </summary>
        public static bool TryCommandCookNearby(float radius = 10f)
        {
            return TryCommandCookNearby(radius, out _);
        }

        /// <summary>
        /// Attempts to command the companion to cook at any nearby station within the supplied radius and reports the result.
        /// </summary>
        public static bool TryCommandCookNearby(float radius, out CompanionCookingCommandResult failureReason)
        {
            return cookingCommandService.TryCommandCookNearby(
                controller,
                controller != null ? controller.CookingController : null,
                radius,
                out failureReason,
                TryDepositCompanionInventoryToBank);
        }

        /// <summary>
        /// Routes a woodcutting command to the active companion when available.
        /// </summary>
        /// <param name="tree">Tree that should be chopped.</param>
        /// <returns>True when the companion accepted the command, otherwise false.</returns>
        public static bool TryCommandChop(TreeNode tree)
        {
            return woodcuttingCommandService.TryCommandChop(controller, controller != null ? controller.WoodcuttingController : null, tree);
        }

        /// <summary>
        /// Attempts to command the companion to chop nearby trees using the default scan radius.
        /// </summary>
        /// <param name="failureReason">Detailed failure reason when the command is rejected.</param>
        /// <returns>True when the area woodcutting command started successfully.</returns>
        public static bool TryCommandChopNearby(out CompanionWoodcuttingCommandResult failureReason)
        {
            return TryCommandChopNearby(10f, out failureReason);
        }

        /// <summary>
        /// Attempts to command the companion to chop nearby trees within the supplied radius.
        /// </summary>
        /// <param name="radius">Radius (in Unity units / tiles) to scan for trees.</param>
        /// <returns>True when the area woodcutting command started successfully.</returns>
        public static bool TryCommandChopNearby(float radius = 10f)
        {
            return TryCommandChopNearby(radius, out _);
        }

        /// <summary>
        /// Attempts to command the companion to chop nearby trees within the supplied radius and reports the resulting status.
        /// </summary>
        /// <param name="radius">Radius (in Unity units / tiles) to scan for trees.</param>
        /// <param name="failureReason">Detailed failure reason when the command is rejected.</param>
        /// <returns>True when the area woodcutting command started successfully.</returns>
        public static bool TryCommandChopNearby(float radius, out CompanionWoodcuttingCommandResult failureReason)
        {
            return woodcuttingCommandService.TryCommandChopNearby(
                controller,
                controller != null ? controller.WoodcuttingController : null,
                radius,
                out failureReason);
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
            CompanionChatEventService.PublishCompanionLevelUpMessage(
                skill,
                level,
                CompanionDisplayUtility.GetPossessivePronoun(activeDefinition));
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
            CompanionGuardModeService.Unbind(controller);
            controller = null;
            companionObject = null;
            DisposeChatResponder();
            activeDefinition = null;
            inventoryVisible = false;
            storedByPet = false;
            storedManually = false;
            companionWasActiveBeforePetSpawn = false;
            combatLevel = 1;
            InventoryVisibilityChanged?.Invoke(false);
            UpdateEquipmentVisibility(false);
            PetLevelBarHUD.DestroyInstance();
            boundHud = null;
            CompanionCommandMenu.Hide();
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
        private static RuntimeInventory ResolvePlayerInventory()
        {
            if (cachedPlayerInventory != null)
                return cachedPlayerInventory;

            cachedPlayerInventory = null;

            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                cachedPlayerInventory = playerObject.GetComponent<RuntimeInventory>() ??
                                        playerObject.GetComponentInChildren<RuntimeInventory>();
                if (cachedPlayerInventory != null)
                    return cachedPlayerInventory;
            }

            var inventories = UnityEngine.Object.FindObjectsOfType<RuntimeInventory>(true);
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
