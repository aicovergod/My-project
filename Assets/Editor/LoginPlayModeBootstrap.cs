#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ensures that the login scene is always the configured Play Mode start scene when working in the editor.
/// </summary>
[InitializeOnLoad]
public static class LoginPlayModeBootstrap
{
    /// <summary>
    /// Menu path for forcing the login start scene to be reapplied manually.
    /// </summary>
    private const string ApplyMenuPath = "Tools/Login/Apply Login Play Mode Start Scene";

    /// <summary>
    /// Absolute project path to the login scene asset.
    /// </summary>
    private const string LoginScenePath = "Assets/Scenes/Login.unity";

    /// <summary>
    /// Static constructor executes on editor load and when scripts are recompiled, wiring up handlers and setting the start scene.
    /// </summary>
    static LoginPlayModeBootstrap()
    {
        // Apply the login scene immediately so the editor uses it for the next Play Mode session.
        ApplyPlayModeStartScene();

        // Ensure we keep the configuration alive after exiting Play Mode because Unity clears the reference.
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    /// <summary>
    /// Dummy method decorated with InitializeOnLoadMethod so Unity triggers the static constructor on load.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void TriggerStaticConstructor()
    {
        // Intentionally empty. Calling this method causes Unity to initialise the type, which runs the static constructor above.
    }

    /// <summary>
    /// Reapplies the login scene whenever we return to Edit Mode so Unity keeps using it as the Play Mode start scene.
    /// </summary>
    /// <param name="state">State change emitted by the editor.</param>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            // Unity clears the start scene when leaving play mode, so we reapply on both transitions for safety.
            ApplyPlayModeStartScene();
        }
    }

    /// <summary>
    /// Provides a manual way to reapply the start scene assignment in case the asset is moved or recreated.
    /// </summary>
    [MenuItem(ApplyMenuPath)]
    private static void ApplyPlayModeStartSceneMenu()
    {
        ApplyPlayModeStartScene(true);
    }

    /// <summary>
    /// Loads the login scene asset and assigns it to <see cref="EditorSceneManager.playModeStartScene"/>.
    /// </summary>
    /// <param name="reportFailure">If true, a warning is logged when the scene cannot be located.</param>
    private static void ApplyPlayModeStartScene(bool reportFailure = false)
    {
        SceneAsset loginScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(LoginScenePath);

        if (loginScene == null)
        {
            if (reportFailure)
            {
                Debug.LogWarning($"LoginPlayModeBootstrap could not find the login scene at '{LoginScenePath}'. Update the path if the scene was moved.");
            }

            return;
        }

        EditorSceneManager.playModeStartScene = loginScene;
    }
}
#endif
