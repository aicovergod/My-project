#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Inventory;
using Skills.Thieving.Data;
using UnityEditor;
using UnityEngine;

namespace Skills.Thieving.Editor
{
    /// <summary>
    ///     Provides an editor window that allows designers to rapidly simulate pickpocketing attempts
    ///     against a <see cref="ThievingNpcDefinition"/>. Mirrors the runtime loot and success logic so
    ///     balance tweaks can be validated without entering play mode.
    /// </summary>
    public sealed class ThievingNpcDefinitionSimulatorWindow : EditorWindow
    {
        private const string WindowTitle = "Thieving NPC Simulator";
        private const string CoinsItemId = "Gold Coins";
        private const int DefaultAttemptCount = 1000;

        [SerializeField]
        [Tooltip("Definition being simulated.")]
        private ThievingNpcDefinition definition;

        [SerializeField]
        [Tooltip("Thieving level used when evaluating success thresholds.")]
        private int thievingLevel;

        [SerializeField]
        [Tooltip("Number of pickpocket attempts to run in the simulation.")]
        private int attempts = DefaultAttemptCount;

        [SerializeField]
        [Tooltip("When enabled the loot table rolls are evaluated and aggregated in the results.")]
        private bool simulateLootRolls = true;

        [SerializeField]
        [Tooltip("When enabled the Rogue outfit bonus rolls are included in the simulation.")]
        private bool simulateRogueOutfit;

        [SerializeField]
        [Tooltip("When enabled Rocky pet rolls are simulated using the configured denominator.")]
        private bool simulatePetRolls = true;

        [SerializeField]
        [Tooltip("Copies the formatted summary to the clipboard after each run.")]
        private bool copySummaryToClipboard;

        private readonly Dictionary<string, ItemAggregation> aggregatedLoot = new Dictionary<string, ItemAggregation>(StringComparer.Ordinal);
        private Vector2 summaryScrollPosition;
        private string latestSummary = string.Empty;

        /// <summary>
        ///     Opens the simulator window directly from a ThievingNpcDefinition inspector context menu.
        /// </summary>
        /// <param name="command">Menu command containing the active definition.</param>
        [MenuItem("CONTEXT/ThievingNpcDefinition/Simulate Pickpockets...")]
        private static void OpenFromContext(MenuCommand command)
        {
            var window = GetWindow<ThievingNpcDefinitionSimulatorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.Show();
            window.Focus();

            if (command.context is ThievingNpcDefinition contextDefinition)
            {
                window.AssignDefinition(contextDefinition);
            }
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(380f, 240f);
        }

