using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Util;
using Skills.Mining; // reuse XP table
using Core.Save;
using Pets;
using Fishing;
using Fishing.Bycatch;

namespace Skills.Fishing
{
    [DisallowMultipleComponent]
    public class FishingSkill : MonoBehaviour, ITickable, ISaveable
    {
        [SerializeField] private XpTable xpTable;
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Transform floatingTextAnchor;
        [SerializeField] private MonoBehaviour saveProvider; // optional custom save provider
        [SerializeField] private BycatchManager bycatchManager;

        private IFishingSave save;

        private int xp;
        private int level;

        private FishableSpot currentSpot;
        private FishingToolDefinition currentTool;
        private int catchProgress;
        private int currentIntervalTicks;
        private int bycatchRollIndex;

        private Dictionary<string, ItemData> fishItems;

        public event System.Action<FishableSpot> OnStartFishing;
        public event System.Action OnStopFishing;
        public event System.Action<string, int> OnFishCaught;
        public event System.Action<int> OnLevelUp;

        public int Level => level;
        public int Xp => xp;
        public bool IsFishing => currentSpot != null;
        public FishableSpot CurrentSpot => currentSpot;
        public FishingToolDefinition CurrentTool => currentTool;
        public float CatchProgressNormalized => currentIntervalTicks <= 1 ? 0f : (float)catchProgress / (currentIntervalTicks - 1);
        public int CurrentCatchIntervalTicks => currentIntervalTicks;

        private Coroutine tickerCoroutine;

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            save = saveProvider as IFishingSave ?? new SaveManagerFishingSave();
            PreloadFishItems();
        }

        private void OnEnable()
        {
            SaveManager.Register(this);
            TrySubscribeToTicker();
            StartCoroutine(SaveLoop());
        }

        private void OnDisable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
            if (tickerCoroutine != null)
                StopCoroutine(tickerCoroutine);
            Save();
            SaveManager.Unregister(this);
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

