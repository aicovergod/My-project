using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements; // Needed for ToolbarMenu support in modern Unity versions.
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Adds a "Scenes" dropdown button to the Unity toolbar so designers can quickly
/// switch scenes without touching the Project window. The button is injected via
/// UIElements into the play-mode toolbar zone and keeps its scene list refreshed
/// as assets or build settings change.
/// </summary>
[InitializeOnLoad]
public static class ScenesToolbar
{
    private const string ToolbarZonePlayMode = "ToolbarZonePlayMode"; // Zone containing the Play/Step controls.
    private const string ToolbarZoneRightAlign = "ToolbarZoneRightAlign"; // Fallback zone if Unity renames play zone.
    private const string ToolbarMenuName = "ScenesToolbarMenu"; // Used to guard against duplicate menus.

    private static readonly List<SceneEntry> SceneEntries = new List<SceneEntry>();
    private static ToolbarMenu toolbarMenu;
    private static string pendingSceneToOpen;

    /// <summary>
    /// Static constructor is called on load and hooks the Editor update loop so we can
    /// wait for the toolbar to be constructed before injecting our custom menu.
    /// </summary>
    static ScenesToolbar()
    {
        EditorApplication.update += TryInstallToolbarButton;
        EditorApplication.projectChanged += RefreshScenes; // Rebuild list when project assets change.
        EditorBuildSettings.sceneListChanged += RefreshScenes; // Rebuild when build settings change.
        AssemblyReloadEvents.afterAssemblyReload += RefreshScenes; // Rebuild after domain reload.
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        RefreshScenes();
    }

    /// <summary>
    /// Attempts to locate the main Unity toolbar and append our dropdown button.
    /// Uses reflection to grab the internal Toolbar instance because Unity does not
    /// currently expose a public API for the main toolbar. All lookup points are
    /// commented so they can be adjusted quickly if Unity renames any of the zones.
    /// </summary>
    private static void TryInstallToolbarButton()
    {
        if (toolbarMenu != null)
        {
            // Toolbar already patched; stop checking each frame.
            EditorApplication.update -= TryInstallToolbarButton;
            return;
        }

        Type toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
        if (toolbarType == null)
        {
            return; // Should never happen but guards older Unity versions.
        }

        UnityEngine.Object[] toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
        if (toolbars == null || toolbars.Length == 0)
        {
            return; // Toolbar not yet created this frame; try again on the next update.
        }

        // Extract the root visual element from the Toolbar instance.
        FieldInfo rootField = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
        if (rootField == null)
        {
            return; // Unity changed internals; early out so we do not spam errors.
        }

        VisualElement root = rootField.GetValue(toolbars[0]) as VisualElement;
        if (root == null)
        {
            return;
        }

        // First try to dock next to the Play button by targeting the play-mode zone.
        VisualElement targetZone = root.Q(ToolbarZonePlayMode, className: "ToolbarZone");
        if (targetZone == null)
        {
            // If Unity renames the play zone, fall back to the right-aligned zone so the button still appears.
            targetZone = root.Q(ToolbarZoneRightAlign, className: "ToolbarZone") ?? root;
        }

        // Target the inner row that contains the transport controls so our button sits on the same horizontal line.
        VisualElement insertionTarget = targetZone.Q<VisualElement>(className: "unity-toolbar__row") ?? targetZone;

        // Prevent duplicate menus if the domain reloads while the toolbar persists.
        ToolbarMenu existingMenu = insertionTarget.Q<ToolbarMenu>(ToolbarMenuName) ?? targetZone.Q<ToolbarMenu>(ToolbarMenuName);
        if (existingMenu != null)
        {
            toolbarMenu = existingMenu;
            RebuildMenu();
            EditorApplication.update -= TryInstallToolbarButton;
            return;
        }

        toolbarMenu = new ToolbarMenu
        {
            name = ToolbarMenuName,
            text = "Scenes",
            tooltip = "Switch scenes quickly without opening the Project window.",
            focusable = false
        };

        // Keep the button compact so it mirrors Unity's existing toolbar styling.
        toolbarMenu.style.flexShrink = 0;
        toolbarMenu.style.flexGrow = 0;
        toolbarMenu.style.alignSelf = Align.Center;

        // Insert directly after the play controls so it feels native.
        insertionTarget.Add(toolbarMenu);

        RebuildMenu();

        // Toolbar is now patched; stop polling.
        EditorApplication.update -= TryInstallToolbarButton;
    }

