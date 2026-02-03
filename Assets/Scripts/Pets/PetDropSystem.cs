using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Inventory;
using Skills;
using BankSystem;
using Core.Save;
using Companions;

namespace Pets
{
    /// <summary>
    /// Rolls for pet drops and manages the active pet instance.
    /// </summary>
    public static class PetDropSystem
    {
        private static readonly List<PetDropTable> tables = new();
        private static readonly Dictionary<ItemData, PetDefinition> itemToPet = new();
        private static GameObject activePetGO;
        private static PetDefinition activePetDef;
        public static PetDefinition ActivePet => activePetDef;
        public static GameObject ActivePetObject => activePetGO;
        public static PetCombatController ActivePetCombat => activePetGO != null ? activePetGO.GetComponent<PetCombatController>() : null;

        /// <summary>
        ///     Determines whether the supplied definition matches the currently active pet instance.
        ///     The comparison succeeds when the references are identical or when their IDs match.
        /// </summary>
        /// <param name="definition">Definition to evaluate against the active pet.</param>
        /// <returns>True when <paramref name="definition"/> represents the active pet.</returns>
        public static bool IsActivePetDefinition(PetDefinition definition)
        {
            if (definition == null || activePetDef == null)
                return false;

            if (ReferenceEquals(activePetDef, definition))
                return true;

            if (string.IsNullOrEmpty(definition.id) || string.IsNullOrEmpty(activePetDef.id))
                return false;

            return string.Equals(activePetDef.id, definition.id, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Provides the floating-text controller for the active pet instance when available.
        /// </summary>
        public static PetFloatingTextController ActivePetFloatingText => activePetGO != null ? activePetGO.GetComponent<PetFloatingTextController>() : null;
        public static bool DebugPetRolls { get; set; }
        public static bool GuardModeEnabled { get; set; }
        public static bool PetInventoryVisible { get; set; }
        private static bool initialized;
        private static bool quittingRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInit()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

            if (!quittingRegistered)
            {
                Application.quitting += SaveOnQuit;
                quittingRegistered = true;
            }
        }

        /// <summary>
        /// Invoked after each scene load so saved pets refresh once a profile is active and the scene is fully authenticated.
        /// </summary>
        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Initialize();

            var player = GameObject.FindGameObjectWithTag("Player");

            if (activePetGO != null)
            {
                if (player != null)
                {
                    activePetGO.transform.position = player.transform.position;
                    var follower = activePetGO.GetComponent<PetFollower>();
                    if (follower != null)
                        follower.SetPlayer(player.transform);
                }

                var expExisting = activePetGO.GetComponent<PetExperience>();
                PetLevelBarHUD.CreateForPet(expExisting);
            }
            else if (activePetDef != null)
            {
                Vector3 pos = player != null ? player.transform.position : Vector3.zero;
                if (activePetDef.spawnAsCompanion)
                    CompanionManager.TrySpawnCompanion(activePetDef);
                else
                    SpawnPetInternal(activePetDef, pos, true);
            }
        }

        private static void Initialize()
        {
            if (!initialized)
            {
                initialized = true;

                // Load any drop tables placed under Resources/PetDropTables
                var loaded = Resources.LoadAll<PetDropTable>("PetDropTables");
                RegisterTables(loaded);
            }

            TryRestoreSavedPet();
        }

        /// <summary>
        /// Attempts to respawn the saved pet when none is currently active.
        /// </summary>
        private static void TryRestoreSavedPet()
        {
            if (string.IsNullOrEmpty(SaveManager.ActiveProfileId))
                return;

            if (activePetGO != null || activePetDef != null)
                return;

            string saved = PetSaveBridge.Load();
            if (string.IsNullOrEmpty(saved))
                return;

            var pet = FindPetById(saved);
            if (pet == null)
                return;

            var player = GameObject.FindGameObjectWithTag("Player");
            Vector3 pos = player != null ? player.transform.position : Vector3.zero;
            if (pet.spawnAsCompanion)
                SpawnCompanionPet(pet, true);
            else
                SpawnPetInternal(pet, pos, true);
        }

