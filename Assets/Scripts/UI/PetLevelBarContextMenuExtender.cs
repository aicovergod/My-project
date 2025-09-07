using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Beastmaster;
using Inventory;
using Skills.Cooking;
using Object = UnityEngine.Object;

namespace Pets
{
    /// <summary>
    /// Extends the pet level bar context menu with Merge/Unmerge options.
    /// </summary>
    public partial class PetLevelBarMenu
    {
        private Button mergeButton;
        private Text mergeText;
        private Button pickupButton;
        private PetMergeController mergeController;

        private Button cookAllButton;
        private Text cookAllText;
        private float cookAllReadyTime;
        private static Dictionary<string, CookableRecipe> recipeLookup;

        partial void OnMenuCreated(Transform menuRoot)
        {
            mergeButton = CreateButton(menuRoot, "Merge");
            mergeText = mergeButton.GetComponentInChildren<Text>();
            mergeButton.onClick.AddListener(OnMergeClicked);

            pickupButton = CreateButton(menuRoot, "Pick up");
            pickupButton.onClick.AddListener(OnPickupClicked);

            cookAllButton = CreateButton(menuRoot, "Cook All");
            cookAllText = cookAllButton.GetComponentInChildren<Text>();
            cookAllButton.onClick.AddListener(OnCookAllClicked);
        }

        partial void OnMenuShown()
        {
            if (mergeButton != null && pickupButton != null)
            {
                if (mergeController == null)
                    mergeController = Object.FindObjectOfType<PetMergeController>();
                pickupButton.gameObject.SetActive(PetDropSystem.ActivePetObject != null);
                if (mergeController == null)
                {
                    mergeButton.gameObject.SetActive(false);
                }
                else
                {
                    mergeButton.gameObject.SetActive(true);
                    if (mergeController.IsMerged)
                    {
                        mergeText.text = "Unmerge";
                        mergeButton.interactable = true;
                    }
                    else if (mergeController.IsOnCooldown)
                    {
                        TimeSpan cd = TimeSpan.FromSeconds(mergeController.CooldownRemaining);
                        mergeText.text = $"Merge ({cd.Minutes:00}:{cd.Seconds:00})";
                        mergeButton.interactable = false;
                    }
                    else if (!mergeController.CanMerge)
                    {
                        mergeText.text = "Merge";
                        mergeButton.interactable = false;
                    }
                    else
                    {
                        mergeText.text = "Merge";
                        mergeButton.interactable = true;
                    }
                }
            }

            if (cookAllButton != null)
            {
                var petObj = PetDropSystem.ActivePetObject;
                var petExp = petObj != null ? petObj.GetComponent<PetExperience>() : null;
                bool isFryingPan = petExp != null && petExp.definition != null && petExp.definition.id == "Mr Frying Pan";
                cookAllButton.gameObject.SetActive(isFryingPan);
                if (isFryingPan)
                {
                    float remaining = cookAllReadyTime - Time.time;
                    if (remaining > 0f)
                    {
                        TimeSpan cd = TimeSpan.FromSeconds(remaining);
                        cookAllText.text = $"Cook All ({cd.Minutes:00}:{cd.Seconds:00})";
                        cookAllButton.interactable = false;
                    }
                    else
                    {
                        cookAllText.text = "Cook All";
                        cookAllButton.interactable = true;
                    }
                }
            }
        }

        private void OnMergeClicked()
        {
            if (mergeController == null)
                return;
            if (mergeController.IsMerged)
                mergeController.EndMerge();
            else
                mergeController.TryStartMerge();
            Hide();
        }

        private void OnPickupClicked()
        {
            var pet = PetDropSystem.ActivePetObject;
            if (pet != null)
            {
                var clickable = pet.GetComponent<PetClickable>();
                clickable?.Pickup();
            }
            Hide();
        }

        private void OnCookAllClicked()
        {
            var petObj = PetDropSystem.ActivePetObject;
            var petExp = petObj != null ? petObj.GetComponent<PetExperience>() : null;
            int level = petExp != null ? petExp.Level : 1;
            float cooldown = 300f;
            if (level >= 99)
                cooldown = 60f;
            else if (level >= 75)
                cooldown = 120f;
            else if (level >= 50)
                cooldown = 180f;
            else if (level >= 25)
                cooldown = 240f;

            StartCoroutine(CookAllRoutine());
            cookAllReadyTime = Time.time + cooldown;
            Hide();
        }

        private IEnumerator CookAllRoutine()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                yield break;
            var inv = player.GetComponent<Inventory.Inventory>();
            var skill = player.GetComponent<CookingSkill>();
            if (inv == null || skill == null)
                yield break;
            EnsureRecipeLookup();
            for (int i = 0; i < inv.size; i++)
            {
                var entry = inv.GetSlot(i);
                var item = entry.item;
                if (item == null)
                    continue;
                if (!recipeLookup.TryGetValue(item.id, out var recipe))
                    continue;
                if (skill.Level < recipe.requiredLevel)
                    continue;
                int quantity = inv.GetItemCount(item);
                if (quantity <= 0)
                    continue;
                skill.StartCooking(recipe, quantity);
                while (skill.IsCooking)
                    yield return null;
            }
            inv.ClearSelection();
        }

        private static void EnsureRecipeLookup()
        {
            if (recipeLookup != null)
                return;
            recipeLookup = new Dictionary<string, CookableRecipe>();
            var recipes = Resources.LoadAll<CookableRecipe>("CookingDatabase");
            foreach (var r in recipes)
            {
                if (r != null && !string.IsNullOrEmpty(r.rawItemId))
                    recipeLookup[r.rawItemId] = r;
            }
        }
    }
}
