using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Inventory;
using Util;
using UI;
using Skills;
using Pets;
using Quests;
using Random = UnityEngine.Random;

namespace Skills.Woodcutting
{
    /// <summary>
    /// Handles XP, level, and woodcutting tick logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class WoodcuttingSkill : MonoBehaviour, ITickable
    {
        [SerializeField] private Inventory.Inventory inventory;
        [SerializeField] private Equipment equipment;
        [SerializeField] private Transform floatingTextAnchor;

        private TreeNode currentTree;
        private AxeDefinition currentAxe;
        private int chopProgress;
        private int currentIntervalTicks;

        private SkillManager skills;

        private Dictionary<string, ItemData> logItems;
        private int questLogCount;

        public event System.Action<TreeNode> OnStartChopping;
        public event System.Action OnStopChopping;
        public event System.Action<string, int> OnLogGained;
        public event System.Action<int> OnLevelUp;

        public int Level => skills != null ? skills.GetLevel(SkillType.Woodcutting) : 1;
        public float Xp => skills != null ? skills.GetXp(SkillType.Woodcutting) : 0f;
        public bool IsChopping => currentTree != null;
        public TreeNode CurrentTree => currentTree;
        public int CurrentChopIntervalTicks => currentIntervalTicks;
        public AxeDefinition CurrentAxe => currentAxe;
        public float ChopProgressNormalized
            => currentIntervalTicks <= 1 ? 0f : (float)chopProgress / (currentIntervalTicks - 1);

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<Inventory.Inventory>();
            if (equipment == null)
                equipment = GetComponent<Equipment>();
            skills = GetComponent<SkillManager>();
            PreloadLogItems();
        }

        private Coroutine tickerCoroutine;

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

        private void TrySubscribeToTicker()
        {
            if (Ticker.Instance != null)
            {
                Ticker.Instance.Subscribe(this);
                Debug.Log("WoodcuttingSkill subscribed to ticker.");
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
            Debug.Log("WoodcuttingSkill subscribed to ticker after waiting.");
        }

        public void OnTick()
        {
            if (!IsChopping)
                return;

            if (currentTree == null || currentTree.IsDepleted)
            {
                StopChopping();
                return;
            }

            chopProgress++;
            if (chopProgress >= currentIntervalTicks)
            {
                chopProgress = 0;
                AttemptChop();
            }
        }

        private void AttemptChop()
        {
            float baseChance = 0.35f;
            float penalty = 0.0025f * Mathf.Max(currentTree.def.RequiredWoodcuttingLevel - 1, 0);
            int level = skills.GetLevel(SkillType.Woodcutting);
            float chance = baseChance + (level * 0.005f) + currentAxe.Power * 0.01f - penalty;
            chance = Mathf.Clamp(chance, 0.05f, 0.90f);

            if (Random.value <= chance)
            {
                string logId = currentTree.def.LogItemId;
                logItems.TryGetValue(logId, out var item);
                int amount = PetDropSystem.ActivePet?.id == "Beaver" ? 2 : 1;
                if (amount > 1)
                    BeastmasterXp.TryGrantFromPetAssist(currentTree.def.XpPerLog * (amount - 1));
                bool added = true;
                var petStorage = PetDropSystem.ActivePet?.id == "Beaver" && PetDropSystem.ActivePetObject != null
                    ? PetDropSystem.ActivePetObject.GetComponent<PetStorage>()
                    : null;

                Transform anchorTransform = floatingTextAnchor != null ? floatingTextAnchor : transform;
                Vector3 anchorPos = anchorTransform.position;

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
                    FloatingText.Show("Your inventory is full", anchorPos);
                    StopChopping();
                    return;
                }

                float xpBonus = 0f;
                if (equipment != null)
                {
                    foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                    {
                        if (slot == EquipmentSlot.None)
                            continue;
                        var entry = equipment.GetEquipped(slot);
                        if (entry.item != null)
                            xpBonus += entry.item.woodcuttingXpBonusMultiplier;
                    }
                }
                int xpGain = Mathf.RoundToInt(currentTree.def.XpPerLog * amount * (1f + xpBonus));
                int oldLevel = skills.GetLevel(SkillType.Woodcutting);
                int newLevel = skills.AddXP(SkillType.Woodcutting, xpGain);
                string logName = item != null ? item.itemName : currentTree.def.DisplayName;
                FloatingText.Show($"+{amount} {logName}", anchorPos);
                StartCoroutine(ShowXpGainDelayed(xpGain, anchorTransform));
                OnLogGained?.Invoke(logId, amount);

                if (currentTree.def.PetDropChance > 0)
                    PetDropSystem.TryRollPet("woodcutting", currentTree.transform.position, skills, currentTree.def.PetDropChance, out _);

                if (QuestManager.Instance != null && QuestManager.Instance.IsQuestActive("ToolsOfSurvival"))
                {
                    var quest = QuestManager.Instance.GetQuest("ToolsOfSurvival");
                    var step = quest?.Steps.Find(s => s.StepID == "ChopLogs");
                    if (step != null && !step.IsComplete)
                    {
                        questLogCount += amount;
                        if (questLogCount >= 3)
                            QuestManager.Instance.UpdateStep("ToolsOfSurvival", "ChopLogs");
                    }
                }

                if (newLevel > oldLevel)
                {
                    FloatingText.Show($"Woodcutting level {newLevel}", anchorPos);
                    OnLevelUp?.Invoke(newLevel);
                }

                currentTree.OnLogChopped();
                if (currentTree.IsDepleted)
                    StopChopping();
            }
            else
            {
                Debug.Log($"Failed to chop {currentTree.name}");
            }
        }

