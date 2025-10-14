using System.Collections.Generic;
using BankSystem;
using ShopSystem;
using UnityEngine;
using World;

namespace UI
{
    public interface IUIWindow
    {
        bool IsOpen { get; }
        void Close();
    }

    /// <summary>
    /// Central manager for UI windows. Opening one window closes any others.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance => PersistentSceneSingleton<UIManager>.Instance;

        private readonly List<IUIWindow> windows = new List<IUIWindow>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            PersistentSceneSingleton<UIManager>.Bootstrap(CreateSingleton);
        }

        private void Awake()
        {
            if (!PersistentSceneSingleton<UIManager>.HandleAwake(this))
                return;
        }

        private void OnDestroy()
        {
            PersistentSceneSingleton<UIManager>.HandleOnDestroy(this);
        }

        public void RegisterWindow(IUIWindow window)
        {
            if (window == null)
                return;

            if (!windows.Contains(window))
                windows.Add(window);
        }

        public void UnregisterWindow(IUIWindow window)
        {
            if (window == null)
                return;

            windows.Remove(window);
        }

        /// <summary>
        /// Attempts to bring the requested window to the foreground while respecting modal locks.
        /// Returns <c>false</c> if a modal interface is active and the request does not target the
        /// modal window or the inventory, allowing callers to gracefully bail out of their open flow.
        /// </summary>
        public bool TryOpenWindow(IUIWindow window)
        {
            if (window == null)
                return false;

            bool shopActive = ShopUI.IsShopModalActive;
            bool bankActive = BankUI.IsBankModalActive;
            bool isShopWindow = window is ShopUI;
            bool isBankWindow = window is BankUI;
            bool isInventoryWindow = window is Inventory.Inventory;
            Inventory.Inventory requestedInventory = window as Inventory.Inventory;

            // When a modal interface (shop or bank) is active, only that window and the inventory may request focus.
            if ((shopActive && !isShopWindow && !isInventoryWindow) || (bankActive && !isBankWindow && !isInventoryWindow))
                return false;

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var w = windows[i];
                if (w == null)
                {
                    windows.RemoveAt(i);
                    continue;
                }

                if (w == window || !w.IsOpen)
                    continue;

                // Allow the inventory to coexist with modal interfaces (shop/bank) while trading/banking.
                bool allowShopPair = shopActive && ((isShopWindow && w is Inventory.Inventory) || (isInventoryWindow && w is ShopUI));
                bool allowBankPair = bankActive && ((isBankWindow && w is Inventory.Inventory) || (isInventoryWindow && w is BankUI));
                if (allowShopPair || allowBankPair)
                    continue;

                if (requestedInventory != null && w is Inventory.Inventory existingInventory && !existingInventory.useSharedUIRoot)
                {
                    // Dedicated inventories (pet storage, contextual bags, etc.) do not participate in the
                    // shared UI root, meaning they should remain visible when the shared inventory is opened.
                    // Skipping Close() here allows those bespoke inventories to coexist with the player bag.
                    continue;
                }

                w.Close();
            }

            return true;
        }

        private static UIManager CreateSingleton()
        {
            var go = new GameObject(nameof(UIManager));
            return go.AddComponent<UIManager>();
        }
    }
}
