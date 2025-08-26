using System.Collections.Generic;
using UnityEngine;

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
        public static UIManager Instance { get; private set; }

        private readonly List<IUIWindow> windows = new List<IUIWindow>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            var go = new GameObject("UIManager");
            DontDestroyOnLoad(go);
            go.AddComponent<UIManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void RegisterWindow(IUIWindow window)
        {
            if (!windows.Contains(window))
                windows.Add(window);
        }

        public void OpenWindow(IUIWindow window)
        {
            foreach (var w in windows)
            {
                if (w != window && w.IsOpen)
                    w.Close();
            }
        }
    }
}
