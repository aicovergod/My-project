using System;
using UnityEngine;
using World;
using Player.Ranks;
using Companions.Conversation;

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

        private Rect windowRect = new Rect(10f, 10f, 320f, 220f);
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
            bool hasDeveloperAccess = HasDeveloperAccess();
            if (!hasDeveloperAccess)
            {
                if (visible)
                    visible = false;

                return;
            }

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
            GUILayout.Label("Suggestion prompt state");

            var debugState = CompanionConversationService.GetSuggestionDebugState();
            GUILayout.Label($"CompanionHasAnsweredSuggestionQuestion: {debugState.HasActiveSuggestion}");

            string remaining = debugState.TimeRemaining.HasValue
                ? FormatTimeSpan(debugState.TimeRemaining.Value)
                : "--";
            GUILayout.Label($"Time remaining: {remaining}");

            if (!string.IsNullOrWhiteSpace(debugState.LastSuggestion))
                GUILayout.Label($"Last suggestion: {debugState.LastSuggestion}");

            Color previousColor = GUI.color;
            if (debugState.PlayerAskedAgain)
                GUI.color = Color.yellow;

            GUILayout.Label($"PlayerHasAskedCompanionSuggestionQuestionAgain: {debugState.PlayerAskedAgain}");
            GUI.color = previousColor;

            GUILayout.Space(6f);
            GUILayout.Label("Press F4 again to close this menu.");

            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
        }

        /// <summary>
        /// Determines whether the active account has developer permissions for the companion debug menu.
        /// </summary>
        private static bool HasDeveloperAccess()
        {
            var rankService = PlayerRankService.Instance;
            if (rankService == null)
                return false;

            return rankService.HasPermission(rankService.ActivePlayerRank, PlayerRank.Developer);
        }

        private static string FormatTimeSpan(TimeSpan value)
        {
            return value.TotalHours >= 1d
                ? value.ToString(@"hh\:mm\:ss")
                : value.ToString(@"mm\:ss");
        }
    }
}
