using System;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Skills;

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
        public static bool GuardModeEnabled { get; set; }
        private static bool initialized;
        private static bool quittingRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInit()
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
                SpawnPetInternal(activePetDef, pos);
            }

            if (!quittingRegistered)
            {
                Application.quitting += SaveOnQuit;
                quittingRegistered = true;
            }
        }

        private static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            // Load any drop tables placed under Resources/PetDropTables
            var loaded = Resources.LoadAll<PetDropTable>("PetDropTables");
            RegisterTables(loaded);

            // Create updater for debug hotkey
            var go = new GameObject("~PetDropSystem");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<PetDropSystemUpdater>();
            GameObject.DontDestroyOnLoad(go);

            string saved = PetSaveBridge.Load();
            if (!string.IsNullOrEmpty(saved))
            {
                var pet = FindPetById(saved);
                if (pet != null)
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    Vector3 pos = player != null ? player.transform.position : Vector3.zero;
                    SpawnPetInternal(pet, pos);
                }
            }
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
            return TryRollPet(sourceId, worldPosition, beastmasterLevel, null, out pet);
        }

        /// <summary>
        /// Attempt to roll for a pet drop using the player's skills.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, SkillManager skills, out PetDefinition pet)
        {
            int level = skills != null ? skills.GetLevel(SkillType.Beastmaster) : 1;
            return TryRollPet(sourceId, worldPosition, level, null, out pet);
        }

        /// <summary>
        /// Attempt to roll for a pet drop using a provided RNG and Beastmaster level.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, int beastmasterLevel, System.Random rng, out PetDefinition pet)
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

                    int effectiveOneInN = entry.oneInN;
                    int above = beastmasterLevel - entry.requiredBeastmasterLevel;
                    if (above > 0 && entry.bonusDropMultiplier > 0f)
                    {
                        float mult = 1f + above * entry.bonusDropMultiplier;
                        effectiveOneInN = Mathf.Max(1, Mathf.FloorToInt(entry.oneInN / mult));
                    }

                    int roll = rng != null ? rng.Next(effectiveOneInN) : UnityEngine.Random.Range(0, effectiveOneInN);
                    if (roll == 0)
                    {
                        SpawnPetInternal(entry.pet, worldPosition);
                        pet = entry.pet;
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
                UnityEngine.Object.Destroy(activePetGO);
                activePetGO = null;
                activePetDef = null;
                PetSaveBridge.Clear();
            }
        }

        /// <summary>
        /// Spawns a pet directly at the given world position.
        /// </summary>
        public static GameObject SpawnPet(PetDefinition pet, Vector3 position)
        {
            Initialize();
            if (Beastmaster.PetMergeController.Instance != null && Beastmaster.PetMergeController.Instance.IsMerged)
                return null;
            return SpawnPetInternal(pet, position);
        }

        private static GameObject SpawnPetInternal(PetDefinition pet, Vector3 position)
        {
            if (pet == null)
            {
                Debug.LogError("SpawnPetInternal called with null pet.");
                return null;
            }

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
            PetToastUI.Show("You have a funny feeling like you're being followed…", pet.messageColor);
            Debug.Log($"Spawned pet '{pet.displayName}' at {spawnPos}.");
            return activePetGO;
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

        private class PetDropSystemUpdater : MonoBehaviour
        {
            private void Update()
            {
                if (Input.GetKeyDown(KeyCode.P))
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    Vector3 pos = player != null ? player.transform.position : Vector3.zero;
                    DebugForceFirstDrop(pos);
                }
            }
        }

        internal static bool DebugForceFirstDrop(Vector3 position)
        {
            Initialize();
            if (Beastmaster.PetMergeController.Instance != null && Beastmaster.PetMergeController.Instance.IsMerged)
                return false;
            if (tables.Count == 0 || tables[0].entries.Count == 0)
            {
                Debug.LogWarning("DebugForceFirstDrop: no pet drop tables or entries found.");
                return false;
            }
            var entry = tables[0].entries[0];
            if (entry.pet == null)
            {
                Debug.LogWarning("DebugForceFirstDrop: first entry has no pet.");
                return false;
            }
            Debug.Log("DebugForceFirstDrop spawning first pet drop.");
            SpawnPetInternal(entry.pet, position);
            return true;
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
- Press P in Play Mode to force a test drop.
- Skill/action systems can call PetDropSystem.TryRollPet("mining", hitPos, out var pet) after a successful action tick.
*/