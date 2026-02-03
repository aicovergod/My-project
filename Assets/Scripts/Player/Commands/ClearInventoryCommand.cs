using System;
using System.Collections.Generic;
using Core.Save;
using Inventory.Core;
using Player.Ranks;
using UnityEngine;

namespace Player.Commands
{
    /// <summary>
    /// Clears the issuing player's inventory or, when provided with a username, clears a targeted
    /// player's inventory via their saved profile data. The command requires admin-level permission
    /// to avoid accidental data loss.
    /// </summary>
    public sealed class ClearInventoryCommand : IPlayerCommand
    {
        private const string PlayerInventorySaveKey = "InventoryData";
        private const int DefaultOfflineSlotCount = 28;

        /// <inheritdoc />
        public string Name => "clearinv";

        /// <inheritdoc />
        public string Description => "Clears the issuing or targeted player's inventory.";

        /// <inheritdoc />
        public PlayerRank RequiredRank => PlayerRank.Admin;

        /// <inheritdoc />
        public PlayerCommandResult Execute(PlayerCommandContext context)
        {
            var args = context.Arguments;
            if (args.Count == 0)
                return ClearActivePlayerInventory();

            if (args.Count > 1)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Usage: ::clearinv [\"player name\"]");
            }

            string target = args[0];
            if (string.IsNullOrWhiteSpace(target))
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.InvalidSyntax,
                    "Usage: ::clearinv [\"player name\"]");
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
                    return ClearActivePlayerInventory();
            }

            return ClearOfflineInventory(target, targetSlug);
        }

        private static PlayerCommandResult ClearActivePlayerInventory()
        {
            if (!PlayerLocator.TryFindPlayer(out var playerObject) || playerObject == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "Unable to locate the active player object.");
            }

            var inventory = playerObject.GetComponent<Inventory.Inventory>();
            if (inventory == null)
            {
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    "The active player does not have an inventory component.");
            }

            var model = inventory.Model;
            int slotCount = model != null ? model.Size : 0;
            bool clearedAny = inventory.ClearAllSlots();
            if (slotCount <= 0)
                slotCount = inventory.Model != null ? inventory.Model.Size : 0;

            string message = clearedAny
                ? (slotCount > 0 ? $"Cleared {slotCount}-slot inventory." : "Inventory cleared.")
                : "Inventory is already empty.";

            return PlayerCommandResult.Success(message);
        }

        private static PlayerCommandResult ClearOfflineInventory(string rawTarget, string targetSlug)
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

            if (!TryClearInventoryData(save, targetSlug, out int slotCount, out bool changed, out string error))
            {
                string failure = string.IsNullOrEmpty(error)
                    ? $"Failed to clear the inventory for {displayName}."
                    : error;
                return PlayerCommandResult.Failure(PlayerCommandFailureReason.ExecutionError, failure);
            }

            if (!changed)
            {
                string alreadyMessage = $"Inventory for {displayName} is already empty.";
                return PlayerCommandResult.Success(alreadyMessage);
            }

            try
            {
                AccountManager.SaveAsync(save).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.LogError($"ClearInventoryCommand: Failed to persist cleared inventory for {displayName}.\n{ex}");
                return PlayerCommandResult.Failure(
                    PlayerCommandFailureReason.ExecutionError,
                    $"Failed to save the cleared inventory for {displayName}. Check the logs for details.");
            }

            string successMessage = slotCount > 0
                ? $"Cleared {slotCount}-slot inventory for {displayName}."
                : $"Cleared the inventory for {displayName}.";
            return PlayerCommandResult.Success(successMessage);
        }

        private static bool TryClearInventoryData(
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
                ? PlayerInventorySaveKey
                : string.Concat(slug, ":", PlayerInventorySaveKey);

            var entries = save.data.entries;
            var entry = FindEntry(entries, prefixedKey);

            if (entry == null && !string.IsNullOrEmpty(slug))
            {
                entry = FindEntry(entries, PlayerInventorySaveKey);
                if (entry != null)
                    entry.key = prefixedKey;
            }

            InventoryModel.InventorySaveData existing = null;
            if (entry != null && !string.IsNullOrEmpty(entry.value))
            {
                try
                {
                    existing = JsonUtility.FromJson<InventoryModel.InventorySaveData>(entry.value);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"ClearInventoryCommand: Failed to parse inventory data for '{slug}'. Resetting inventory to empty.\n{ex}");
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

            var clearedData = new InventoryModel.InventorySaveData
            {
                slots = new InventoryModel.SlotData[slotCount]
            };

            // JsonUtility serialises null array elements as "null", which then restores as literal
            // null slot references. InventoryModel expects each slot element to be a fully-instantiated
            // object (matching CaptureState output) so downstream logic can safely dereference it
            // without additional null guards. Populate each slot with an empty instance so offline
            // wipes mirror the runtime capture format and avoid NullReferenceException when loading.
            for (int i = 0; i < clearedData.slots.Length; i++)
            {
                clearedData.slots[i] = new InventoryModel.SlotData
                {
                    id = string.Empty,
                    count = 0
                };
            }

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

        private static int ResolveSlotCount(InventoryModel.InventorySaveData data)
        {
            if (data?.slots != null && data.slots.Length > 0)
                return data.slots.Length;

            return DefaultOfflineSlotCount;
        }

        private static bool HasItems(InventoryModel.InventorySaveData data)
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
    }
}
