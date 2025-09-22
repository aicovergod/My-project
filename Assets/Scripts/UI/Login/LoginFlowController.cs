// Refactor: OSRS-style login with per-account saves; removed Overworld hop; atomic file IO; PBKDF2.
using System;
using System.Threading.Tasks;
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
        [SerializeField]
        private string fallbackSceneName = "OverWorld";

        [SerializeField]
        private Vector2 fallbackSpawnPosition = Vector2.zero;

        [SerializeField]
        private float playerSearchTimeout = 5f;

        private LoginScreenController loginScreen;

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
        public async Task BeginLoginFlowAsync(AccountSave save)
        {
            if (!isActiveAndEnabled)
                return;

            if (save == null)
            {
                ReportError("Save data is missing. Cannot continue into gameplay.");
                return;
            }

            string targetScene = string.IsNullOrWhiteSpace(save.savedSceneName) ? fallbackSceneName : save.savedSceneName;
            Vector2 targetPosition = new Vector2(save.savedX, save.savedY);

            if (!CanLoadScene(targetScene))
            {
                NotifyFallback($"Saved scene '{targetScene}' is unavailable. Loading fallback scene '{fallbackSceneName}'.");
                targetScene = fallbackSceneName;
                targetPosition = fallbackSpawnPosition;
            }

            if (loginScreen != null)
            {
                // Always present a consistent login message that matches the requested
                // branding instead of echoing the scene name.
                loginScreen.SetStatus("Loading into VIosla 2D", loginScreen.InfoColour);
            }

            bool loaded = await TryLoadSceneAsync(targetScene);
            if (!loaded)
            {
                if (!IsFallbackScene(targetScene))
                {
                    NotifyFallback($"Failed to load scene '{targetScene}'. Loading fallback scene '{fallbackSceneName}'.");
                    targetScene = fallbackSceneName;
                    targetPosition = fallbackSpawnPosition;
                    loaded = await TryLoadSceneAsync(targetScene);
                }

                if (!loaded)
                {
                    ReportError($"Unable to load scene '{targetScene}'.");
                    return;
                }
            }

            await Task.Yield();

            GameObject player = await LocatePlayerAsync();
            if (player == null)
            {
                ReportError($"No Player object was found after loading '{targetScene}'.");
                return;
            }

            Vector3 current = player.transform.position;
            player.transform.position = new Vector3(targetPosition.x, targetPosition.y, current.z);

            EnsureCameraFollow(player);

            if (loginScreen != null)
                loginScreen.SetStatus("Entering the world…", loginScreen.SuccessColour);
        }

        private bool CanLoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            return Application.CanStreamedLevelBeLoaded(sceneName);
        }

        private async Task<bool> TryLoadSceneAsync(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            AsyncOperation operation;
            try
            {
                operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            }
            catch (Exception ex)
            {
                Debug.LogError($"LoginFlowController: Exception while loading scene '{sceneName}': {ex}", this);
                return false;
            }

            if (operation == null)
                return false;

            while (!operation.isDone)
                await Task.Yield();

            return true;
        }

        private async Task<GameObject> LocatePlayerAsync()
        {
            float elapsed = 0f;

            while (elapsed < playerSearchTimeout)
            {
                if (PlayerLocator.TryFindPlayer(out GameObject player) && player != null)
                    return player;

                await Task.Yield();
                elapsed += Time.unscaledDeltaTime;
            }

            if (PlayerLocator.TryFindPlayer(out GameObject fallback) && fallback != null)
                return fallback;

            return null;
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

        private void NotifyFallback(string message)
        {
            Debug.LogWarning($"LoginFlowController: {message}", this);
            if (loginScreen != null)
                loginScreen.SetStatus(message, loginScreen.ErrorColour);
        }

        private void ReportError(string message)
        {
            Debug.LogError($"LoginFlowController: {message}", this);
            if (loginScreen != null)
            {
                loginScreen.SetStatus(message, loginScreen.ErrorColour);
                loginScreen.SetLoginButtonInteractable(true);
            }
        }

        private bool IsFallbackScene(string sceneName)
        {
            return string.Equals(sceneName, fallbackSceneName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
