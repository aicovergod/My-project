using System;
using System.Collections.Generic;
using BankSystem;
using Inventory;
using Pets;
using UnityEngine;

namespace Skills.Outfits
{
    /// <summary>
    /// Resolves outfit roll attempts for gathering skills so the logic is
    /// consistent across woodcutting, mining, fishing, and cooking.
    /// </summary>
    public static class SkillingOutfitRewarder
    {
        private const int DefaultWinningRoll = 0;
        private const int DefaultRollRange = 2500;

        /// <summary>
        /// Attempts to award a missing outfit piece using a shared roll routine.
        /// </summary>
        /// <param name="progress">The outfit progress tracker for the skill.</param>
        /// <param name="inventory">The player's inventory reference.</param>
        /// <param name="bank">Bank UI hook used when the inventory is full.</param>
        /// <param name="rng">RNG callback matching Unity's <see cref="UnityEngine.Random.Range(int,int)"/> signature.</param>
        /// <param name="debugSkillLabel">Label used when logging debug roll information.</param>
        /// <param name="successToast">Toast text displayed when the item enters the inventory.</param>
        /// <param name="bankToast">Toast text displayed when the reward is routed to the bank.</param>
        /// <param name="rollRange">Exclusive upper bound for the roll check. Defaults to 2500.</param>
        /// <param name="winningRoll">The integer result that indicates success. Defaults to 0.</param>
        /// <returns>True if a piece was awarded, otherwise false.</returns>
        public static bool TryAwardPiece(
            SkillingOutfitProgress progress,
            Inventory.Inventory inventory,
            BankUI bank,
            Func<int, int, int> rng,
            string debugSkillLabel,
            string successToast,
            string bankToast,
            int rollRange = DefaultRollRange,
            int winningRoll = DefaultWinningRoll)
        {
            if (progress == null)
            {
                Debug.LogWarning("SkillingOutfitRewarder.TryAwardPiece invoked without progress tracker");
                return false;
            }

            if (progress.owned == null)
                progress.owned = new HashSet<string>();

            if (progress.allPieceIds == null || progress.allPieceIds.Length == 0)
            {
                Debug.LogWarning($"[{debugSkillLabel}] Outfit roll skipped because no piece ids were supplied");
                return false;
            }

            var rollFunc = rng ?? UnityEngine.Random.Range;
            int roll = rollFunc(0, Mathf.Max(1, rollRange));

            if (SkillingOutfitProgress.DebugChance)
                Debug.Log($"[{debugSkillLabel}] Skilling outfit roll: {roll} (chance 1 in {Mathf.Max(1, rollRange)})");

            if (roll != winningRoll)
                return false;

            var missingPieces = GetMissingPieces(progress);
            if (missingPieces.Count == 0)
            {
                Debug.LogWarning($"[{debugSkillLabel}] Outfit roll succeeded but no pieces are missing");
                return false;
            }

            string chosenId = missingPieces[rollFunc(0, missingPieces.Count)];
            var item = ItemDatabase.GetItem(chosenId);
            if (item == null)
            {
                Debug.LogError($"[{debugSkillLabel}] Outfit reward item '{chosenId}' is not present in the ItemDatabase");
                return false;
            }

            bool addedToInventory = inventory != null && inventory.AddItem(item);
            if (!addedToInventory)
            {
                bank?.AddItemToBank(item);
                PetToastUI.Show(bankToast);
            }
            else
            {
                PetToastUI.Show(successToast);
            }

            bool newlyAdded = progress.owned.Add(chosenId);
            if (!newlyAdded)
                Debug.LogWarning($"[{debugSkillLabel}] Outfit reward duplicate detected for '{chosenId}'");

            // Sanity check to ensure owned count never exceeds the number of possible pieces.
            if (progress.owned.Count > progress.allPieceIds.Length)
            {
                Debug.LogWarning(
                    $"[{debugSkillLabel}] Outfit owned count ({progress.owned.Count}) exceeds available piece count ({progress.allPieceIds.Length})");
            }

            return true;
        }

        private static List<string> GetMissingPieces(SkillingOutfitProgress progress)
        {
            var missing = new List<string>();
            foreach (var id in progress.allPieceIds)
            {
                if (!progress.owned.Contains(id))
                    missing.Add(id);
            }

            return missing;
        }
    }
}