    /// <summary>
    /// Clears and rebuilds the dropdown menu contents using the cached scene list.
    /// Called whenever the toolbar is first installed and whenever the scene list refreshes.
    /// </summary>
    private static void RebuildMenu()
    {
        if (toolbarMenu == null)
        {
            return;
        }

        DropdownMenu dropdown = toolbarMenu.menu;
        dropdown.MenuItems().Clear();

        if (SceneEntries.Count == 0)
        {
            dropdown.AppendAction("No scenes found", _ => { }, DropdownMenuAction.Status.Disabled);
            dropdown.AppendSeparator();
            dropdown.AppendAction("Refresh", _ => RefreshScenes(), DropdownMenuAction.Status.Normal);
            return;
        }

        // Detect duplicate scene names so we can include the folder path for clarity.
        HashSet<string> duplicateNames = SceneEntries
            .GroupBy(scene => scene.Name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        bool hasBuildScenes = SceneEntries.Any(scene => scene.FromBuildSettings);
        bool insertedNonBuildSeparator = false;

        foreach (SceneEntry scene in SceneEntries)
        {
            if (hasBuildScenes && !scene.FromBuildSettings && !insertedNonBuildSeparator)
            {
                dropdown.AppendSeparator();
                insertedNonBuildSeparator = true;
            }

            string displayLabel = duplicateNames.Contains(scene.Name)
                ? $"{scene.Name} ({scene.RelativePath})"
                : scene.Name;

            DropdownMenuAction.Status status = DropdownMenuAction.Status.Normal;
            dropdown.AppendAction(displayLabel, _ => RequestSceneOpen(scene), status);
        }

        dropdown.AppendSeparator();
        dropdown.AppendAction("Refresh", _ => RefreshScenes(), DropdownMenuAction.Status.Normal);
    }

    /// <summary>
    /// Refreshes the cached list of project scenes. Build Settings scenes are placed first,
    /// followed by every other scene sorted alphabetically.
    /// </summary>
    private static void RefreshScenes()
    {
        SceneEntries.Clear();

        // Collect scenes that appear in the build settings first, preserving their order there.
        IEnumerable<SceneEntry> buildScenes = EditorBuildSettings.scenes
            .Where(scene => !string.IsNullOrEmpty(scene.path) && scene.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            .Select(scene => CreateSceneEntry(scene.path, true))
            .Where(entry => entry.IsValid);

        HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SceneEntry entry in buildScenes)
        {
            if (seenPaths.Add(entry.Path))
            {
                SceneEntries.Add(entry);
            }
        }

        // Discover every scene in the project and append any that were not already added.
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        List<SceneEntry> remainingScenes = new List<SceneEntry>();
        foreach (string guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seenPaths.Contains(path))
            {
                continue; // Already added via build settings.
            }

            SceneEntry entry = CreateSceneEntry(path, false);
            if (entry.IsValid)
            {
                remainingScenes.Add(entry);
                seenPaths.Add(path);
            }
        }

        remainingScenes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        SceneEntries.AddRange(remainingScenes);

        RebuildMenu();
    }

    /// <summary>
    /// Creates a descriptor for a scene asset and marks whether it originated from the build settings.
    /// </summary>
    private static SceneEntry CreateSceneEntry(string assetPath, bool fromBuildSettings)
    {
        string name = Path.GetFileNameWithoutExtension(assetPath);
        string relativePath = Path.GetDirectoryName(assetPath)?.Replace("\\", "/") ?? string.Empty;
        bool exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(assetPath) != null;
        return new SceneEntry(assetPath, name, relativePath, fromBuildSettings, exists);
    }

    /// <summary>
    /// Handles a scene selection request. Prompts to save modified scenes and opens the target
    /// scene either immediately (edit mode) or once play mode has exited.
    /// </summary>
    private static void RequestSceneOpen(SceneEntry entry)
    {
        if (!entry.IsValid)
        {
            EditorUtility.DisplayDialog("Scene Missing", $"The scene at '{entry.Path}' could not be found.", "OK");
            RefreshScenes();
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return; // User cancelled.
        }

        if (EditorApplication.isPlaying)
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Exit Play Mode?",
                "You are currently in Play Mode. Unity must exit Play Mode before the scene can be changed. Exit Play Mode now?",
                "Exit & Switch",
                "Cancel");

            if (!confirm)
            {
                return;
            }

            pendingSceneToOpen = entry.Path;
            EditorApplication.isPlaying = false; // Trigger exit; OnPlayModeStateChanged handles the actual switch.
        }
        else
        {
            OpenSceneNow(entry.Path);
        }
    }

    /// <summary>
    /// Responds to play mode state changes so we can finish opening a requested scene after exiting Play Mode.
    /// </summary>
    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(pendingSceneToOpen))
        {
            string pathToOpen = pendingSceneToOpen;
            pendingSceneToOpen = null;
            EditorApplication.delayCall += () => OpenSceneNow(pathToOpen);
        }
    }

    /// <summary>
    /// Opens the specified scene immediately in single-scene mode.
    /// </summary>
    private static void OpenSceneNow(string scenePath)
    {
        try
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to open scene '{scenePath}': {exception.Message}");
        }
    }

    /// <summary>
    /// Helper struct describing a single Unity scene asset.
    /// </summary>
    private readonly struct SceneEntry
    {
        public SceneEntry(string path, string name, string relativePath, bool fromBuildSettings, bool isValid)
        {
            Path = path;
            Name = name;
            RelativePath = relativePath;
            FromBuildSettings = fromBuildSettings;
            IsValid = isValid;
        }

        public string Path { get; }
        public string Name { get; }
        public string RelativePath { get; }
        public bool FromBuildSettings { get; }
        public bool IsValid { get; }
    }
}