        /// <summary>
        /// Registers additional drop tables at runtime.
        /// </summary>
        public static void RegisterTables(IEnumerable<PetDropTable> dropTables)
        {
            if (dropTables == null)
                return;

            foreach (var table in dropTables)
            {
                if (table == null || tables.Contains(table))
                    continue;
                tables.Add(table);
                foreach (var e in table.entries)
                {
                    if (e.pet != null && e.pet.pickupItem != null)
                        itemToPet[e.pet.pickupItem] = e.pet;
                }
            }
        }

        /// <summary>
        /// Attempt to roll for a pet drop using the player's Beastmaster level.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, int beastmasterLevel, out PetDefinition pet)
        {
            return TryRollPet(sourceId, worldPosition, beastmasterLevel, null, null, out pet);
        }

        /// <summary>
        /// Attempt to roll for a pet drop using the player's skills.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, SkillManager skills, out PetDefinition pet)
        {
            int level = skills != null ? skills.GetLevel(SkillType.Beastmaster) : 1;
            return TryRollPet(sourceId, worldPosition, level, null, null, out pet);
        }

        /// <summary>
        /// Attempt to roll for a pet drop using the player's Beastmaster level and an override drop rate.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, int beastmasterLevel, int oneInNOverride, out PetDefinition pet)
        {
            return TryRollPet(sourceId, worldPosition, beastmasterLevel, oneInNOverride, null, out pet);
        }

        /// <summary>
        /// Attempt to roll for a pet drop using the player's skills and an override drop rate.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, SkillManager skills, int oneInNOverride, out PetDefinition pet)
        {
            int level = skills != null ? skills.GetLevel(SkillType.Beastmaster) : 1;
            return TryRollPet(sourceId, worldPosition, level, oneInNOverride, null, out pet);
        }

        /// <summary>
        /// Attempt to roll for a pet drop using a provided RNG and Beastmaster level.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, int beastmasterLevel, System.Random rng, out PetDefinition pet)
        {
            return TryRollPet(sourceId, worldPosition, beastmasterLevel, null, rng, out pet);
        }