        private IEnumerator ShowXpGainDelayed(int xpGain, Transform anchor)
        {
            yield return new WaitForSeconds(Ticker.TickDuration * 5f);
            if (anchor != null)
            {
                FloatingText.Show($"+{xpGain} XP", anchor.position);
            }
        }

        public void StartChopping(TreeNode tree, AxeDefinition axe)
        {
            if (tree == null || axe == null)
                return;

            currentTree = tree;
            currentAxe = axe;
            chopProgress = 0;
            currentIntervalTicks = Mathf.Max(1, Mathf.RoundToInt(tree.def.ChopIntervalTicks / Mathf.Max(0.01f, axe.SwingSpeedMultiplier)));
            Debug.Log($"Started chopping {tree.name}");
            currentTree.IsBusy = true;
            OnStartChopping?.Invoke(tree);
        }

        public void StopChopping()
        {
            if (!IsChopping)
                return;

            Debug.Log("Stopped chopping");
            if (currentTree != null)
                currentTree.IsBusy = false;
            currentTree = null;
            currentAxe = null;
            chopProgress = 0;
            currentIntervalTicks = 0;
            OnStopChopping?.Invoke();
        }

        public bool CanAddLog(TreeDefinition tree)
        {
            if (inventory == null || tree == null)
                return true;
            if (logItems == null)
                PreloadLogItems();
            if (!logItems.TryGetValue(tree.LogItemId, out var item) || item == null)
                return true;

            int amount = PetDropSystem.ActivePet?.id == "Beaver" ? 2 : 1;
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

        /// <summary>
        /// Debug helper to directly set the woodcutting level via the SkillManager.
        /// </summary>
        public void DebugSetLevel(int newLevel)
        {
            skills?.DebugSetLevel(SkillType.Woodcutting, Mathf.Clamp(newLevel, 1, 99));
            OnLevelUp?.Invoke(Level);
        }

        private void PreloadLogItems()
        {
            logItems = new Dictionary<string, ItemData>();
            var items = Resources.LoadAll<ItemData>("Item");
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.id))
                    logItems[item.id] = item;
            }
        }
    }
}
