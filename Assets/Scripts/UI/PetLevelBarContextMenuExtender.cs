using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Beastmaster;
using Inventory;
using Skills;
using Skills.Cooking;
using UI;
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
        private static float cookAllCooldownEnd;
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
            if (mergeButton == null || pickupButton == null || cookAllButton == null)
                return;
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

            UpdateCookAllButton();
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

        private void UpdateCookAllButton()
        {
            if (cookAllButton == null)
                return;

            var pet = PetDropSystem.ActivePetObject;
            bool show = false;
            if (pet != null)
            {
                var exp = pet.GetComponent<PetExperience>();
                if (exp != null && exp.definition != null && exp.definition.id == "Mr Frying Pan")
                    show = true;
            }

            cookAllButton.gameObject.SetActive(show);
            if (!show)
                return;

            if (Time.time < cookAllCooldownEnd)
            {
                TimeSpan cd = TimeSpan.FromSeconds(cookAllCooldownEnd - Time.time);
                cookAllText.text = $"Cook All ({cd.Minutes:00}:{cd.Seconds:00})";
                cookAllButton.interactable = false;
            }
            else
            {
                cookAllText.text = "Cook All";
                cookAllButton.interactable = true;
            }
        }

        private void OnCookAllClicked()
        {
            if (Time.time < cookAllCooldownEnd)
                return;

            var pet = PetDropSystem.ActivePetObject;
            if (pet == null)
                return;

            var exp = pet.GetComponent<PetExperience>();
            int level = exp != null ? exp.Level : 1;

            CookAll();

            cookAllCooldownEnd = Time.time + GetCooldownSeconds(level);
            Hide();
        }

        private void CookAll()
        {
            EnsureRecipeLookup();

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return;

            var inventory = player.GetComponent<Inventory.Inventory>();
            var skills = player.GetComponent<SkillManager>();
            if (inventory == null)
                return;

            int totalXp = 0;

            for (int i = 0; i < inventory.size; i++)
            {
                var entry = inventory.GetSlot(i);
                if (entry.item == null || entry.count <= 0)
                    continue;
                if (!recipeLookup.TryGetValue(entry.item.id, out var recipe))
                    continue;

                var cookedItem = ItemDatabase.GetItem(recipe.cookedItemId);
                if (cookedItem == null)
                    continue;

                int qty = entry.count;
                inventory.RemoveItem(entry.item, qty);
                inventory.AddItem(cookedItem, qty);

                if (recipe.xp > 0)
                    totalXp += recipe.xp * qty;
            }

            if (skills != null && totalXp > 0)
            {
                skills.AddXP(SkillType.Cooking, totalXp);

                Transform anchor = player.transform;
                var anchorChild = player.transform.Find("FloatingTextAnchor");
                if (anchorChild != null)
                    anchor = anchorChild;
                FloatingText.Show($"+{totalXp} XP", anchor.position);
            }
        }

        private void EnsureRecipeLookup()
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

        private static float GetCooldownSeconds(int level)
        {
            if (level >= 99) return 60f;
            if (level >= 75) return 120f;
            if (level >= 50) return 180f;
            if (level >= 25) return 240f;
            return 300f;
        }
    }
}
