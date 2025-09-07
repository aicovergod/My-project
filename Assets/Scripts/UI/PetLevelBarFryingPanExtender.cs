using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Inventory;
using Skills.Cooking;

namespace Pets
{
    public partial class PetLevelBarMenu
    {
        private Button cookAllButton;
        private Text cookAllText;
        private float cookAllReadyTime;
        private static Dictionary<string, CookableRecipe> recipeLookup;

        partial void OnMenuCreated(Transform menuRoot)
        {
            cookAllButton = CreateButton(menuRoot, "Cook All");
            cookAllText = cookAllButton.GetComponentInChildren<Text>();
            cookAllButton.onClick.AddListener(OnCookAllClicked);
        }

        partial void OnMenuShown()
        {
            if (cookAllButton == null)
                return;
            var petObj = PetDropSystem.ActivePetObject;
            var petExp = petObj != null ? petObj.GetComponent<PetExperience>() : null;
            bool isFryingPan = petExp != null && petExp.definition != null && petExp.definition.id == "Mr Frying Pan";
            cookAllButton.gameObject.SetActive(isFryingPan);
            if (!isFryingPan)
                return;

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
