using System;
using BankSystem;
using Combat;
using Inventory;
using Pets;
using Skills;
using Skills.Mining;
using UI.Chat;
using UnityEngine;
using UnityEngine.SceneManagement;
using Companions.UI;

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

        /// <summary>
        /// Tracks whether guard mode has been locked by a combat-decline cooldown so activation attempts
        /// can be suppressed until the timer expires.
        /// </summary>
        private static bool guardModeLockedByCombatCooldown;

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

        /// <summary>True once the chat service subscription for inventory full reactions is active.</summary>
        private static bool chatSubscribed;

        /// <summary>Last unscaled time the companion reacted to a full inventory message.</summary>
        private static float lastInventoryFullReactionTime = -10f;

        /// <summary>Cooldown applied between companion responses to repeated inventory full messages.</summary>
        private const float InventoryFullChatCooldownSeconds = 4f;

        /// <summary>Normalised comparison string for detecting the standard full inventory game message.</summary>
        private const string PlayerInventoryFullGameMessage = "your inventory is full";

        /// <summary>Normalised comparison string for the combined player and companion inventory message.</summary>
        private const string PlayerAndCompanionInventoryFullGameMessage =
            "your inventory and your companion's inventory are full";

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

        /// <summary>
        /// True when a combat-decline cooldown is preventing guard mode from being enabled.
        /// Exposed for UI surfaces that may wish to grey out the toggle.
        /// </summary>
        public static bool IsGuardModeLockedByCombatCooldown => guardModeLockedByCombatCooldown;

        /// <summary>Exposes the bound skill manager used for stats UI integration.</summary>
        public static SkillManager CompanionSkills => controller != null ? controller.SkillManager : null;

        /// <summary>Provides access to the configured inventory wrapper.</summary>
        public static CompanionInventory CompanionInventory => controller != null ? controller.Inventory : null;

        /// <summary>Provides access to the configured equipment wrapper.</summary>
        public static CompanionEquipment CompanionEquipment => controller != null ? controller.Equipment : null;

        /// <summary>Safely exposes the cached player inventory used for companion transfers.</summary>
        public static Inventory.Inventory GetPlayerInventory() => ResolvePlayerInventory();

        /// <summary>Exposes the spawned companion object for systems that need the instance handle.</summary>
        public static GameObject CompanionObject => companionObject;

        /// <summary>Provides access to the cooldown tracker used for throttling companion skill commands.</summary>
        public static CompanionSkillCooldownTracker CompanionSkillCooldowns =>
            controller != null ? controller.SkillCooldowns : null;

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
                        EnsureChatSubscription();
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

            activeDefinition = resolvedDefinition;
            guardModeEnabled = false;
            guardModeLockedByCombatCooldown = false;
            inventoryVisible = false;
            equipmentVisible = false;
            storedByPet = false;
            storedManually = false;
            companionWasActiveBeforePetSpawn = false;
            suppressRestoreAfterPet = false;

            UpdateCombatLevel();
            EnsureHud();
            EnsureChatSubscription();
            UpdateEquipmentVisibility(false);
            if (!suppressManualSpawnGreeting)
                PublishRandomManualSpawnMessage();
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

            RemoveChatSubscription();

            guardModeEnabled = false;
            guardModeLockedByCombatCooldown = false;
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
        /// Ensures the chat service is subscribed so the companion can react to full inventory messages.
        /// Safe to invoke repeatedly as duplicate subscriptions are ignored.
        /// </summary>
        private static void EnsureChatSubscription()
        {
            if (chatSubscribed)
                return;

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            chat.MessageReceived -= HandleChatMessageReceived;
            chat.MessageReceived += HandleChatMessageReceived;
            chatSubscribed = true;
        }

        /// <summary>
        /// Removes the chat subscription when the companion is despawned so no stray callbacks fire.
        /// </summary>
        private static void RemoveChatSubscription()
        {
            if (!chatSubscribed)
                return;

            var chat = ChatService.Instance;
            if (chat != null)
                chat.MessageReceived -= HandleChatMessageReceived;

            chatSubscribed = false;
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
                EnsureChatSubscription();
                return HasActiveCompanion;
            }

            controller.HandleSummonRequest();
            controller.Inventory?.ForceClosed();
            controller.Equipment?.ForceClosed();

            EnsureHud();
            EnsureChatSubscription();
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
                    PublishRandomManualStoreMessage();
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
            if (IsCombatCooldownActiveAndUpdateLock(false))
            {
                DisableGuardModeForCooldown(false);
                return;
            }

            if (!guardModeEnabled || controller == null || target == null)
                return;

            if (!controller.gameObject.activeSelf)
                return;

            if (!controller.CanFight)
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
            if (IsCombatCooldownActiveAndUpdateLock(true))
                return false;

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
            if (!guardModeEnabled)
            {
                if (IsCombatCooldownActiveAndUpdateLock(true))
                    return;

                guardModeEnabled = true;
                guardModeLockedByCombatCooldown = false;
                GuardModeChanged?.Invoke(true);
                PublishRandomGuardModeActivationMessage();
                return;
            }

            guardModeEnabled = false;
            GuardModeChanged?.Invoke(false);
            PublishRandomGuardModeDeactivationMessage();
        }

        /// <summary>
        /// Called whenever the combat-decline cooldown is (re)started so guard mode can be locked and
        /// forcibly disabled until the timer elapses.
        /// </summary>
        public static void HandleCombatDeclineCooldownStarted()
        {
            guardModeLockedByCombatCooldown = true;
            DisableGuardModeForCooldown(true);
        }

        /// <summary>
        /// Called whenever the combat-decline cooldown ends or is cleared so guard mode toggles can
        /// resume functioning normally.
        /// </summary>
        public static void HandleCombatDeclineCooldownCleared()
        {
            if (!guardModeLockedByCombatCooldown)
                return;

            guardModeLockedByCombatCooldown = false;
        }

        /// <summary>
        /// Disables guard mode due to the combat cooldown and optionally publishes a deactivation line.
        /// </summary>
        private static void DisableGuardModeForCooldown(bool publishGuardMessage)
        {
            if (!guardModeEnabled)
                return;

            guardModeEnabled = false;
            GuardModeChanged?.Invoke(false);

            if (publishGuardMessage)
                PublishRandomGuardModeDeactivationMessage();
        }

        /// <summary>
        /// Evaluates whether the combat-decline cooldown is still active and updates the guard lock flag.
        /// </summary>
        private static bool IsCombatCooldownActiveAndUpdateLock(bool publishMessage)
        {
            var tracker = CompanionSkillCooldowns;
            bool active = CompanionSkillCooldownTimers.IsCombatDeclineCooldownActive(tracker, publishMessage);
            if (active)
            {
                guardModeLockedByCombatCooldown = true;
                return true;
            }

            if (guardModeLockedByCombatCooldown)
                guardModeLockedByCombatCooldown = false;

            return false;
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
        public static CompanionEquipAttemptResult TryEquipItemFromPlayerInventory(Inventory.Inventory playerInventory, InventoryEntry entry)
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
            if (!HasActiveCompanion)
            {
                if (EnableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because no companion is active.");
                return false;
            }

            var inventoryWrapper = CompanionInventory;
            if (inventoryWrapper == null)
            {
                if (EnableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the inventory wrapper is unavailable.");
                return false;
            }

            var inventoryComponent = inventoryWrapper.InventoryComponent;
            if (inventoryComponent == null)
            {
                if (EnableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the inventory component is missing.");
                return false;
            }

            var bank = BankUI.Instance;
            if (bank == null)
            {
                if (EnableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the bank UI could not be resolved.");
                return false;
            }

            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject == null)
            {
                if (EnableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the player object could not be located.");
                return false;
            }

            if (!CompanionBankDepositAnchor.IsPlayerWithinDepositRange(playerObject.transform.position))
            {
                if (EnableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because no bank anchors are within range.");

                PublishRandomBankOutOfRangeMessage();

                var chat = ChatService.Instance;
                chat?.PublishGameMessage("There are no banks close by");
                return false;
            }

            if (IsCompanionInventoryEmpty(inventoryComponent))
            {
                if (EnableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the inventory is empty.");

                PublishRandomEmptyBankInventoryMessage();
                return false;
            }

            int moved = bank.DepositAllFromInventory(inventoryComponent);
            if (EnableDebugLogging)
            {
                if (moved > 0)
                    Debug.Log($"[Companion] Deposited {moved} item(s) from the companion inventory into the bank.");
                else
                    Debug.Log("[Companion] Deposit attempt completed but no items were moved (inventory empty or bank full).");
            }

            if (moved > 0)
            {
                PublishRandomBankDepositMessage();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the supplied inventory currently has any occupied slots.
        /// </summary>
        /// <param name="inventory">Companion inventory component to inspect.</param>
        /// <returns>
        /// <c>true</c> when the inventory exists but all slots are empty; otherwise <c>false</c>.
        /// </returns>
        private static bool IsCompanionInventoryEmpty(Inventory.Inventory inventory)
        {
            if (inventory == null)
                return true;

            var model = inventory.Model;
            if (model == null)
                return true;

            int slotCount = model.Size;
            for (int i = 0; i < slotCount; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item != null && entry.count > 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Emits a random companion chat line to confirm the deposit when items reach the bank.
        /// </summary>
        private static void PublishRandomBankDepositMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomBankDepositLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Emits a random flavour line when the companion is asked to bank items but has nothing to deposit.
        /// </summary>
        private static void PublishRandomEmptyBankInventoryMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomEmptyBankInventoryLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Emits a random reminder when the companion cannot deposit items because no banks are nearby.
        /// Keeps flavour consistent with the right-click and pet level bar bank interactions.
        /// </summary>
        private static void PublishRandomBankOutOfRangeMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomBankOutOfRangeLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Publishes a greeting when the companion automatically respawns after a login.
        /// Helps returning players feel acknowledged without requiring manual interaction.
        /// </summary>
        public static void PublishAutoSpawnGreeting()
        {
            if (!HasActiveCompanion)
                return;

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomAutoSpawnGreetingLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Publishes a random guard mode activation message to the companion chat channel.
        /// Ensures enabling guard mode feels responsive without spamming when toggled off.
        /// </summary>
        private static void PublishRandomGuardModeActivationMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomGuardActivationLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Publishes a random chat line whenever the companion is freshly spawned by the player.
        /// Adds flavour to manual summons triggered by dropping the companion’s charm item.
        /// </summary>
        private static void PublishRandomManualSpawnMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomManualSpawnGreetingLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Publishes a random farewell line when the player manually stores their companion.
        /// Keeps the pickup action flavourful so the companion acknowledges being dismissed.
        /// </summary>
        private static void PublishRandomManualStoreMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomManualStoreLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(GetCompanionDisplayName(), message);
        }

        /// <summary>
        /// Publishes a random guard mode deactivation message when the player disables guard mode.
        /// Keeps flavourful feedback flowing even as the companion relaxes from defence duty.
        /// </summary>
        private static void PublishRandomGuardModeDeactivationMessage()
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = CompanionChatLibrary.GetRandomGuardDeactivationLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(GetCompanionDisplayName(), message);
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
            {
                Debug.LogWarning("[Companion] Cannot command mining: target rock reference was null.");
                return false;
            }

            if (controller == null)
            {
                Debug.LogWarning("[Companion] Cannot command mining: companion controller has not been initialised.");
                return false;
            }

            if (controller.MiningController == null)
            {
                Debug.LogWarning("[Companion] Cannot command mining: companion mining controller is missing.");
                return false;
            }

            if (!HasActiveCompanion)
            {
                Debug.LogWarning("[Companion] Cannot command mining: the companion is not currently active.");
                return false;
            }

            if (CompanionSkillCooldownTimers.ShouldDeclineMiningRequest(controller.SkillCooldowns, out var cooldownResult))
            {
                Debug.LogWarning($"[Companion] Mining command outcome: accepted=False (result={cooldownResult}).");
                return false;
            }

            bool accepted = controller.MiningController.TryCommandMine(rock, out var result);
            if (!accepted && result == CompanionMiningCommandResult.InventoryFull)
                accepted = true;

            string message = $"[Companion] Mining command outcome: accepted={accepted} (result={result}).";
            if (accepted)
                Debug.Log(message);
            else
                Debug.LogWarning(message);

            return accepted;
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
            string rejectionReason = string.Empty;
            failureReason = CompanionMiningCommandResult.Unreachable;

            if (controller == null)
            {
                rejectionReason = "Companion controller has not been initialised.";
                failureReason = CompanionMiningCommandResult.RequirementsNotMet;
                Debug.LogWarning($"[Companion] Area mining command outcome: success=False, radius={radius}, reason={rejectionReason}");
                return false;
            }

            if (controller.MiningController == null)
            {
                rejectionReason = "Companion mining controller is missing.";
                failureReason = CompanionMiningCommandResult.RequirementsNotMet;
                Debug.LogWarning($"[Companion] Area mining command outcome: success=False, radius={radius}, reason={rejectionReason}");
                return false;
            }

            if (!HasActiveCompanion)
            {
                rejectionReason = "The companion is not currently active.";
                failureReason = CompanionMiningCommandResult.RequirementsNotMet;
                Debug.LogWarning($"[Companion] Area mining command outcome: success=False, radius={radius}, reason={rejectionReason}");
                return false;
            }

            if (CompanionSkillCooldownTimers.ShouldDeclineMiningRequest(controller.SkillCooldowns, out failureReason))
            {
                Debug.LogWarning($"[Companion] Area mining command outcome: success=False, radius={radius}, reason=Cooldown active.");
                return false;
            }

            bool accepted = controller.MiningController.TryStartAreaMining(radius, out failureReason);
            if (!accepted)
            {
                if (failureReason == CompanionMiningCommandResult.InventoryFull)
                {
                    string inventoryDetail = "Area mining aborted because the companion inventory is full.";
                    Debug.Log($"[Companion] Area mining command outcome: success=True, radius={radius}, reason={inventoryDetail}");
                    return true;
                }

                rejectionReason = $"The mining controller rejected the area mining request ({failureReason}).";
                Debug.LogWarning($"[Companion] Area mining command outcome: success=False, radius={radius}, reason={rejectionReason}");
                return false;
            }

            string successDetail = "Area mining routine started successfully.";
            Debug.Log($"[Companion] Area mining command outcome: success=True, radius={radius}, reason={successDetail}");
            return true;
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
            string message;

            switch (skill)
            {
                case SkillType.Hitpoints:
                    message = CompanionChatLibrary.GetRandomHitpointsLevelUpLine();
                    break;
                case SkillType.Defence:
                    message = CompanionChatLibrary.GetRandomDefenceLevelUpLine();
                    break;
                case SkillType.Strength:
                    message = CompanionChatLibrary.GetRandomStrengthLevelUpLine();
                    break;
                case SkillType.Attack:
                    message = CompanionChatLibrary.GetRandomAttackLevelUpLine();
                    break;
                case SkillType.Ranged:
                    message = CompanionChatLibrary.GetRandomRangedLevelUpLine();
                    break;
                case SkillType.Magic:
                    message = CompanionChatLibrary.GetRandomMagicLevelUpLine();
                    break;
                case SkillType.Beastmaster:
                    message = CompanionChatLibrary.GetRandomBeastmasterLevelUpLine();
                    break;
                case SkillType.Fishing:
                    message = CompanionChatLibrary.GetRandomFishingLevelUpLine();
                    break;
                case SkillType.Cooking:
                    message = CompanionChatLibrary.GetRandomCookingLevelUpLine();
                    break;
                case SkillType.Firemaking:
                    message = CompanionChatLibrary.GetRandomFiremakingLevelUpLine();
                    break;
                case SkillType.Woodcutting:
                    message = CompanionChatLibrary.GetRandomWoodcuttingLevelUpLine();
                    break;
                case SkillType.Mining:
                    message = CompanionChatLibrary.GetRandomMiningLevelUpLine();
                    break;
                default:
                {
                    string pronoun = ResolveCompanionPronoun();
                    string skillName = SkillNameUtility.GetSentenceName(skill);
                    message = $"Just levelled up {pronoun} {skillName} to level {level}!";
                    break;
                }
            }

            chat.PublishCompanionMessage(companionName, message);
        }

        /// <summary>
        /// Reacts to system chat messages so the companion acknowledges when the player's inventory is full.
        /// Ensures the companion only responds when active and applies a small cooldown to prevent spam.
        /// </summary>
        /// <param name="message">Chat message emitted by the game channel.</param>
        private static void HandleChatMessageReceived(ChatMessage message)
        {
            if (message.Channel != ChatChannel.Game)
                return;

            if (!HasActiveCompanion)
                return;

            if (string.IsNullOrWhiteSpace(message.Text))
                return;

            string trimmed = message.Text.Trim();
            if (trimmed.Length == 0)
                return;

            string normalised = trimmed.ToLowerInvariant();
            bool playerInventoryFull = string.Equals(normalised, PlayerInventoryFullGameMessage, StringComparison.Ordinal);
            bool combinedInventoryFull = string.Equals(normalised, PlayerAndCompanionInventoryFullGameMessage, StringComparison.Ordinal);

            if (!playerInventoryFull && !combinedInventoryFull)
                return;

            float now = Time.unscaledTime;
            if (now - lastInventoryFullReactionTime < InventoryFullChatCooldownSeconds)
                return;

            lastInventoryFullReactionTime = now;

            string companionLine = combinedInventoryFull
                ? CompanionChatLibrary.GetRandomPlayerAndCompanionInventoryFullLine()
                : CompanionChatLibrary.GetRandomPlayerInventoryFullLine();
            if (string.IsNullOrWhiteSpace(companionLine))
                return;

            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string companionName = GetCompanionDisplayName();
            chat.PublishCompanionMessage(companionName, companionLine);

            if (enableDebugLogging)
            {
                string context = combinedInventoryFull ? "(player+companion)" : "(player)";
                Debug.Log($"[Companion] Reacted to full inventory message {context} with: {companionLine}");
            }
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
            RemoveChatSubscription();
            activeDefinition = null;
            inventoryVisible = false;
            storedByPet = false;
            storedManually = false;
            companionWasActiveBeforePetSpawn = false;
            guardModeEnabled = false;
            guardModeLockedByCombatCooldown = false;
            combatLevel = 1;
            InventoryVisibilityChanged?.Invoke(false);
            UpdateEquipmentVisibility(false);
            GuardModeChanged?.Invoke(false);
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