        private IEnumerator SaveLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(10f);
                Save();
            }
        }

        public void OnTick()
        {
            if (!IsFishing)
                return;
            if (currentSpot == null || currentSpot.IsDepleted)
            {
                StopFishing();
                return;
            }
            catchProgress++;
            if (catchProgress >= currentIntervalTicks)
            {
                catchProgress = 0;
                AttemptCatch();
            }
        }

        private void AttemptCatch()
        {
            var fish = GetRandomFish(currentSpot.def);
            if (fish == null)
            {
                StopFishing();
                return;
            }
            if (!string.IsNullOrEmpty(currentSpot.def.BaitItemId))
            {
                Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
                if (inventory == null || !inventory.RemoveItem(currentSpot.def.BaitItemId))
                {
                    FloatingText.Show("You need bait", anchor.position);
                    StopFishing();
                    return;
                }
                var baitItem = ItemDatabase.GetItem(currentSpot.def.BaitItemId);
                if (baitItem != null)
                    FloatingText.Show($"-1 {baitItem.itemName}", anchor.position);
                else
                    FloatingText.Show("-1 bait", anchor.position);
            }

            float baseChance = 0.35f;
            float penalty = 0.0025f * Mathf.Max(fish.RequiredLevel - 1, 0);
            float chance = baseChance + (level * 0.005f) + currentTool.CatchBonus * 0.01f - penalty;
            chance = Mathf.Clamp(chance, 0.05f, 0.90f);

            if (Random.value <= chance)
            {
                int amount = PetDropSystem.ActivePet?.id == "Heron" ? 2 : 1;
                if (amount > 1)
                    BeastmasterXp.TryGrantFromPetAssist(fish.Xp * (amount - 1));

                fishItems.TryGetValue(fish.ItemId, out var item);
                bool added = true;
                var petStorage = PetDropSystem.ActivePet?.id == "Heron" && PetDropSystem.ActivePetObject != null
                    ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                    : null;

                Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;

                for (int i = 0; i < amount; i++)
                {
                    bool stepAdded = false;
                    if (item != null && inventory != null)
                        stepAdded = inventory.AddItem(item, 1);
                    if (!stepAdded && petStorage != null)
                        stepAdded = petStorage.StoreItem(item, 1);
                    if (!stepAdded)
                    {
                        added = false;
                        break;
                    }
                }

                if (!added)
                {
                    FloatingText.Show("Your inventory is full", anchor.position);
                    StopFishing();
                    return;
                }

                xp += fish.Xp * amount;
                FloatingText.Show($"+{amount} {fish.DisplayName}", anchor.position);
                StartCoroutine(ShowXpGainDelayed(fish.Xp * amount, anchor));
                OnFishCaught?.Invoke(fish.Id, amount);

                TryRollBycatch(anchor);

                int newLevel = xpTable.GetLevel(xp);
                if (newLevel > level)
                {
                    level = newLevel;
                    FloatingText.Show($"Fishing level {level}", anchor.position);
                    OnLevelUp?.Invoke(level);
                }

                currentSpot.OnFishCaught();
                if (currentSpot.IsDepleted)
                    StopFishing();
            }
        }

        private FishDefinition GetRandomFish(FishingSpotDefinition spot)
        {
            if (spot == null) return null;
            var eligible = new List<FishDefinition>();
            foreach (var f in spot.AvailableFish)
            {
                if (f != null && level >= f.RequiredLevel)
                    eligible.Add(f);
            }
            if (eligible.Count == 0)
                return null;
            return eligible[UnityEngine.Random.Range(0, eligible.Count)];
        }

        private IEnumerator ShowXpGainDelayed(int gain, Transform anchor)
        {
            yield return new WaitForSeconds(Ticker.TickDuration * 5f);
            if (anchor != null)
                FloatingText.Show($"+{gain} XP", anchor.position);
        }

        private void TryRollBycatch(Transform anchor)
        {
            if (bycatchManager == null || currentSpot == null || currentTool == null)
                return;

            var waterType = currentSpot.def != null ? currentSpot.def.WaterType : WaterType.Any;
            int streak = bycatchManager.GetStreak(waterType);
            var ctx = new BycatchRollContext
            {
                playerLevel = level,
                hasBait = !string.IsNullOrEmpty(currentSpot.def.BaitItemId),
                waterType = waterType,
                tool = MapTool(currentTool),
                luck = 0f,
                spotRarityMultiplier = 1f,
                noRareStreakForThisWater = streak,
                playerIdHash = gameObject.GetInstanceID(),
                nodeHash = currentSpot.def != null ? currentSpot.def.Id.GetHashCode() : currentSpot.GetInstanceID(),
                rollIndex = bycatchRollIndex++
            };

            var res = bycatchManager.Roll(ctx);
            bycatchManager.ApplyStreakResult(waterType, res);
            if (res.IsNone)
                return;

            var itemData = ItemDatabase.GetItem(res.item.ItemId);
            if (itemData == null || inventory == null || !inventory.AddItem(itemData, res.quantity))
            {
                FloatingText.Show("Your inventory is full", anchor.position);
                return;
            }

            FloatingText.Show($"+{res.quantity} {res.item.DisplayName}", anchor.position);
        }

        private Fishing.Bycatch.FishingTool MapTool(FishingToolDefinition tool)
        {
            if (tool == null)
                return Fishing.Bycatch.FishingTool.Any;
            string name = tool.DisplayName?.Replace(" ", "");
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out Fishing.Bycatch.FishingTool res))
                return res;
            name = tool.Id?.Replace(" ", "");
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out res))
                return res;
            return Fishing.Bycatch.FishingTool.Any;
        }

        public void StartFishing(FishableSpot spot, FishingToolDefinition tool)
        {
            if (spot == null || tool == null)
                return;
            if (!string.IsNullOrEmpty(spot.def.BaitItemId))
            {
                if (inventory == null || !inventory.HasItem(spot.def.BaitItemId))
                {
                    Transform anchor = floatingTextAnchor != null ? floatingTextAnchor : transform;
                    FloatingText.Show("You need bait", anchor.position);
                    return;
                }
            }
            currentSpot = spot;
            currentTool = tool;
            catchProgress = 0;
            currentIntervalTicks = Mathf.Max(1, Mathf.RoundToInt(spot.def.CatchIntervalTicks / Mathf.Max(0.01f, tool.SwingSpeedMultiplier)));
            currentSpot.IsBusy = true;
            OnStartFishing?.Invoke(spot);
        }

        public void StopFishing()
        {
            if (!IsFishing)
                return;
            if (currentSpot != null)
                currentSpot.IsBusy = false;
            currentSpot = null;
            currentTool = null;
            catchProgress = 0;
            currentIntervalTicks = 0;
            OnStopFishing?.Invoke();
        }

        public bool CanAddFish(FishDefinition fish)
        {
            if (inventory == null || fish == null)
                return true;
            if (fishItems == null)
                PreloadFishItems();
            if (!fishItems.TryGetValue(fish.ItemId, out var item) || item == null)
                return true;

            int amount = PetDropSystem.ActivePet?.id == "Heron" ? 2 : 1;
            if (inventory.CanAddItem(item, amount))
                return true;

            if (amount > 1)
            {
                var petStorage = PetDropSystem.ActivePetObject != null
                    ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                    : null;
                var petInv = petStorage != null
                    ? petStorage.GetComponent<Inventory.Inventory>()
                    : null;

                if (inventory.CanAddItem(item, 1) && petInv != null && petInv.CanAddItem(item, 1))
                    return true;

                if (petInv != null && petInv.CanAddItem(item, amount))
                    return true;
            }
            return false;
        }

        public void DebugSetLevel(int newLevel)
        {
            if (xpTable == null)
                return;
            newLevel = Mathf.Clamp(newLevel, 1, 99);
            xp = xpTable.GetXpForLevel(newLevel);
            level = newLevel;
            OnLevelUp?.Invoke(level);
        }

        public void Save()
        {
            save.SaveXp(xp);
        }

        public void Load()
        {
            xp = save.LoadXp();
            level = xpTable != null ? xpTable.GetLevel(xp) : 1;
        }

        private void PreloadFishItems()
        {
            fishItems = new Dictionary<string, ItemData>();
            var items = Resources.LoadAll<ItemData>("Item");
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.id))
                    fishItems[item.id] = item;
            }
        }
    }

    public interface IFishingSave
    {
        int LoadXp();
        void SaveXp(int xp);
    }

    public class SaveManagerFishingSave : IFishingSave
    {
        private const string Key = "fishing_xp";

        public int LoadXp()
        {
            return SaveManager.Load<int>(Key);
        }

        public void SaveXp(int xp)
        {
            SaveManager.Save(Key, xp);
        }
    }
}
