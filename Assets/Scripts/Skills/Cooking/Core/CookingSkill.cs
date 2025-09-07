using System;
using System.Collections;
using UnityEngine;
using Inventory;
using Util;
using UI;
using BankSystem;
using Pets;
using Skills.Outfits;
using Core.Save;

namespace Skills.Cooking
{
    /// <summary>
    /// Handles the cooking skill including tick based processing of recipes and
    /// XP awards. Success removes a raw item and adds the cooked result. Failure
    /// simply removes the raw item.
    /// </summary>
    [DisallowMultipleComponent]
    public class CookingSkill : MonoBehaviour, ITickable
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Transform floatingTextAnchor;

        private SkillManager skills;
        private CookableRecipe currentRecipe;
        private int itemsRemaining;
        private int cookProgress;
        private const int CookIntervalTicks = 5;
        private Coroutine tickerCoroutine;
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
            skills = GetComponent<SkillManager>();
            cookingOutfit = new SkillingOutfitProgress(new[]
            {
                "Cooking Hat",
                "Cooking Top",
                "Cooking Pants",
                "Cooking Boots",
                "Cooking Gloves"
            }, "CookingOutfitOwned");
        }

        private void OnEnable()
        {
            TrySubscribeToTicker();
        }

        private void OnDisable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
            if (tickerCoroutine != null)
                StopCoroutine(tickerCoroutine);
        }

        private void OnDestroy()
        {
            SaveManager.Unregister(cookingOutfit);
        }

        private void TrySubscribeToTicker()
        {
            if (Ticker.Instance != null)
            {
                Ticker.Instance.Subscribe(this);
            }
            else
            {
                tickerCoroutine = StartCoroutine(WaitForTicker());
            }
        }

        private IEnumerator WaitForTicker()
        {
            while (Ticker.Instance == null)
                yield return null;
            Ticker.Instance.Subscribe(this);
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

        public void OnTick()
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
            float burnChance = currentRecipe.burnChance;
            if (level > currentRecipe.requiredLevel)
            {
                float t = Mathf.InverseLerp(currentRecipe.requiredLevel, currentRecipe.noBurnLevel, level);
                burnChance = Mathf.Lerp(currentRecipe.burnChance, 0f, t);
            }

            bool burned = UnityEngine.Random.value < burnChance;
            if (burned)
            {
                FloatingText.Show("Burned", anchor.position);
            }
            else
            {
                var cookedItem = ItemDatabase.GetItem(currentRecipe.cookedItemId);
                if (cookedItem == null || !inventory.AddItem(cookedItem, 1))
                {
                    FloatingText.Show("Your inventory is full", anchor.position);
                    StopCooking();
                    return;
                }

                int xpGain = currentRecipe.xp;
                int oldLevel = skills.GetLevel(SkillType.Cooking);
                int newLevel = skills.AddXP(SkillType.Cooking, xpGain);
                FloatingText.Show($"+1 {cookedItem.itemName}", anchor.position);
                StartCoroutine(ShowXpGainDelayed(xpGain, anchor));
                OnFoodCooked?.Invoke(currentRecipe.cookedItemId, 1);
                TryAwardCookingOutfitPiece();
                if (newLevel > oldLevel)
                {
                    FloatingText.Show($"Cooking level {newLevel}", anchor.position);
                    OnLevelUp?.Invoke(newLevel);
                }
            }

            if (itemsRemaining <= 0)
                StopCooking();
        }

        private IEnumerator ShowXpGainDelayed(int xp, Transform anchor)
        {
            yield return new WaitForSeconds(Ticker.TickDuration * 5f);
            if (anchor != null)
                FloatingText.Show($"+{xp} XP", anchor.position);
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

            var missing = new System.Collections.Generic.List<string>();
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
