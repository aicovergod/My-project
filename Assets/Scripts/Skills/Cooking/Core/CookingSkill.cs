using System;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Util;
using UI;
using BankSystem;
using Pets;
using Skills.Outfits;
using Core.Save;
using Skills.Common;

namespace Skills.Cooking
{
    /// <summary>
    /// Handles the cooking skill including tick based processing of recipes and
    /// XP awards. Success removes a raw item and adds the cooked result. Failure
    /// simply removes the raw item.
    /// </summary>
    [DisallowMultipleComponent]
    public class CookingSkill : TickedSkillBehaviour
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Equipment equipment;
        [SerializeField] private Transform floatingTextAnchor;

        private SkillManager skills;
        private CookableRecipe currentRecipe;
        private int itemsRemaining;
        private int cookProgress;
        private const int CookIntervalTicks = 5;
        private SkillingOutfitProgress cookingOutfit;

        public event Action<CookableRecipe> OnStartCooking;
        public event Action OnStopCooking;
        public event Action<string, int> OnFoodCooked;
        public event Action<int> OnLevelUp;

        public int Level => skills != null ? skills.GetLevel(SkillType.Cooking) : 1;
        public float Xp => skills != null ? skills.GetXp(SkillType.Cooking) : 0f;
        public bool IsCooking => currentRecipe != null && itemsRemaining > 0;
        public float CookProgressNormalized => CookIntervalTicks <= 1 ? 0f : (float)cookProgress / (CookIntervalTicks - 1);

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            skills = GetComponent<SkillManager>();
            cookingOutfit = new SkillingOutfitProgress(new[]
            {
                "Chefs Hat",
                "Chefs Top",
                "Chefs Pants",
                "Chefs Boots",
                "Cooking Mittens"
            }, "CookingOutfitOwned");
        }

        private void OnDestroy()
        {
            SaveManager.Unregister(cookingOutfit);
        }

        public void StartCooking(CookableRecipe recipe, int quantity)
        {
            if (recipe == null || quantity <= 0)
                return;
            if (skills != null && skills.GetLevel(SkillType.Cooking) < recipe.requiredLevel)
                return;
            currentRecipe = recipe;
            itemsRemaining = quantity;
            cookProgress = 0;
            OnStartCooking?.Invoke(recipe);
        }

        public void StopCooking()
        {
            if (!IsCooking)
                return;
            currentRecipe = null;
            itemsRemaining = 0;
            cookProgress = 0;
            OnStopCooking?.Invoke();
        }

        protected override void HandleTick()
        {
            if (!IsCooking)
                return;
            cookProgress++;
            if (cookProgress >= CookIntervalTicks)
            {
                cookProgress = 0;
                AttemptCook();
            }
        }

        public static float CalculateBurnChance(int level, CookableRecipe recipe)
        {
            if (level >= recipe.noBurnLevel)
                return 0f;
            float relative = (recipe.noBurnLevel - level) /
                             (float)(recipe.noBurnLevel - recipe.requiredLevel);
            return recipe.burnChance * Mathf.Clamp01(relative);
        }

        private void AttemptCook()
        {
            if (currentRecipe == null || inventory == null)
            {
                StopCooking();
                return;
            }

            if (!inventory.RemoveItem(currentRecipe.rawItemId))
            {
                StopCooking();
                return;
            }

            itemsRemaining--;
            Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;

            int level = skills != null ? skills.GetLevel(SkillType.Cooking) : 1;
            float burnChance = CalculateBurnChance(level, currentRecipe);

            bool burned = UnityEngine.Random.value < burnChance;
            if (burned)
            {
                FloatingText.Show("Burned", anchor.position);
            }
            else
            {
                var cookedItem = ItemDatabase.GetItem(currentRecipe.cookedItemId);
                string cookedName = cookedItem != null ? cookedItem.itemName : currentRecipe.cookedItemId;
                var context = new GatheringRewardContext
                {
                    runner = this,
                    skills = skills,
                    skillType = SkillType.Cooking,
                    inventory = inventory,
                    petStorage = null,
                    item = cookedItem,
                    rewardDisplayName = cookedName,
                    quantity = 1,
                    xpPerItem = currentRecipe.xp,
                    petAssistExtraQuantity = 0,
                    floatingTextAnchor = floatingTextAnchor,
                    fallbackAnchor = transform,
                    equipment = equipment,
                    equipmentXpBonusEvaluator = data => data != null ? data.cookingXpBonusMultiplier : 0f,
                    showItemFloatingText = true,
                    showXpPopup = true,
                    xpPopupDelayTicks = 5f,
                    rewardMessageFormatter = qty => $"+{qty} {cookedName}",
                    onItemsGranted = result => OnFoodCooked?.Invoke(currentRecipe.cookedItemId, result.QuantityAwarded),
                    onXpApplied = result =>
                    {
                        if (PetDropSystem.ActivePet?.id == "Mr Frying Pan")
                            PetExperience.AddPetXp(result.XpGained);

                        if (result.LeveledUp && result.Anchor != null)
                        {
                            FloatingText.Show($"Cooking level {result.NewLevel}", result.Anchor.position);
                            OnLevelUp?.Invoke(result.NewLevel);
                        }
                    },
                    onSuccess = result =>
                    {
                        int petChance = Mathf.Max(5000, 10000 - (level - 1) * 100);
                        var anchorTransform = result.Anchor != null ? result.Anchor : transform;
                        PetDropSystem.TryRollPet("cooking", anchorTransform.position, skills, petChance, out _);
                        TryAwardCookingOutfitPiece();
                    },
                    onFailure = _ => StopCooking()
                };

                var rewardResult = GatheringRewardProcessor.Process(context);
                if (!rewardResult.Success)
                    return;
            }

            if (itemsRemaining <= 0)
                StopCooking();
        }
        public bool CanCook(CookableRecipe recipe, int quantity)
        {
            if (inventory == null || recipe == null)
                return false;
            var item = ItemDatabase.GetItem(recipe.rawItemId);
            if (item == null)
                return false;
            return inventory.GetItemCount(item) >= quantity;
        }

        private void TryAwardCookingOutfitPiece()
        {
            int roll = UnityEngine.Random.Range(0, 2500);
            if (SkillingOutfitProgress.DebugChance)
                Debug.Log($"[Cooking] Skilling outfit roll: {roll} (chance 1 in 2500)");
            if (roll != 0)
                return;

            var missing = new List<string>();
            foreach (var id in cookingOutfit.allPieceIds)
            {
                if (!cookingOutfit.owned.Contains(id))
                    missing.Add(id);
            }
            if (missing.Count == 0)
                return;

            string chosen = missing[UnityEngine.Random.Range(0, missing.Count)];
            var item = ItemDatabase.GetItem(chosen);
            bool added = inventory != null && item != null && inventory.AddItem(item);
            if (!added)
            {
                BankUI.Instance?.AddItemToBank(item);
                PetToastUI.Show("A piece of cooking outfit has been added to your bank");
            }
            else
            {
                PetToastUI.Show("You've received a piece of cooking outfit");
            }
            cookingOutfit.owned.Add(chosen);
        }
    }
}