        /// <summary>
        ///     Assigns the supplied definition to the simulator and resets dependent state.
        /// </summary>
        /// <param name="newDefinition">Definition to simulate.</param>
        private void AssignDefinition(ThievingNpcDefinition newDefinition)
        {
            definition = newDefinition;
            if (definition != null)
            {
                thievingLevel = Mathf.Max(1, definition.RequiredLevel);
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.Space(4f);

                EditorGUI.BeginChangeCheck();
                var pickedDefinition = (ThievingNpcDefinition)EditorGUILayout.ObjectField(
                    new GUIContent("Definition", "Pickpocket target definition to simulate."),
                    definition,
                    typeof(ThievingNpcDefinition),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    AssignDefinition(pickedDefinition);
                }

                if (definition == null)
                {
                    EditorGUILayout.HelpBox("Assign a Thieving NPC definition to begin simulating pickpocket attempts.", MessageType.Info);
                    return;
                }

                thievingLevel = Mathf.Clamp(
                    EditorGUILayout.IntField(
                        new GUIContent("Thieving Level", "Level used when calculating success thresholds."),
                        Mathf.Max(1, thievingLevel)),
                    1,
                    255);

                attempts = Mathf.Clamp(
                    EditorGUILayout.IntField(
                        new GUIContent("Attempt Count", "Number of simulated pickpocket attempts."),
                        Mathf.Max(1, attempts)),
                    1,
                    10_000_000);

                simulateLootRolls = EditorGUILayout.Toggle(
                    new GUIContent("Simulate Loot Rolls", "When enabled the loot table rolls are evaluated."),
                    simulateLootRolls);

                using (new EditorGUI.DisabledScope(!simulateLootRolls))
                {
                    simulateRogueOutfit = EditorGUILayout.Toggle(
                        new GUIContent("Simulate Rogue Outfit", "When enabled the Rogue outfit bonus rolls are included."),
                        simulateRogueOutfit);
                }

                simulatePetRolls = EditorGUILayout.Toggle(
                    new GUIContent("Simulate Pet Rolls", "When enabled Rocky pet rolls are evaluated using the definition denominator."),
                    simulatePetRolls);

                copySummaryToClipboard = EditorGUILayout.Toggle(
                    new GUIContent("Copy Summary To Clipboard", "When enabled the simulation summary is written to the clipboard for quick sharing."),
                    copySummaryToClipboard);

                EditorGUILayout.Space(8f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Run Simulation", GUILayout.Width(180f), GUILayout.Height(32f)))
                    {
                        RunSimulation();
                    }
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(12f);
                DrawSummaryArea();
            }
        }

        /// <summary>
        ///     Executes the configured number of pickpocket attempts and logs an aggregate summary to the console.
        /// </summary>
        private void RunSimulation()
        {
            if (definition == null)
                return;

            int attemptCount = Mathf.Max(1, attempts);
            int level = Mathf.Clamp(thievingLevel > 0 ? thievingLevel : definition.RequiredLevel, 1, 255);

            aggregatedLoot.Clear();
            latestSummary = string.Empty;
            int successCount = 0;
            int failureCount = 0;
            float totalXp = 0f;
            int petProcCount = 0;

            // Environment is also used as a project namespace so explicitly reference the BCL type
            // to ensure we seed the RNG correctly even when the namespace is in scope.
            int seed = unchecked(System.Environment.TickCount ^ GetHashCode() ^ DateTime.Now.Millisecond);
            var rng = new System.Random(seed);

            for (int i = 0; i < attemptCount; i++)
            {
                int threshold = definition.GetSuccessThreshold(level);
                int roll = Mathf.Clamp(rng.Next(0, 256), 0, 255);
                bool success = roll <= threshold;

                if (!success)
                {
                    failureCount++;
                    continue;
                }

                successCount++;
                totalXp += definition.BaseXp;

                if (simulateLootRolls)
                {
                    var rewards = ResolveLootRolls(definition.CoinRange, definition.LootTable, definition.BaseLootRolls, rng);
                    if (simulateRogueOutfit && definition.RogueOutfitBonusRolls > 0)
                    {
                        var bonusRewards = ResolveLootRolls(Vector2Int.zero, definition.LootTable, definition.RogueOutfitBonusRolls, rng);
                        rewards.AddRange(bonusRewards);
                    }

                    foreach (var reward in rewards)
                    {
                        AccumulateLoot(reward.item, reward.quantity);
                    }
                }

                if (simulatePetRolls && definition.PetRollDenominator > 0)
                {
                    if (rng.Next(definition.PetRollDenominator) == 0)
                    {
                        petProcCount++;
                    }
                }
            }

            if (failureCount + successCount < attemptCount)
            {
                failureCount = attemptCount - successCount;
            }

            var builder = new StringBuilder();
            string npcName = definition.DisplayName;
            builder.AppendLine($"Thieving simulation for {npcName}");
            builder.AppendLine($"Attempts: {attemptCount:N0} | Level: {level}");
            builder.AppendLine($"Successes: {successCount:N0} ({(successCount / (float)attemptCount):P2})");
            builder.AppendLine($"Failures: {failureCount:N0} ({(failureCount / (float)attemptCount):P2})");
            builder.AppendLine($"Total XP: {totalXp:N2}");
            if (simulatePetRolls && definition.PetRollDenominator > 0)
            {
                builder.AppendLine($"Pet Procs: {petProcCount:N0}");
            }

            if (!simulateLootRolls)
            {
                builder.AppendLine("Loot simulation disabled. Enable \"Simulate Loot Rolls\" to view loot breakdowns.");
            }
            else if (aggregatedLoot.Count == 0)
            {
                builder.AppendLine("No loot generated during this run.");
            }
            else
            {
                builder.AppendLine("Loot Totals:");
                foreach (var entry in aggregatedLoot.Values.OrderByDescending(e => e.Quantity).ThenBy(e => e.DisplayName, StringComparer.Ordinal))
                {
                    builder.AppendLine($"  - {entry.DisplayName} x{entry.Quantity:N0}");
                }
            }

            string summary = builder.ToString();
            Debug.Log(summary);
            latestSummary = summary;

            if (copySummaryToClipboard)
            {
                EditorGUIUtility.systemCopyBuffer = summary;
            }
        }

        /// <summary>
        ///     Renders the scrollable text area showing the most recent simulation results.
        /// </summary>
        private void DrawSummaryArea()
        {
            EditorGUILayout.LabelField("Simulation Summary", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(latestSummary))
            {
                EditorGUILayout.HelpBox("Run the simulation to view success, XP, and loot breakdowns.", MessageType.Info);
                return;
            }

            summaryScrollPosition = EditorGUILayout.BeginScrollView(summaryScrollPosition, GUILayout.MinHeight(160f));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextArea(latestSummary, GUILayout.ExpandHeight(true));
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        ///     Aggregates the supplied item so the final summary can present per-item totals.
        /// </summary>
        /// <param name="item">Item awarded by the simulated loot roll.</param>
        /// <param name="quantity">Quantity awarded.</param>
        private void AccumulateLoot(ItemData item, int quantity)
        {
            if (item == null || quantity <= 0)
                return;

            string key = !string.IsNullOrEmpty(item.id) ? item.id : item.name;
            if (!aggregatedLoot.TryGetValue(key, out var existing))
            {
                existing = new ItemAggregation(item);
            }

            existing.Quantity += quantity;
            aggregatedLoot[key] = existing;
        }

        /// <summary>
        ///     Resolves the loot drops using the same logic implemented by <see cref="Skills.Thieving.Core.ThievingSkill"/>.
        /// </summary>
        /// <param name="coinRange">Coin range granted per attempt.</param>
        /// <param name="lootTable">Additional loot entries to evaluate.</param>
        /// <param name="rolls">Number of weighted rolls performed.</param>
        /// <param name="rng">Random number generator used for deterministic editor runs.</param>
        /// <returns>List of generated rewards.</returns>
        private List<(ItemData item, int quantity)> ResolveLootRolls(
            Vector2Int coinRange,
            IReadOnlyList<ThievingLootTableEntry> lootTable,
            int rolls,
            System.Random rng)
        {
            var rewards = new List<(ItemData item, int quantity)>();

            var coinItem = ItemDatabase.GetItem(CoinsItemId);
            if (coinItem != null && (coinRange.x > 0 || coinRange.y > 0))
            {
                int min = Mathf.Max(0, coinRange.x);
                int max = Mathf.Max(min, coinRange.y);
                int amount = RandomRangeInclusive(rng, min, max);
                if (amount > 0)
                {
                    rewards.Add((coinItem, amount));
                }
            }

            if (lootTable == null || lootTable.Count == 0 || rolls <= 0)
                return rewards;

            var chanceEntries = new List<(ThievingLootTableEntry entry, ItemData item, float clampedChance)>();

            for (int i = 0; i < lootTable.Count; i++)
            {
                var entry = lootTable[i];
                if (entry.guaranteed)
                {
                    var guaranteedItem = ItemDatabase.GetItem(entry.itemId);
                    if (guaranteedItem == null)
                        continue;

                    int quantity = ResolveQuantity(entry.quantityRange, rng);
                    if (quantity <= 0)
                        continue;

                    rewards.Add((guaranteedItem, quantity));
                    continue;
                }

                float clampedChance = Mathf.Clamp(entry.dropChancePercent, 0f, 100f);
                if (clampedChance <= 0f)
                    continue;

                var chanceItem = ItemDatabase.GetItem(entry.itemId);
                if (chanceItem == null)
                    continue;

                chanceEntries.Add((entry, chanceItem, clampedChance));
            }

            if (chanceEntries.Count == 0)
                return rewards;

            for (int rollIndex = 0; rollIndex < rolls; rollIndex++)
            {
                float roll = (float)(rng.NextDouble() * 100.0);
                float cumulative = 0f;

                for (int entryIndex = 0; entryIndex < chanceEntries.Count; entryIndex++)
                {
                    var candidate = chanceEntries[entryIndex];
                    cumulative += candidate.clampedChance;

                    if (roll > cumulative)
                        continue;

                    int quantity = ResolveQuantity(candidate.entry.quantityRange, rng);
                    if (quantity > 0)
                    {
                        rewards.Add((candidate.item, quantity));
                    }

                    break;
                }


            }

            return rewards;
        }

        /// <summary>
        ///     Resolves a quantity using inclusive minimum/maximum values mirroring the runtime logic.
        /// </summary>
        /// <param name="range">Range stored on the loot table entry.</param>
        /// <param name="rng">Random number generator shared with the simulation.</param>
        /// <returns>Quantity rolled for the entry.</returns>
        private static int ResolveQuantity(Vector2Int range, System.Random rng)
        {
            int min = range.x <= 0 ? 1 : range.x;
            int max = range.y <= 0 ? min : range.y;
            if (max < min)
                max = min;
            return RandomRangeInclusive(rng, min, max);
        }

        /// <summary>
        ///     Helper that mirrors Unity's inclusive integer <see cref="UnityEngine.Random.Range(int, int)"/> behaviour.
        /// </summary>
        /// <param name="rng">Random number generator shared with the simulation.</param>
        /// <param name="minInclusive">Minimum inclusive value.</param>
        /// <param name="maxInclusive">Maximum inclusive value.</param>
        /// <returns>Random integer within the provided bounds.</returns>
        private static int RandomRangeInclusive(System.Random rng, int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
                maxInclusive = minInclusive;
            return rng.Next(minInclusive, maxInclusive + 1);
        }

        /// <summary>
        ///     Stores aggregate loot information for the simulation summary.
        /// </summary>
        private struct ItemAggregation
        {
            public ItemAggregation(ItemData item)
            {
                Item = item;
                Quantity = 0;
            }

            public ItemData Item { get; }

            public int Quantity { get; set; }

            public string DisplayName
            {
                get
                {
                    if (Item == null)
                        return "Unknown";
                    return !string.IsNullOrEmpty(Item.itemName) ? Item.itemName : Item.name;
                }
            }
        }
    }
}
#endif
