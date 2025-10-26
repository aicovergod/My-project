using System;
using UnityEngine;
using World;
using Player.Ranks;
using Companions;
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
        private Vector2 scrollPosition;

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
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUIStyle.none, GUI.skin.verticalScrollbar);

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
            GUILayout.Label("Active companion status");
            DrawBooleanLabel("Has active companion", CompanionManager.HasActiveCompanion);

            var activeAction = CompanionManager.GetActiveAction();
            GUILayout.Label($"Current action: {CompanionManager.GetActiveActionDisplayName(activeAction)}");
            if (CompanionManager.HasActiveAction)
                GUILayout.Label($"Stop button label: {CompanionManager.GetStopActionLabel(activeAction)}");

            GUILayout.Space(6f);
            GUILayout.Label("Guard and storage state");
            DrawBooleanLabel("Companion stored", CompanionManager.IsStored);
            DrawBooleanLabel("Guard mode enabled", CompanionManager.GuardModeEnabled);
            DrawBooleanLabel(
                "Guard mode locked by combat cooldown",
                CompanionManager.IsGuardModeLockedByCombatCooldown,
                Color.yellow);

            GUILayout.Space(6f);
            GUILayout.Label("UI visibility");
            DrawBooleanLabel("Companion inventory visible", CompanionManager.IsInventoryVisible());
            DrawBooleanLabel("Companion equipment visible", CompanionManager.IsEquipmentVisible());
            DrawBooleanLabel(
                "Command radial visible",
                CompanionCommandMenu.IsVisible,
                new Color(0.65f, 0.85f, 1f));
            DrawBooleanLabel(
                "Cooldown inspector open",
                CompanionCooldownsWindow.Instance != null && CompanionCooldownsWindow.Instance.IsOpen,
                new Color(0.75f, 0.75f, 1f));

            GUILayout.Space(6f);
            GUILayout.Label("Debug toggles");
            DrawBooleanLabel("Companion debug logging enabled", CompanionManager.EnableDebugLogging);

            GUILayout.Space(6f);
            GUILayout.Label("Suggestion prompt state");

            DrawBooleanLabel(
                "CompanionHasAnsweredSuggestionQuestion",
                CompanionConversationService.CompanionHasAnsweredSuggestionQuestion);

            var debugState = CompanionConversationService.GetSuggestionDebugState();

            string remaining = debugState.TimeRemaining.HasValue
                ? FormatTimeSpan(debugState.TimeRemaining.Value)
                : "--";
            GUILayout.Label($"Time remaining: {remaining}");

            if (!string.IsNullOrWhiteSpace(debugState.LastSuggestion))
                GUILayout.Label($"Last suggestion: {debugState.LastSuggestion}");

            DrawBooleanLabel(
                "PlayerHasAskedCompanionSuggestionQuestionAgain",
                CompanionConversationService.PlayerHasAskedCompanionSuggestionQuestionAgain,
                Color.yellow);

            GUILayout.Space(6f);
            GUILayout.Label("Press F4 again to close this menu.");

            GUILayout.EndScrollView();

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

        /// <summary>
        /// Renders a boolean value using a consistent yes/no format and optional highlight colour.
        /// </summary>
        private static void DrawBooleanLabel(string label, bool value, Color? trueColor = null)
        {
            Color originalColor = GUI.color;

            if (trueColor.HasValue && value)
                GUI.color = trueColor.Value;

            GUILayout.Label($"{label}: {FormatBoolean(value)}");
            GUI.color = originalColor;
        }

        /// <summary>
        /// Formats a boolean into a short, human-friendly string for GUI output.
        /// </summary>
        private static string FormatBoolean(bool value)
        {
            return value ? "Yes" : "No";
        }
    }
}
