using Companions;
using Inventory;
using Quests;
using Skills;
using UnityEngine;

namespace UI.Utilities
{
    /// <summary>
    /// Provides helper methods that enforce mutual exclusivity across the primary interface tab
    /// windows (inventory, equipment, quests, magic, combat styles, and skills). This keeps the
    /// behaviour consistent regardless of whether a window is opened via the <see cref="UIManager"/>
    /// or directly toggled through bespoke flows.
    /// </summary>
    public static class InterfaceTabMutexUtility
    {
        /// <summary>
        /// Closes every tab window except the one supplied. The method is resilient to missing
        /// singletons or prefabs so it can be invoked from any opening path without causing errors.
        /// </summary>
        /// <param name="keepOpen">Optional window that should remain open.</param>
        public static void CloseAllTabWindowsExcept(UI.IUIWindow keepOpen)
        {
            CloseWindowIfNecessary(ResolveWindow(QuestUI.Instance), keepOpen);
            CloseWindowIfNecessary(UnityEngine.Object.FindObjectOfType<Inventory.Inventory>(true), keepOpen);
            CloseWindowIfNecessary(UnityEngine.Object.FindObjectOfType<Equipment>(true), keepOpen);
            CloseWindowIfNecessary(ResolveWindow(UI.MagicUI.Instance), keepOpen);
            CloseWindowIfNecessary(ResolveWindow(UI.AttackStyleUI.Instance), keepOpen);
            CloseWindowIfNecessary(ResolveWindow(SkillsUI.Instance), keepOpen);
        }

        /// <summary>
        /// Attempts to resolve a window by using the provided candidate. When the candidate is null
        /// or has been destroyed the scene is searched for another instance.
        /// </summary>
        private static T ResolveWindow<T>(T candidate)
            where T : Component, UI.IUIWindow
        {
            if (candidate != null)
                return candidate;

            return UnityEngine.Object.FindObjectOfType<T>(true);
        }

        /// <summary>
        /// Closes the supplied window when it is currently open and does not match the window we are
        /// trying to keep visible.
        /// </summary>
        private static void CloseWindowIfNecessary<T>(T window, UI.IUIWindow keepOpen)
            where T : Component, UI.IUIWindow
        {
            if (window == null || ReferenceEquals(window, keepOpen))
                return;

            if (keepOpen is CompanionEquipment && window is Inventory.Inventory sharedInventory && sharedInventory.useSharedUIRoot)
                return;

            if (window is Inventory.Inventory inventoryWindow && !inventoryWindow.useSharedUIRoot)
            {
                // Dedicated inventories (pet storage, contextual bags, etc.) do not participate in the
                // shared tab root and should never be forced closed by the mutex logic. However, the
                // FindObjectOfType lookup above can return one of these dedicated windows which means
                // we still need to close the shared inventory window if it is currently visible.
                Inventory.Inventory sharedInventoryWindow = FindSharedInventoryWindow(inventoryWindow, keepOpen);
                if (sharedInventoryWindow == null)
                    return;

                if (!sharedInventoryWindow.IsOpen)
                    return;

                sharedInventoryWindow.Close();
                return;
            }

            if (!window.IsOpen)
                return;

            window.Close();
        }

        /// <summary>
        /// Attempts to locate the shared inventory window when the provided inventory is a dedicated
        /// instance (pet storage, contextual inventory, etc.).
        /// </summary>
        /// <param name="originalInventory">The inventory instance originally resolved by the mutex.</param>
        /// <param name="keepOpen">Optional window that should remain open.</param>
        /// <returns>The shared inventory window if one exists and should be closed.</returns>
        private static Inventory.Inventory FindSharedInventoryWindow(Inventory.Inventory originalInventory, UI.IUIWindow keepOpen)
        {
            Inventory.Inventory[] allInventories = UnityEngine.Object.FindObjectsOfType<Inventory.Inventory>(true);
            for (int i = 0; i < allInventories.Length; i++)
            {
                Inventory.Inventory candidate = allInventories[i];
                if (!candidate.useSharedUIRoot)
                    continue;

                if (ReferenceEquals(candidate, keepOpen) || ReferenceEquals(candidate, originalInventory))
                    continue;

                return candidate;
            }

            return null;
        }
    }
}
