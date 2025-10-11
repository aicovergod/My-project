using System.Collections.Generic;
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

        public void OpenWindow(IUIWindow window)
        {
            if (window == null)
                return;

            bool shopActive = ShopUI.IsShopModalActive;
            bool isShopWindow = window is ShopUI;
            bool isInventoryWindow = window is Inventory.Inventory;

            // When the shop modal is active, only the shop and inventory windows may request focus.
            if (shopActive && !isShopWindow && !isInventoryWindow)
                return;

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

                // Allow the inventory and shop windows to coexist while trading.
                if (shopActive && ((isShopWindow && w is Inventory.Inventory) || (isInventoryWindow && w is ShopUI)))
                    continue;

                w.Close();
            }
        }

        private static UIManager CreateSingleton()
        {
            var go = new GameObject(nameof(UIManager));
            return go.AddComponent<UIManager>();
        }
    }
}
