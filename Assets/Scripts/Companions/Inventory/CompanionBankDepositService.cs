using BankSystem;
using UI.Chat;
using UnityEngine;

namespace Companions.Inventory
{
    /// <summary>
    /// Provides helper methods for routing the companion's backpack into the bank.
    /// Centralises range checks, chat feedback, and debug logging so UI surfaces can
    /// trigger deposits without depending on <see cref="CompanionManager"/> internals.
    /// </summary>
    public static class CompanionBankDepositService
    {
        /// <summary>Fallback label used when no explicit companion name has been provided.</summary>
        private const string DefaultCompanionDisplayName = "Companion";

        /// <summary>
        /// Attempts to deposit every item currently stored in the companion inventory into the player's bank.
        /// </summary>
        /// <param name="controller">Active companion controller containing the inventory wrapper.</param>
        /// <param name="enableDebugLogging">True when verbose companion logging should be emitted.</param>
        /// <param name="companionDisplayName">Display name used for companion chat output.</param>
        /// <returns>True when at least one item was deposited successfully; otherwise false.</returns>
        public static bool TryDepositCompanionInventoryToBank(
            Companions.CompanionController controller,
            bool enableDebugLogging,
            string companionDisplayName)
        {
            companionDisplayName = ResolveDisplayName(companionDisplayName);

            if (controller == null || controller.gameObject == null || !controller.gameObject.activeSelf)
            {
                if (enableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because no companion is active.");
                return false;
            }

            var inventoryWrapper = controller.Inventory;
            if (inventoryWrapper == null)
            {
                if (enableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the inventory wrapper is unavailable.");
                return false;
            }

            var inventoryComponent = inventoryWrapper.InventoryComponent;
            if (inventoryComponent == null)
            {
                if (enableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the inventory component is missing.");
                return false;
            }

            var bank = BankUI.Instance;
            if (bank == null)
            {
                if (enableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the bank UI could not be resolved.");
                return false;
            }

            var playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject == null)
            {
                if (enableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the player object could not be located.");
                return false;
            }

            if (!CompanionBankDepositAnchor.IsPlayerWithinDepositRange(playerObject.transform.position))
            {
                if (enableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because no bank anchors are within range.");

                PublishRandomBankOutOfRangeMessage(companionDisplayName);
                return false;
            }

            if (IsCompanionInventoryEmpty(inventoryComponent))
            {
                if (enableDebugLogging)
                    Debug.Log("[Companion] Deposit aborted because the inventory is empty.");

                PublishRandomEmptyBankInventoryMessage(companionDisplayName);
                return false;
            }

            int moved = bank.DepositAllFromInventory(inventoryComponent);
            if (enableDebugLogging)
            {
                if (moved > 0)
                    Debug.Log($"[Companion] Deposited {moved} item(s) from the companion inventory into the bank.");
                else
                    Debug.Log("[Companion] Deposit attempt completed but no items were moved (inventory empty or bank full).");
            }

            if (moved > 0)
            {
                PublishRandomBankDepositMessage(companionDisplayName);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the supplied inventory currently has any occupied slots.
        /// </summary>
        /// <param name="inventory">Companion inventory component to inspect.</param>
        /// <returns><c>true</c> when the inventory exists but all slots are empty; otherwise <c>false</c>.</returns>
        private static bool IsCompanionInventoryEmpty(global::Inventory.Inventory inventory)
        {
            if (inventory == null)
                return true;

            var model = inventory.Model;
            if (model == null)
                return true;

            int slotCount = model.Size;
            for (int i = 0; i < slotCount; i++)
            {
                var entry = model.GetEntry(i);
                if (entry.item != null && entry.count > 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Emits a random companion chat line to confirm the deposit when items reach the bank.
        /// </summary>
        private static void PublishRandomBankDepositMessage(string companionDisplayName)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = Companions.CompanionChatLibrary.GetRandomBankDepositLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(companionDisplayName, message);
        }

        /// <summary>
        /// Emits a random flavour line when the companion is asked to bank items but has nothing to deposit.
        /// </summary>
        private static void PublishRandomEmptyBankInventoryMessage(string companionDisplayName)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = Companions.CompanionChatLibrary.GetRandomEmptyBankInventoryLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(companionDisplayName, message);
        }

        /// <summary>
        /// Emits a random reminder when the companion cannot deposit items because no banks are nearby.
        /// Keeps flavour consistent with the right-click and pet level bar bank interactions.
        /// </summary>
        private static void PublishRandomBankOutOfRangeMessage(string companionDisplayName)
        {
            var chat = ChatService.Instance;
            if (chat == null)
                return;

            string message = Companions.CompanionChatLibrary.GetRandomBankOutOfRangeLine();
            if (string.IsNullOrWhiteSpace(message))
                return;

            chat.PublishCompanionMessage(companionDisplayName, message);
        }

        /// <summary>
        /// Normalises the supplied display name so chat messages always include a usable label.
        /// </summary>
        private static string ResolveDisplayName(string companionDisplayName)
        {
            if (string.IsNullOrWhiteSpace(companionDisplayName))
                return DefaultCompanionDisplayName;

            return companionDisplayName;
        }
    }
}
