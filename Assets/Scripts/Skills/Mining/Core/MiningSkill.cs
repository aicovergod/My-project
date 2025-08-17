using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Util;
using Skills.Mining;

namespace Skills.Mining
{
    /// <summary>
    /// Handles XP, level, and mining tick logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningSkill : MonoBehaviour, ITickable
    {
        [SerializeField] private XpTable xpTable;
        [SerializeField] private Inventory.InventoryModel inventory;
        [SerializeField] private Transform floatingTextAnchor;
        [SerializeField] private MonoBehaviour saveProvider; // Optional custom save provider

        private IMiningSave save;

        private int xp;
        private int level;

        private MineableRock currentRock;
        private PickaxeDefinition currentPickaxe;
        private int swingProgress;

        private Dictionary<string, ItemData> oreItems;

        public event System.Action<MineableRock> OnStartMining;
        public event System.Action OnStopMining;
        public event System.Action<string, int> OnOreGained;
        public event System.Action<int> OnLevelUp;

        public int Level => level;
        public int Xp => xp;
        public bool IsMining => currentRock != null;
        public MineableRock CurrentRock => currentRock;
        public int CurrentSwingSpeedTicks => currentPickaxe?.SwingSpeedTicks ?? 0;
        public float SwingProgressNormalized
            => currentPickaxe == null || currentPickaxe.SwingSpeedTicks <= 1
                ? 0f
                : (float)swingProgress / (currentPickaxe.SwingSpeedTicks - 1);

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.InventoryModel>();
            save = saveProvider as IMiningSave ?? new SaveManagerMiningSave();
            xp = save.LoadXp();
            level = xpTable != null ? xpTable.GetLevel(xp) : 1;

            PreloadOreItems();
        }

        private Coroutine tickerCoroutine;

        private void OnEnable()
        {
            TrySubscribeToTicker();
            StartCoroutine(SaveLoop());
        }

        private void OnDisable()
        {
            if (Ticker.Instance != null)
                Ticker.Instance.Unsubscribe(this);
            if (tickerCoroutine != null)
                StopCoroutine(tickerCoroutine);
        }

        private void TrySubscribeToTicker()
        {
            if (Ticker.Instance != null)
            {
                Ticker.Instance.Subscribe(this);
                Debug.Log("MiningSkill subscribed to ticker.");
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
            Debug.Log("MiningSkill subscribed to ticker after waiting.");
        }

        private IEnumerator SaveLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(10f);
                save.SaveXp(xp);
            }
        }

        private void OnApplicationQuit()
        {
            save.SaveXp(xp);
        }

        public void OnTick()
        {
            if (!IsMining)
                return;

            // Stop immediately if the current rock has already been depleted
            if (currentRock == null || currentRock.IsDepleted)
            {
                StopMining();
                return;
            }

            swingProgress++;
            Debug.Log($"Mining tick: {swingProgress}/{currentPickaxe.SwingSpeedTicks}");
            if (swingProgress >= currentPickaxe.SwingSpeedTicks)
            {
                swingProgress = 0;
                AttemptMine();
            }
        }

        private void AttemptMine()
        {
            float baseChance = 0.35f;
            float penalty = 0.0025f * Mathf.Max(currentRock.RockDef.Ore.LevelRequirement - 1, 0);
            float chance = baseChance + (level * 0.005f) + currentPickaxe.MiningRollBonus - penalty;
            chance = Mathf.Clamp(chance, 0.05f, 0.90f);

            if (Random.value <= chance)
            {
                OreDefinition ore = currentRock.MineOre();
                if (ore != null)
                {
                    oreItems.TryGetValue(ore.Id, out var item);
                    bool added = false;
                    if (item != null && inventory != null)
                        added = inventory.AddItem(item);

                    Vector3 anchorPos = floatingTextAnchor != null
                        ? floatingTextAnchor.position
                        : transform.position;

                    if (!added)
                    {
                        FloatingText.Show("Your inventory is full", anchorPos);
                        StopMining();
                        return;
                    }

                    xp += ore.XpPerOre;
                    FloatingText.Show($"+1 {ore.DisplayName}", anchorPos);
                    OnOreGained?.Invoke(ore.Id, 1);

                    int newLevel = xpTable.GetLevel(xp);
                    if (newLevel > level)
                    {
                        level = newLevel;
                        FloatingText.Show($"Mining level {level}", anchorPos);
                        OnLevelUp?.Invoke(level);
                    }
                }

                if (currentRock.IsDepleted)
                    StopMining();
            }
            else
            {
                Debug.Log($"Failed to mine {currentRock.name}");
            }
        }

        public void StartMining(MineableRock rock, PickaxeDefinition pickaxe)
        {
            if (rock == null || pickaxe == null)
                return;

            currentRock = rock;
            currentPickaxe = pickaxe;
            swingProgress = 0;
            Debug.Log($"Started mining {rock.name}");
            OnStartMining?.Invoke(rock);
        }

        public void StopMining()
        {
            if (!IsMining)
                return;

            Debug.Log("Stopped mining");
            currentRock = null;
            currentPickaxe = null;
            swingProgress = 0;
            OnStopMining?.Invoke();
        }

        public bool CanAddOre(OreDefinition ore)
        {
            if (inventory == null || ore == null)
                return true;
            if (oreItems == null)
                PreloadOreItems();
            if (!oreItems.TryGetValue(ore.Id, out var item) || item == null)
                return true;
            return inventory.CanAddItem(item);
        }

        private void PreloadOreItems()
        {
            oreItems = new Dictionary<string, ItemData>();
            var items = Resources.LoadAll<ItemData>("Item");
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.id))
                    oreItems[item.id] = item;
            }
        }
    }
}