        private static bool TryRollPet(string sourceId, Vector3 worldPosition, int beastmasterLevel, int? oneInNOverride, System.Random rng, out PetDefinition pet)
        {
            Initialize();
            pet = null;
            if (Beastmaster.PetMergeController.Instance != null && Beastmaster.PetMergeController.Instance.IsMerged)
                return false;
            foreach (var table in tables)
            {
                foreach (var entry in table.entries)
                {
                    if (entry.pet == null || entry.oneInN <= 0)
                        continue;
                    if (!string.Equals(entry.sourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (beastmasterLevel < entry.requiredBeastmasterLevel)
                        continue;

                    int baseOneInN = oneInNOverride.HasValue && oneInNOverride.Value > 0 ? oneInNOverride.Value : entry.oneInN;
                    int effectiveOneInN = baseOneInN;
                    int above = beastmasterLevel - entry.requiredBeastmasterLevel;
                    if (above > 0 && entry.bonusDropMultiplier > 0f)
                    {
                        float mult = 1f + above * entry.bonusDropMultiplier;
                        effectiveOneInN = Mathf.Max(1, Mathf.FloorToInt(baseOneInN / mult));
                    }

                    int roll = rng != null ? rng.Next(effectiveOneInN) : UnityEngine.Random.Range(0, effectiveOneInN);
                    if (DebugPetRolls)
                        Debug.Log($"{sourceId} pet roll: {roll} (chance 1 in {effectiveOneInN})");
                    if (roll == 0)
                    {
                        pet = entry.pet;
                        if (activePetGO == null)
                        {
                            SpawnPet(entry.pet, worldPosition);
                        }
                        else
                        {
                            var player = GameObject.FindGameObjectWithTag("Player");
                            var inventory = player != null ? player.GetComponent<Inventory.Inventory>() : null;
                            if (inventory != null && inventory.CanAddItem(entry.pet.pickupItem))
                            {
                                inventory.AddItem(entry.pet.pickupItem);
                                PetToastUI.Show("You feel something crawl inside your backpack", entry.pet.messageColor);
                            }
                            else
                            {
                                BankUI.Instance?.AddItemToBank(entry.pet.pickupItem);
                                PetToastUI.Show("You have a feeling something has snuck into your bank", entry.pet.messageColor);
                            }
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Despawn the currently active pet, if any.
        /// </summary>
        public static void DespawnActive()
        {
            PetLevelBarHUD.DestroyInstance();
            if (activePetGO != null)
            {
                var storage = activePetGO.GetComponent<PetStorage>();
                storage?.Close();
                PetInventoryVisible = false;
                UnityEngine.Object.Destroy(activePetGO);
                activePetGO = null;
                activePetDef = null;
                PetSaveBridge.Clear();
            }
            else if (activePetDef != null && activePetDef.spawnAsCompanion)
            {
                activePetDef = null;
                PetSaveBridge.Clear();
            }

            CompanionManager.HandlePetDespawned();
        }

        /// <summary>
        /// Clears the tracked companion definition when the player manually stores it in their inventory.
        /// </summary>
        public static void HandleCompanionManuallyStored()
        {
            if (activePetDef == null || !activePetDef.spawnAsCompanion)
                return;

            activePetDef = null;
            PetInventoryVisible = false;
            PetSaveBridge.Clear();
        }

        /// <summary>
        ///     Attempts to display floating text using the active pet's anchor when available.
        /// </summary>
        /// <param name="message">Message to display above the active pet.</param>
        /// <param name="color">Optional colour override.</param>
        /// <param name="size">Optional text scale override.</param>
        /// <param name="background">Optional background sprite for the text.</param>
        /// <returns>True when the message is shown successfully.</returns>
        public static bool TryShowFloatingText(string message, Color? color = null, float? size = null, Sprite background = null)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            var controller = ActivePetFloatingText;
            if (controller == null)
                return false;

            return controller.TryShowMessage(message, color, size, background);
        }

        /// <summary>
        /// Spawns a pet directly at the given world position.
        /// </summary>
        /// <param name="pet">Definition describing the pet that should manifest.</param>
        /// <param name="position">World position where the follower should appear.</param>
        /// <param name="allowAutoRestore">
        ///     Controls whether a previously active companion should automatically return once the new
        ///     pet despawns. Pass <c>false</c> when a manual pet summon intentionally replaces the
        ///     companion so it remains stored until the player calls it back in.
        /// </param>
        public static GameObject SpawnPet(PetDefinition pet, Vector3 position, bool allowAutoRestore = true)
        {
            Initialize();

            if (pet != null && pet.spawnAsCompanion)
            {
                if (activePetGO != null)
                    DespawnActive();

                return SpawnCompanionPet(pet, false);
            }

            if (Beastmaster.PetMergeController.Instance != null && Beastmaster.PetMergeController.Instance.IsMerged)
                return null;
            CompanionManager.HandlePrePetSpawn(allowAutoRestore);
            return SpawnPetInternal(pet, position);
        }

        private static GameObject SpawnPetInternal(PetDefinition pet, Vector3 position, bool isRespawnFromSave = false)
        {
            if (pet == null)
            {
                Debug.LogError("SpawnPetInternal called with null pet.");
                return null;
            }

            // Remember whether the pet backpack UI was visible before we clear the existing pet.
            bool reopenPetInventory = PetInventoryVisible;

            DespawnActive();
            Vector3 spawnPos = position + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.5f);

            // Find the player once at spawn time so the follower knows whom to follow.
            var playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
            activePetGO = PetSpawner.Spawn(pet, spawnPos, playerTransform);
            GameObject.DontDestroyOnLoad(activePetGO);
            activePetDef = pet;
            PetSaveBridge.Save(pet.id);
            var exp = activePetGO.GetComponent<PetExperience>();
            PetLevelBarHUD.CreateForPet(exp);
            ShowSpawnToast(pet, isRespawnFromSave);
            Debug.Log($"Spawned pet '{pet.displayName}' at {spawnPos}.");

            var playerInventory = playerTransform != null ? playerTransform.GetComponent<Inventory.Inventory>() : null;
            var storage = activePetGO.GetComponent<PetStorage>();

            // Restore the remembered visibility flag so any later toggles remain consistent.
            PetInventoryVisible = reopenPetInventory;
            if (PetInventoryVisible)
            {
                if (playerInventory != null && playerInventory.IsOpen && !playerInventory.BankOpen)
                {
                    // The player still has their main inventory open, so reopen the pet storage UI next frame.
                    storage?.StartCoroutine(storage.OpenDelayed());
                    PetInventoryVisible = true;
                }
                else
                {
                    storage?.Close();
                }
            }
            else
            {
                storage?.Close();
            }

            ActivePetFloatingText?.RefreshAnchorPosition();

            return activePetGO;
        }

        /// <summary>
        /// Displays the appropriate toast message for the pet spawn context.
        /// </summary>
        /// <param name="pet">The pet that was spawned.</param>
        /// <param name="isRespawnFromSave">True when the pet is being restored after a load, false when this is a brand-new drop.</param>
        private static void ShowSpawnToast(PetDefinition pet, bool isRespawnFromSave)
        {
            if (pet == null)
                return;

            // When companions automatically respawn after a login we avoid surfacing a toast.
            // The login flow already greets the player through the companion chat pipeline,
            // and the legacy "funny feeling" toast caused duplicate messaging on every load.
            if (pet.spawnAsCompanion && isRespawnFromSave)
                return;

            if (isRespawnFromSave)
            {
                PetToastUI.Show("Your pet appears by your side", pet.messageColor);
            }
            else
            {
                PetToastUI.Show("You have a funny feeling like you're being followed…", pet.messageColor);
            }
        }

        /// <summary>
        /// Retrieves the pet associated with an inventory item.
        /// </summary>
        public static PetDefinition FindPetByItem(ItemData item)
        {
            Initialize();
            itemToPet.TryGetValue(item, out var pet);
            return pet;
        }

        /// <summary>
        /// Handles spawning pets that should manifest through the companion pipeline instead of the follower pipeline.
        /// Persists the selection so reloads restore the same companion.
        /// </summary>
        /// <param name="pet">Definition that should become the active companion.</param>
        /// <param name="isRespawnFromSave">True when restoring after a load, false when triggered from an item drop.</param>
        private static GameObject SpawnCompanionPet(PetDefinition pet, bool isRespawnFromSave)
        {
            if (pet == null)
            {
                Debug.LogError("SpawnCompanionPet called with null pet.");
                return null;
            }

            bool replacingExisting = activePetDef != null && activePetDef.spawnAsCompanion &&
                string.Equals(activePetDef.id, pet.id, StringComparison.Ordinal);

            activePetGO = null;
            activePetDef = pet;
            PetSaveBridge.Save(pet.id);
            PetInventoryVisible = false;

            CompanionManager.TrySpawnCompanion(pet, isRespawnFromSave);

            if (isRespawnFromSave)
                CompanionManager.PublishAutoSpawnGreeting();

            if (!replacingExisting)
                ShowSpawnToast(pet, isRespawnFromSave);

            return CompanionManager.CompanionObject;
        }

        private static void SaveOnQuit()
        {
            if (activePetDef != null)
                PetSaveBridge.Save(activePetDef.id);
        }

        private static PetDefinition FindPetById(string id)
        {
            foreach (var table in tables)
            {
                foreach (var entry in table.entries)
                {
                    if (entry.pet != null && entry.pet.id == id)
                        return entry.pet;
                }
            }
            return null;
        }

    }
}

/*
Hookup Checklist:
- Tag the player Player.
- Ensure default font Legacy runtime.ttf is available (place in Assets/Fonts/ if missing).
- Put sample sprite at Assets/Game/Sprites/Pets/chick_idle.png (point filter, no compression).
- Assign DefaultPetDrops.asset into a PetDropSystem bootstrapping MonoBehaviour in the first loaded scene,
  or place it under Resources/PetDropTables for auto-loading.
- Skill/action systems can call PetDropSystem.TryRollPet("mining", hitPos, out var pet) after a successful action tick.
 */
