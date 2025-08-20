using System;
using UnityEngine;
using Combat;
using Pets;
using UI;

namespace Beastmaster
{
    /// <summary>
    /// Controls merging the player with their active combat pet.
    /// </summary>
    [DisallowMultipleComponent]
    public class PetMergeController : MonoBehaviour
    {
        private static PetMergeController instance;
        public static PetMergeController Instance => instance;
        [SerializeField] private MergeConfig config;
        [SerializeField] private MonoBehaviour beastmasterServiceComponent;
        [SerializeField] private MonoBehaviour petServiceComponent;
        [SerializeField] private PlayerVisualBinder visualBinder;
        [SerializeField] private PlayerCombatBinder combatBinder;
        [SerializeField] private MergeHudTimer hudTimer;

        private IBeastmasterService beastmaster;
        private IPetService petService;

        private bool merged;
        private float durationRemaining;
        private float cooldownRemaining;

        private const string MERGE_KEY = "BM_Merge_Remaining";
        private const string COOLDOWN_KEY = "BM_Merge_Cooldown";

        public bool IsMerged => merged;
        public bool IsOnCooldown => cooldownRemaining > 0f;
        public float CooldownRemaining => cooldownRemaining;
        public bool CanMerge
        {
            get
            {
                if (merged || cooldownRemaining > 0f)
                    return false;
                if (beastmaster == null || petService == null)
                    return false;
                if (beastmaster.CurrentLevel < 50)
                    return false;
                return petService.TryGetActiveCombatPet(out _);
            }
        }

        private void Awake()
        {
            instance = this;

            if (beastmasterServiceComponent == null)
                beastmasterServiceComponent = FindObjectOfType<BeastmasterServiceAdapter>();
            if (petServiceComponent == null)
                petServiceComponent = FindObjectOfType<PetServiceAdapter>();

            beastmaster = beastmasterServiceComponent as IBeastmasterService;
            petService = petServiceComponent as IPetService;

            if (hudTimer == null)
                hudTimer = GetComponentInChildren<MergeHudTimer>(true);

            if (beastmaster == null)
                Debug.LogWarning("PetMergeController missing IBeastmasterService component.");
            if (petService == null)
                Debug.LogWarning("PetMergeController missing IPetService component.");
            if (hudTimer == null)
                Debug.LogWarning("PetMergeController missing MergeHudTimer component.");

            LoadState();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void Update()
        {
            if (merged)
            {
                durationRemaining -= Time.deltaTime;
                if (durationRemaining <= 0f)
                {
                    EndMerge();
                }
                else
                {
                    hudTimer?.UpdateTime(TimeSpan.FromSeconds(durationRemaining));
                }
            }
            else if (cooldownRemaining > 0f)
            {
                cooldownRemaining -= Time.deltaTime;
            }
        }

        /// <summary>Attempt to start merging with the active pet.</summary>
        public bool TryStartMerge()
        {
            if (!CanMerge)
                return false;
            if (!config.TryGetMergeParams(beastmaster.CurrentLevel, out var dur, out var cd, out var locked) || locked)
                return false;
            if (!petService.TryGetActiveCombatPet(out var pet))
                return false;

            durationRemaining = (float)dur.TotalSeconds;
            cooldownRemaining = (float)cd.TotalSeconds;
            merged = true;

            petService.HideActivePet();
            var visuals = petService.GetVisuals(pet);
            visualBinder?.ApplyPetLook(visuals);
            var combat = petService.GetCombatProfile(pet);
            combatBinder?.UseProfile(combat);
            hudTimer?.Show(TimeSpan.FromSeconds(durationRemaining));
            SaveState();
            return true;
        }

        /// <summary>End the current merge, restoring player state and starting cooldown.</summary>
        public void EndMerge()
        {
            if (!merged)
                return;
            merged = false;
            visualBinder?.RestorePlayerLook();
            combatBinder?.RestorePlayerProfile();
            petService?.ShowActivePet(transform.position);
            hudTimer?.Hide();
            SaveState();
        }

        /// <summary>Reset merge and cooldown timers to zero.</summary>
        public void ResetMergeTimer()
        {
            if (merged)
                EndMerge();
            cooldownRemaining = 0f;
            durationRemaining = 0f;
            hudTimer?.Hide();
            SaveState();
        }

        private void OnDisable()
        {
            SaveState();
        }

        private void OnApplicationQuit()
        {
            SaveState();
        }

        private void SaveState()
        {
            PlayerPrefs.SetFloat(MERGE_KEY, merged ? durationRemaining : 0f);
            PlayerPrefs.SetFloat(COOLDOWN_KEY, cooldownRemaining);
            PlayerPrefs.Save();
        }

        private void LoadState()
        {
            durationRemaining = PlayerPrefs.GetFloat(MERGE_KEY, 0f);
            cooldownRemaining = PlayerPrefs.GetFloat(COOLDOWN_KEY, 0f);
            if (durationRemaining > 0f && petService != null && petService.TryGetActiveCombatPet(out var pet))
            {
                merged = true;
                petService.HideActivePet();
                var visuals = petService.GetVisuals(pet);
                visualBinder?.ApplyPetLook(visuals);
                var combat = petService.GetCombatProfile(pet);
                combatBinder?.UseProfile(combat);
                hudTimer?.Show(TimeSpan.FromSeconds(durationRemaining));
            }
            else
            {
                durationRemaining = 0f;
            }
        }
    }
}
