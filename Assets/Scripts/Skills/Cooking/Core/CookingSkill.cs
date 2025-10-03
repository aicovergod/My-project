using System;
using UnityEngine;
using Inventory;
using Util;
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
        [SerializeField, Tooltip("Enables verbose debug logging for cooking actions.")]
        private bool enableDebugLogging;

        private SkillManager skills;
        private CookableRecipe currentRecipe;
        private int itemsRemaining;
        private readonly TickProgressTracker cookProgressTracker = new TickProgressTracker();
        private const int CookIntervalTicks = 5;
        private SkillingOutfitProgress cookingOutfit;

        public event Action<CookableRecipe> OnStartCooking;
        public event Action OnStopCooking;
        public event Action<string, int> OnFoodCooked;
        public event Action<int> OnLevelUp;

        public int Level => skills != null ? skills.GetLevel(SkillType.Cooking) : 1;
        public float Xp => skills != null ? skills.GetXp(SkillType.Cooking) : 0f;
        public CookingObject ActiveCookingObject { get; private set; }
        public bool IsCooking => currentRecipe != null && itemsRemaining > 0 && ActiveCookingObject != null;
        public float CookProgressNormalized
        {
            get
            {
                int required = cookProgressTracker.RequiredTicks;
                if (required <= 1)
                    return 0f;

                return Mathf.Clamp01((float)cookProgressTracker.ProgressTicks / (required - 1));
            }
        }

        /// <summary>
        ///     Number of OSRS-style ticks required to cook a single item.
        /// </summary>
        public int CookTicksPerItem => CookIntervalTicks;

        /// <summary>
        ///     Gets or sets the runtime flag controlling verbose debug logging for this skill.
        /// </summary>
        public bool EnableDebugLogging
        {
            get => enableDebugLogging;
            set => enableDebugLogging = value;
        }

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            skills = GetComponent<SkillManager>();
            cookProgressTracker.TickAdvanced += HandleCookProgressAdvanced;
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
            cookProgressTracker.TickAdvanced -= HandleCookProgressAdvanced;
        }

        /// <summary>
        ///     Attempts to begin a cooking session at the supplied station.
        ///     Validates the player's level and ingredient availability before starting.
        /// </summary>
        /// <param name="station">World object representing the cooking station.</param>
        /// <param name="recipe">Recipe to cook.</param>
        /// <param name="quantity">Number of raw items to process.</param>
        /// <param name="failureMessage">Feedback explaining why the action failed.</param>
        /// <returns><c>true</c> if the session began successfully; otherwise <c>false</c>.</returns>
        public bool TryStartCooking(CookingObject station, CookableRecipe recipe, int quantity, out string failureMessage)
        {
            failureMessage = string.Empty;

            if (station == null || recipe == null)
            {
                failureMessage = "You can't cook here";
                return false;
            }

            if (quantity <= 0)
            {
                failureMessage = "You need something raw to cook";
                return false;
            }

            if (skills != null && skills.GetLevel(SkillType.Cooking) < recipe.requiredLevel)
            {
                failureMessage = $"You need Cooking level {recipe.requiredLevel}";
                return false;
            }

            if (inventory == null)
            {
                failureMessage = "You can't cook here";
                return false;
            }

            var rawItem = ItemDatabase.GetItem(recipe.rawItemId);
            if (rawItem == null)
            {
                failureMessage = "You can't cook that";
                return false;
            }

            int available = inventory.GetItemCount(rawItem);
            if (available <= 0)
            {
                failureMessage = "You need something raw to cook";
                return false;
            }

            if (quantity > available)
                quantity = available;

            if (IsCooking)
                StopCooking();

            ActiveCookingObject = station;
            currentRecipe = recipe;
            itemsRemaining = quantity;
            cookProgressTracker.Reset(CookIntervalTicks);
            LogDebug($"Started cooking {recipe.cookedItemId} x{quantity}");
            OnStartCooking?.Invoke(recipe);
            return true;
        }

        /// <summary>
        ///     Stops the current cooking session, clearing the active station and notifying listeners.
        /// </summary>
        public void StopCooking()
        {
            bool hadActiveSession = IsCooking || ActiveCookingObject != null;

            currentRecipe = null;
            itemsRemaining = 0;
            cookProgressTracker.Reset(0);
            ActiveCookingObject = null;

            if (!hadActiveSession)
                return;

            LogDebug("Stopped cooking");
            OnStopCooking?.Invoke();
        }

        protected override bool LogTickerSubscription => enableDebugLogging;

        protected override void HandleTick()
        {
            if (!IsCooking)
                return;

            if (cookProgressTracker.Advance())
            {
                AttemptCook();
                if (IsCooking)
                    cookProgressTracker.Reset(CookIntervalTicks);
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
                LogDebug("Cooking aborted because recipe or inventory reference was lost.");
                StopCooking();
                return;
            }

            if (!inventory.RemoveItem(currentRecipe.rawItemId))
            {
                LogDebug("Failed to remove raw ingredient; stopping cooking session.");
                StopCooking();
                return;
            }

            itemsRemaining--;
            Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
            Vector3? stationPosition = ActiveCookingObject != null ? ActiveCookingObject.transform.position : (Vector3?)null;

            int level = skills != null ? skills.GetLevel(SkillType.Cooking) : 1;
            float burnChance = CalculateBurnChance(level, currentRecipe);

            bool burned = UnityEngine.Random.value < burnChance;
            if (burned)
            {
                bool displayed = false;
                if (stationPosition.HasValue)
                    displayed = GatheringFloatingTextService.TryShowNow("Burned", anchor, stationPosition.Value);

                if (!displayed && !stationPosition.HasValue)
                    GatheringFloatingTextService.TryShowAtAnchor("Burned", anchor);
                LogDebug($"Burned {currentRecipe.cookedItemId} (burn chance {burnChance:P2})");
            }
            else
            {
                var cookedItem = ItemDatabase.GetItem(currentRecipe.cookedItemId);
                string cookedName = cookedItem != null ? cookedItem.itemName : currentRecipe.cookedItemId;
                var context = GatheringRewardContextBuilder.BuildContext(new GatheringRewardContextBuilder.ContextArgs
                {
                    Runner = this,
                    Skills = skills,
                    SkillType = SkillType.Cooking,
                    Inventory = inventory,
                    PetStorage = null,
                    Item = cookedItem,
                    RewardDisplayName = cookedName,
                    Quantity = 1,
                    XpPerItem = currentRecipe.xp,
                    PetAssistExtraQuantity = 0,
                    FloatingTextAnchor = floatingTextAnchor,
                    FallbackAnchor = transform,
                    ResourcePosition = stationPosition,
                    Equipment = equipment,
                    EquipmentXpBonusEvaluator = data => data != null ? data.cookingXpBonusMultiplier : 0f,
                    RewardMessageFormatter = qty => $"+{qty} {cookedName}",
                    OnItemsGranted = result => OnFoodCooked?.Invoke(currentRecipe.cookedItemId, result.QuantityAwarded),
                    OnXpAppliedBeforeLevelCheck = result =>
                    {
                        if (PetDropSystem.ActivePet?.id == "Mr Frying Pan")
                            PetExperience.AddPetXp(result.XpGained);
                    },
                    OnSuccess = result =>
                    {
                        int petChance = Mathf.Max(5000, 10000 - (level - 1) * 100);
                        SkillingPetRewarder.TryRollPet("cooking", skills, result.Anchor ?? transform, petChance);
                        TryAwardCookingOutfitPiece();
                    },
                    OnFailure = _ => StopCooking(),
                    LevelUpFloatingTextFormatter = result => $"Cooking level {result.NewLevel}",
                    OnLevelUp = newLevel => OnLevelUp?.Invoke(newLevel)
                });

                var rewardResult = GatheringRewardProcessor.Process(context);
                if (!rewardResult.Success)
                    return;

                LogDebug($"Successfully cooked {cookedName} (burn chance {burnChance:P2})");
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

        private bool TryAwardCookingOutfitPiece()
        {
            return SkillingOutfitRewarder.TryAwardPiece(
                cookingOutfit,
                inventory,
                BankUI.Instance,
                UnityEngine.Random.Range,
                "Cooking",
                "You've received a piece of cooking outfit",
                "A piece of cooking outfit has been added to your bank");
        }

        /// <summary>
        ///     Emits a formatted debug message when <see cref="enableDebugLogging"/> is enabled.
        /// </summary>
        /// <param name="message">Message to output to the Unity console.</param>
        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
                return;

            Debug.Log($"[CookingSkill] {message}");
        }

        private void HandleCookProgressAdvanced(int progress, int required)
        {
            if (!IsCooking)
                return;

            LogDebug($"Cooking tick: {progress}/{required}");
        }
    }
}
