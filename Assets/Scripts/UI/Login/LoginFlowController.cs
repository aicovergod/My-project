// ------------------------------------------------------------------------------
// CHANGES: Added a dedicated login flow controller that loads the saved scene
// directly, positions the existing Player prefab, and handles fallbacks when the
// saved data is invalid.
// ------------------------------------------------------------------------------
using System;
using System.Collections;
using Core.Save;
using Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UI.Login
{
    /// <summary>
    /// Handles the post-authentication flow by loading the player's saved scene and positioning the
    /// in-scene player prefab before the gameplay HUD becomes active.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoginFlowController : MonoBehaviour
    {
        private const string PlayerPositionKey = "PlayerPosition";

        [Header("Fallback Settings")]
        [SerializeField, Tooltip("Scene to load when the saved scene is unavailable or missing.")]
        private string fallbackSceneName = "OverWorld";

        [SerializeField, Tooltip("Spawn coordinates used when no saved position exists.")]
        private Vector2 fallbackSpawnPosition = Vector2.zero;

        [SerializeField, Tooltip("Seconds spent searching for the player after a scene load before declaring failure.")]
        private float playerSearchTimeout = 5f;

        private LoginScreenController loginScreen;
        private Coroutine loginRoutine;

        [Serializable]
        private sealed class PlayerPositionRecord
        {
            public float x;
            public float y;
            public float z;
            public string scene;
        }

        private readonly struct TargetLocation
        {
            public TargetLocation(string sceneName, Vector2 position, bool applyPosition, string statusMessage, string diagnosticMessage)
            {
                SceneName = sceneName;
                Position = position;
                ApplyPosition = applyPosition;
                StatusMessage = statusMessage;
                DiagnosticMessage = diagnosticMessage;
            }

            public string SceneName { get; }
            public Vector2 Position { get; }
            public bool ApplyPosition { get; }
            public string StatusMessage { get; }
            public string DiagnosticMessage { get; }
        }

        private void Awake()
        {
            if (loginScreen == null)
                loginScreen = GetComponent<LoginScreenController>();
        }

        /// <summary>
        /// Binds the controller to the login screen so status messaging can be coordinated.
        /// </summary>
        public void SetScreen(LoginScreenController screen)
        {
            loginScreen = screen;
        }

        /// <summary>
        /// Initiates the scene-loading routine once authentication has completed.
        /// </summary>
        public void BeginLoginFlow(bool accountCreated)
        {
            if (!isActiveAndEnabled)
                return;

            if (loginRoutine != null)
                StopCoroutine(loginRoutine);

            loginRoutine = StartCoroutine(BeginLoginFlowRoutine(accountCreated));
        }

        private IEnumerator BeginLoginFlowRoutine(bool accountCreated)
        {
            var target = ResolveTargetLocation(accountCreated);
            if (!string.IsNullOrEmpty(target.DiagnosticMessage))
                Debug.LogWarning($"LoginFlowController: {target.DiagnosticMessage}", this);

            if (loginScreen != null)
                loginScreen.SetStatus(target.StatusMessage, loginScreen.InfoColour);

            yield return ResumeFromLocation(target, allowFallback: true);

            loginRoutine = null;
        }

        private TargetLocation ResolveTargetLocation(bool accountCreated)
        {
            var data = SaveManager.Load<PlayerPositionRecord>(PlayerPositionKey);
            if (data != null && !string.IsNullOrWhiteSpace(data.scene))
            {
                if (Application.CanStreamedLevelBeLoaded(data.scene))
                {
                    return new TargetLocation(
                        data.scene,
                        new Vector2(data.x, data.y),
                        applyPosition: true,
                        statusMessage: "Restoring last location…",
                        diagnosticMessage: string.Empty);
                }

                string diagnostic = $"Saved scene '{data.scene}' is not available. Defaulting to '{fallbackSceneName}'.";
                return new TargetLocation(
                    fallbackSceneName,
                    fallbackSpawnPosition,
                    applyPosition: true,
                    statusMessage: "Preparing the overworld…",
                    diagnosticMessage: diagnostic);
            }

            string status = accountCreated
                ? "Preparing the overworld…"
                : "Previous session did not record a location. Preparing the overworld…";

            string diagnosticFallback = accountCreated
                ? string.Empty
                : "No saved position was found for this profile. Falling back to the overworld.";

            return new TargetLocation(
                fallbackSceneName,
                fallbackSpawnPosition,
                applyPosition: true,
                statusMessage: status,
                diagnosticMessage: diagnosticFallback);
        }

        private IEnumerator ResumeFromLocation(TargetLocation target, bool allowFallback)
        {
            DestroyLingeringPlayers();

            bool loadSucceeded = false;
            string failure = string.Empty;

            yield return LoadSceneRoutine(target.SceneName, () => loadSucceeded = true, message => failure = message);

            if (!loadSucceeded)
            {
                string reason = string.IsNullOrEmpty(failure) ? "Unknown failure." : failure;
                Debug.LogError($"LoginFlowController: Failed to load scene '{target.SceneName}': {reason}", this);

                if (allowFallback && !IsFallbackScene(target.SceneName))
                {
                    if (loginScreen != null)
                        loginScreen.SetStatus("Failed to restore last location. Loading overworld fallback…", loginScreen.ErrorColour);

                    var fallback = CreateFallbackTarget();
                    if (loginScreen != null)
                        loginScreen.SetStatus(fallback.StatusMessage, loginScreen.InfoColour);

                    yield return ResumeFromLocation(fallback, false);
                }
                else if (loginScreen != null)
                {
                    loginScreen.SetStatus($"Failed to load scene '{target.SceneName}'.", loginScreen.ErrorColour);
                    loginScreen.SetLoginButtonInteractable(true);
                }

                yield break;
            }

            yield return null;

            GameObject player = null;
            yield return AcquirePlayerCoroutine(found => player = found);

            if (player == null)
            {
                Debug.LogError($"LoginFlowController: No Player object was found after loading scene '{target.SceneName}'.", this);

                if (allowFallback && !IsFallbackScene(target.SceneName))
                {
                    if (loginScreen != null)
                        loginScreen.SetStatus("Failed to locate player in saved scene. Loading overworld fallback…", loginScreen.ErrorColour);

                    var fallback = CreateFallbackTarget();
                    if (loginScreen != null)
                        loginScreen.SetStatus(fallback.StatusMessage, loginScreen.InfoColour);

                    yield return ResumeFromLocation(fallback, false);
                }
                else if (loginScreen != null)
                {
                    loginScreen.SetStatus("Unable to locate the player after scene load.", loginScreen.ErrorColour);
                    loginScreen.SetLoginButtonInteractable(true);
                }

                yield break;
            }

            CullDuplicatePlayers(player);

            if (target.ApplyPosition)
            {
                Vector3 current = player.transform.position;
                player.transform.position = new Vector3(target.Position.x, target.Position.y, current.z);
            }

            EnsureCameraFollow(player);
        }

        private IEnumerator LoadSceneRoutine(string sceneName, Action onSuccess, Action<string> onFailure)
        {
            AsyncOperation operation = null;

            try
            {
                operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            }
            catch (Exception ex)
            {
                onFailure?.Invoke(ex.Message);
                yield break;
            }

            if (operation == null)
            {
                onFailure?.Invoke("LoadSceneAsync returned null.");
                yield break;
            }

            while (!operation.isDone)
                yield return null;

            onSuccess?.Invoke();
        }

        private IEnumerator AcquirePlayerCoroutine(Action<GameObject> callback)
        {
            GameObject player = null;
            float elapsed = 0f;

            while (elapsed < playerSearchTimeout)
            {
                if (PlayerLocator.TryFindPlayer(out player) && player != null)
                    break;

                player = null;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            callback?.Invoke(player);
        }

        private void EnsureCameraFollow(GameObject player)
        {
            if (player == null)
                return;

            var followers = FindObjectsOfType<CameraFollow2D>(true);
            for (int i = 0; i < followers.Length; i++)
                followers[i].target = player.transform;

            var vcamType = Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine");
            if (vcamType == null)
                return;

            var vcams = FindObjectsOfType(vcamType, true);
            var followProperty = vcamType.GetProperty("Follow");
            if (followProperty == null || !followProperty.CanWrite)
                return;

            for (int i = 0; i < vcams.Length; i++)
                followProperty.SetValue(vcams[i], player.transform, null);
        }

        private static void DestroyLingeringPlayers()
        {
            GameObject[] players;

            try
            {
                players = GameObject.FindGameObjectsWithTag("Player");
            }
            catch (UnityException)
            {
                return;
            }

            for (int i = 0; i < players.Length; i++)
            {
                var candidate = players[i];
                if (candidate != null && candidate.scene.name == "DontDestroyOnLoad")
                    UnityEngine.Object.Destroy(candidate);
            }
        }

        private static void CullDuplicatePlayers(GameObject keep)
        {
            if (keep == null)
                return;

            GameObject[] players;

            try
            {
                players = GameObject.FindGameObjectsWithTag("Player");
            }
            catch (UnityException)
            {
                return;
            }

            for (int i = 0; i < players.Length; i++)
            {
                var candidate = players[i];
                if (candidate == null || candidate == keep)
                    continue;

                UnityEngine.Object.Destroy(candidate);
            }
        }

        private TargetLocation CreateFallbackTarget()
        {
            return new TargetLocation(
                fallbackSceneName,
                fallbackSpawnPosition,
                applyPosition: true,
                statusMessage: "Preparing the overworld…",
                diagnosticMessage: string.Empty);
        }

        private bool IsFallbackScene(string sceneName)
        {
            return string.Equals(sceneName, fallbackSceneName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
