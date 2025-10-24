using UnityEngine;
using World;

namespace Companions.UI
{
    /// <summary>
    /// Lightweight debug menu that surfaces companion-specific tooling behind the F4 hotkey. The menu
    /// currently exposes a shortcut for opening the live cooldown inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AdminF4Menu : SceneGatedSingletonBehaviour<AdminF4Menu>
    {
        private const int WindowId = 0xF40F4;

        private Rect windowRect = new Rect(10f, 10f, 280f, 140f);
        private bool visible;

        /// <summary>Indicates whether the F4 admin menu is currently visible.</summary>
        public static bool IsVisible => Instance != null && Instance.visible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BootstrapSingleton(CreateInstance);
        }

        private static AdminF4Menu CreateInstance()
        {
            var go = new GameObject(nameof(AdminF4Menu));
            return go.AddComponent<AdminF4Menu>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F4))
                visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            windowRect = GUI.ModalWindow(WindowId, windowRect, DrawWindowContents, "Companion Debug (F4)");
        }

        private void DrawWindowContents(int windowId)
        {
            GUILayout.Label("Companion debugging shortcuts");

            if (CompanionManager.CompanionSkillCooldowns == null)
                GUILayout.Label("No active cooldown tracker detected.");

            if (GUILayout.Button("Show Companion Cooldowns"))
            {
                var window = CompanionCooldownsWindow.Instance;
                if (window != null)
                    window.Open();

                visible = false;
            }

            GUILayout.Space(6f);
            GUILayout.Label("Press F4 again to close this menu.");

            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
        }
    }
}
