using System;
using System.Collections.Generic;
using BankSystem;
using Core.Save;
using Player.Ranks;
using UnityEngine;

namespace Player.Commands
{
    /// <summary>
    /// Clears the issuing player's bank or, when supplied with a username argument, clears the
    /// targeted player's bank by manipulating their saved profile data. Restricted to admin rank
    /// to avoid accidental economy-wide data loss.
    /// </summary>
    public sealed class ClearBankCommand : IPlayerCommand
    {
        private const string PlayerBankSaveKey = "BankData";
        private const int DefaultOfflineSlotCount = 400;

        /// <inheritdoc />
        public string Name => "clearbank";

        /// <inheritdoc />
        public string Description => "Clears the issuing or targeted player's bank.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Admin;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            var args = context.Arguments;
            if (args.Count == 0)
                return ClearActivePlayerBank();

            if (args.Count > 1)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Usage: ::clearbank [\"player name\"]");
            }

            string target = args[0];
            if (string.IsNullOrWhiteSpace(target))
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Usage: ::clearbank [\"player name\"]");
            }

            string targetSlug = AccountManager.SanitizeUsername(target);
            if (string.IsNullOrEmpty(targetSlug))
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "The supplied username does not contain any valid characters.");
            }

            string activeUsername = SaveManager.ActiveAccountUsername;
            if (!string.IsNullOrEmpty(activeUsername))
            {
                string activeSlug = AccountManager.SanitizeUsername(activeUsername);
                if (string.Equals(activeSlug, targetSlug, StringComparison.Ordinal))
                    return ClearActivePlayerBank();
            }

            return ClearOfflineBank(target, targetSlug);
        }

        /// <summary>
        /// Clears the bank of the currently logged-in player by invoking the persistent
        /// <see cref="BankUI"/> singleton and persisting the resulting state.
        /// </summary>
        private static PlayerCommandResult ClearActivePlayerBank()
        {
            var bank = BankUI.Instance;
            if (bank == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The bank interface is not available in this scene.");
            }

            BankSaveData existing = SaveManager.Load<BankSaveData>(PlayerBankSaveKey);
            bool hadItems = HasItems(existing);
            int slotCount = ResolveSlotCount(existing);

            bank.ClearBank();

            string message = hadItems
                ? (slotCount > 0 ? $"Cleared {slotCount}-slot bank." : "Bank cleared.")
                : "Bank is already empty.";

            return PlayerCommandResult.Success(message);
        }

        /// <summary>
        /// Clears the bank data stored on disk for an offline player account.
        /// </summary>
        private static PlayerCommandResult ClearOfflineBank(string rawTarget, string targetSlug)
        {
            var status = AccountManager.TryLoadAccount(rawTarget, out var save);
            switch (status)
            {
                case AccountManager.AccountLoadStatus.NotFound:
                    return PlayerCommandResult.Failure(
                        PlayerCommandFailureReason.ExecutionError,
                        $"No account was found for '{rawTarget}'.");
                case AccountManager.AccountLoadStatus.FailedToDeserialize:
                    return PlayerCommandResult.Failure(
                        PlayerCommandFailureReason.ExecutionError,
                        $"Account data for '{rawTarget}' could not be read. Resolve the corrupted save before retrying.");
            }

            if (save == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Failed to load the requested account.");
            }

            string displayName = !string.IsNullOrEmpty(save.username) ? save.username : rawTarget;

            if (!TryClearBankData(save, targetSlug, out int slotCount, out bool changed, out string error))
            {
                string failure = string.IsNullOrEmpty(error)
                    ? $"Failed to clear the bank for {displayName}."
                    : error;
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.ExecutionError, failure);
            }

            if (!changed)
            {
                string alreadyMessage = $"Bank for {displayName} is already empty.";
                return PlayerCommandResult.Success(alreadyMessage);
            }

            try
            {
                AccountManager.SaveAsync(save).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.LogError($"ClearBankCommand: Failed to persist cleared bank for {displayName}.\n{ex}");
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Failed to save the cleared bank for {displayName}. Check the logs for details.");
            }

            string successMessage = slotCount > 0
                ? $"Cleared {slotCount}-slot bank for {displayName}."
                : $"Cleared the bank for {displayName}.";
            return PlayerCommandResult.Success(successMessage);
        }

        private static bool TryClearBankData(
            AccountSave save,
            string fallbackSlug,
            out int slotCount,
            out bool changed,
            out string error)
        {
            slotCount = DefaultOfflineSlotCount;
            changed = false;
            error = string.Empty;

            if (save == null)
            {
                error = "Account data was unavailable.";
                return false;
            }

            save.data ??= new AccountSave.AccountData
            {
                version = SaveManager.SaveDataVersion,
                entries = new List<AccountSave.AccountDataEntry>()
            };
            save.data.entries ??= new List<AccountSave.AccountDataEntry>();

            string slug = !string.IsNullOrEmpty(save.usernameSlug)
                ? AccountManager.SanitizeUsername(save.usernameSlug)
                : string.Empty;

            if (string.IsNullOrEmpty(slug))
                slug = fallbackSlug;

            string prefixedKey = string.IsNullOrEmpty(slug)
                ? PlayerBankSaveKey
                : string.Concat(slug, ":", PlayerBankSaveKey);

            var entries = save.data.entries;
            var entry = FindEntry(entries, prefixedKey);

            if (entry == null && !string.IsNullOrEmpty(slug))
            {
                entry = FindEntry(entries, PlayerBankSaveKey);
                if (entry != null)
                    entry.key = prefixedKey;
            }

            BankSaveData existing = null;
            if (entry != null && !string.IsNullOrEmpty(entry.value))
            {
                try
                {
                    existing = JsonUtility.FromJson<BankSaveData>(entry.value);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"ClearBankCommand: Failed to parse bank data for '{slug}'. Resetting bank to empty.\n{ex}");
                    existing = null;
                }
            }

            slotCount = ResolveSlotCount(existing);
            bool hadItems = HasItems(existing);

            if (entry == null)
            {
                entry = new AccountSave.AccountDataEntry { key = prefixedKey };
                entries.Add(entry);
            }

            if (!hadItems && existing != null && existing.slots != null && existing.slots.Length == slotCount)
            {
                changed = false;
                return true;
            }

            var clearedData = new BankSaveData
            {
                slots = new BankSlotData[slotCount]
            };

            entry.value = JsonUtility.ToJson(clearedData);
            changed = true;
            return true;
        }

        private static AccountSave.AccountDataEntry FindEntry(IList<AccountSave.AccountDataEntry> entries, string key)
        {
            if (entries == null || string.IsNullOrEmpty(key))
                return null;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && string.Equals(entry.key, key, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        private static int ResolveSlotCount(BankSaveData data)
        {
            if (data?.slots != null && data.slots.Length > 0)
                return data.slots.Length;

            return DefaultOfflineSlotCount;
        }

        private static bool HasItems(BankSaveData data)
        {
            if (data?.slots == null)
                return false;

            for (int i = 0; i < data.slots.Length; i++)
            {
                var slot = data.slots[i];
                if (slot == null)
                    continue;

                if (!string.IsNullOrEmpty(slot.id) && slot.count > 0)
                    return true;
            }

            return false;
        }

        [Serializable]
        private sealed class BankSaveData
        {
            public BankSlotData[] slots;
        }

        [Serializable]
        private sealed class BankSlotData
        {
            public string id;
            public int count;
        }
    }
}
