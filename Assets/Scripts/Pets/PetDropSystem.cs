using System;
using System.Collections.Generic;
using UnityEngine;
using Inventory;

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
        private static bool initialized;

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

#if PET_SAVE_SUPPORTED
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
#endif
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
        /// Attempt to roll for a pet drop.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, out PetDefinition pet)
        {
            return TryRollPet(sourceId, worldPosition, null, out pet);
        }

        /// <summary>
        /// Attempt to roll for a pet drop using a provided RNG.
        /// </summary>
        public static bool TryRollPet(string sourceId, Vector3 worldPosition, System.Random rng, out PetDefinition pet)
        {
            Initialize();
            pet = null;
            foreach (var table in tables)
            {
                foreach (var entry in table.entries)
                {
                    if (entry.pet == null || entry.oneInN <= 0)
                        continue;
                    if (!string.Equals(entry.sourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int roll = rng != null ? rng.Next(entry.oneInN) : UnityEngine.Random.Range(0, entry.oneInN);
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
            if (activePetGO != null)
            {
                UnityEngine.Object.Destroy(activePetGO);
                activePetGO = null;
                activePetDef = null;
#if PET_SAVE_SUPPORTED
                PetSaveBridge.Clear();
#endif
            }
        }

        /// <summary>
        /// Spawns a pet directly at the given world position.
        /// </summary>
        public static GameObject SpawnPet(PetDefinition pet, Vector3 position)
        {
            Initialize();
            return SpawnPetInternal(pet, position);
        }

        private static GameObject SpawnPetInternal(PetDefinition pet, Vector3 position)
        {
            if (pet == null)
                return null;

            DespawnActive();
            Vector3 spawnPos = position + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.5f);
            activePetGO = PetSpawner.Spawn(pet, spawnPos);
            activePetDef = pet;
#if PET_SAVE_SUPPORTED
            PetSaveBridge.Save(pet.id);
#endif
            PetToastUI.Show("You have a funny feeling like you're being followed…", pet.messageColor);
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
            if (tables.Count == 0 || tables[0].entries.Count == 0)
                return false;
            var entry = tables[0].entries[0];
            if (entry.pet == null)
                return false;
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