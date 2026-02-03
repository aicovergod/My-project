// Refactor: OSRS-style login with per-account saves; removed Overworld hop; atomic file IO; PBKDF2.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Save;
using Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using World;

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

            bool hasSavedScene = !string.IsNullOrWhiteSpace(save.savedSceneName);
            string targetScene = hasSavedScene ? save.savedSceneName : fallbackSceneName;
            Vector2 targetPosition = hasSavedScene ? new Vector2(save.savedX, save.savedY) : fallbackSpawnPosition;

            Debug.Log($"LoginFlowController: Starting login flow for '{save.username}'. SavedScene='{save.savedSceneName}', TargetScene='{targetScene}', TargetPosition={targetPosition}.", this);

            if (hasSavedScene && !PersistentSceneGate.IsSceneAllowed(targetScene))
            {
                NotifyFallback($"Saved scene '{targetScene}' is excluded by the persistent catalog. Loading fallback scene '{fallbackSceneName}'.");
                targetScene = fallbackSceneName;
                targetPosition = fallbackSpawnPosition;
                Debug.Log($"LoginFlowController: Scene '{save.savedSceneName}' blocked by gate. Switching to fallback scene '{targetScene}'.", this);
            }

            if (!CanLoadScene(targetScene))
            {
                string unavailableScene = targetScene;
                NotifyFallback($"Saved scene '{targetScene}' is unavailable. Loading fallback scene '{fallbackSceneName}'.");
                targetScene = fallbackSceneName;
                targetPosition = fallbackSpawnPosition;
                Debug.Log($"LoginFlowController: Scene '{unavailableScene}' could not be prevalidated. Falling back to '{fallbackSceneName}'.", this);
            }

            if (loginScreen != null)
            {
                // Always present a consistent login message that matches the requested
                // branding instead of echoing the scene name.
                loginScreen.SetStatus("Loading into VIosla 2D", loginScreen.InfoColour);
            }

            var transitionManager = EnsureTransitionManagerReady();
            bool manualTransitionStarted = false;
            bool manualTransitionCompleted = false;

            bool ControllerAlive()
            {
                // Unity overrides the == operator so destroyed objects evaluate to null.
                return this != null && isActiveAndEnabled;
            }

            void CompletePendingManualTransition()
            {
                if (!manualTransitionStarted || manualTransitionCompleted)
                    return;

                SceneTransitionManager liveManager = SceneTransitionManager.Instance ?? transitionManager;
                if (liveManager == null)
                    return;

                transitionManager = liveManager;
                liveManager.CompleteManualTransition(SceneManager.GetActiveScene());
                manualTransitionCompleted = true;
            }

            try
            {
                if (transitionManager != null)
                {
                    Scene pendingScene = SceneManager.GetSceneByName(targetScene);
                    transitionManager.BeginManualTransition(pendingScene);
                    manualTransitionStarted = true;
                    Debug.Log($"LoginFlowController: Manual transition begun for '{targetScene}'.", this);
                }
                else
                {
                    Debug.LogWarning("LoginFlowController: SceneTransitionManager instance was null. Proceeding without manual transition safeguards.", this);
                }

                bool loaded = await TryLoadSceneAsync(targetScene);
                if (!ControllerAlive())
                {
                    CompletePendingManualTransition();
                    return;
                }

                if (!loaded)
                {
                    if (!IsFallbackScene(targetScene))
                    {
                        NotifyFallback($"Failed to load scene '{targetScene}'. Loading fallback scene '{fallbackSceneName}'.");
                        targetScene = fallbackSceneName;
                        targetPosition = fallbackSpawnPosition;
                        loaded = await TryLoadSceneAsync(targetScene);
                        if (!ControllerAlive())
                        {
                            CompletePendingManualTransition();
                            return;
                        }
                        Debug.Log($"LoginFlowController: Retrying load using fallback scene '{targetScene}'.", this);
                    }

                    if (!loaded)
                    {
                        ReportError($"Unable to load scene '{targetScene}'.");
                        return;
                    }
                }

                await Task.Yield();
                if (!ControllerAlive())
                {
                    CompletePendingManualTransition();
                    return;
                }

                Debug.Log("LoginFlowController: Scene load reported complete. Searching for player instance.", this);
                GameObject player = await LocatePlayerAsync();
                if (!ControllerAlive())
                {
                    CompletePendingManualTransition();
                    return;
                }
                if (player == null)
                {
                    ReportError($"No Player object was found after loading '{targetScene}'.");
                    return;
                }

                Debug.Log($"LoginFlowController: Player '{player.name}' located. Moving to target position {targetPosition}.", this);

                Vector3 current = player.transform.position;
                player.transform.position = new Vector3(targetPosition.x, targetPosition.y, current.z);

                EnsureCameraFollow(player);

                if (loginScreen != null)
                    loginScreen.SetStatus("Entering the world…", loginScreen.SuccessColour);

                if (manualTransitionStarted)
                {
                    // Re-resolve the transition manager in case the original reference was destroyed or swapped during load.
                    SceneTransitionManager liveManager = SceneTransitionManager.Instance;
                    if (liveManager != null)
                        transitionManager = liveManager;

                    if (transitionManager != null)
                    {
                        transitionManager.CompleteManualTransition(SceneManager.GetActiveScene());
                        manualTransitionCompleted = true;
                        Debug.Log($"LoginFlowController: Manual transition completed for scene '{SceneManager.GetActiveScene().name}'.", this);
                    }
                }
            }
            finally
            {
                if (manualTransitionStarted && !manualTransitionCompleted && ControllerAlive())
                {
                    // Repeat the lookup during cleanup so manual transitions do not leak if the manager was replaced.
                    SceneTransitionManager liveManager = SceneTransitionManager.Instance;
                    if (liveManager != null)
                        transitionManager = liveManager;

                    if (transitionManager != null)
                    {
                        Debug.Log("LoginFlowController: Manual transition cleanup invoked after exception or early exit.", this);
                        transitionManager.CompleteManualTransition(SceneManager.GetActiveScene());
                        manualTransitionCompleted = true;
                    }
                }
            }
        }

        private bool CanLoadScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            if (!PersistentSceneGate.IsSceneAllowed(sceneName))
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
                Debug.Log($"LoginFlowController: Initiating load for scene '{sceneName}'.", this);
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

            Debug.Log($"LoginFlowController: Scene '{sceneName}' finished loading.", this);
            return true;
        }

        private async Task<GameObject> LocatePlayerAsync()
        {
            Debug.Log("LoginFlowController: Searching for player object after scene load.", this);
            float elapsed = 0f;

            while (elapsed < playerSearchTimeout)
            {
                if (PlayerLocator.TryFindPlayer(out GameObject player) && player != null)
                {
                    Debug.Log($"LoginFlowController: Player found after {elapsed:F2}s of searching.", this);
                    return player;
                }

                await Task.Yield();
                elapsed += Time.unscaledDeltaTime;
            }

            if (PlayerLocator.TryFindPlayer(out GameObject fallback) && fallback != null)
            {
                Debug.Log("LoginFlowController: Player resolved on final fallback probe after timeout.", this);
                return fallback;
            }

            Debug.LogError("LoginFlowController: Player search timed out with no result.", this);
            return null;
        }

        private void EnsureCameraFollow(GameObject player)
        {
            if (player == null)
                return;

            var followers = FindObjectsOfType<CameraFollow2D>(true);
            for (int i = 0; i < followers.Length; i++)
                followers[i].target = player.transform;
            Debug.Log($"LoginFlowController: Assigned {followers.Length} CameraFollow2D targets to the player.", this);

            var vcamType = Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine");
            if (vcamType == null)
                return;

            var vcams = FindObjectsOfType(vcamType, true);
            var followProperty = vcamType.GetProperty("Follow");
            if (followProperty == null || !followProperty.CanWrite)
                return;

            for (int i = 0; i < vcams.Length; i++)
                followProperty.SetValue(vcams[i], player.transform, null);
            Debug.Log($"LoginFlowController: Assigned player follow target to {vcams.Length} Cinemachine virtual cameras.", this);
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

        /// <summary>
        /// Ensures the <see cref="SceneTransitionManager"/> exists before manual transitions begin by
        /// loading the persistent object catalog and instantiating the prefab that contains the
        /// manager when necessary.
        /// </summary>
        private SceneTransitionManager EnsureTransitionManagerReady()
        {
            SceneTransitionManager manager = SceneTransitionManager.Instance;
            if (manager != null)
                return manager;

            PersistentObjectCatalog catalog = Resources.Load<PersistentObjectCatalog>(PersistentObjectBootstrap.CatalogResourcePath);
            if (catalog == null)
            {
                Debug.LogWarning("LoginFlowController: PersistentObjectCatalog could not be located. Scene transitions will skip manual safeguards.", this);
                return null;
            }

            IReadOnlyList<GameObject> prefabs = catalog.Prefabs;
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null)
                    continue;

                SceneTransitionManager prefabManager = prefab.GetComponentInChildren<SceneTransitionManager>(true);
                if (prefabManager == null)
                    continue;

                GameObject instance = Instantiate(prefab);
                instance.name = prefab.name;
                DontDestroyOnLoad(instance);

                if (instance.GetComponentInChildren<ScenePersistentObject>(true) == null)
                {
                    instance.AddComponent<ScenePersistentObject>();
                    Debug.LogWarning($"LoginFlowController: Prefab '{prefab.name}' was missing a ScenePersistentObject. One was added automatically.", instance);
                }

                manager = SceneTransitionManager.Instance;
                if (manager == null)
                    manager = instance.GetComponentInChildren<SceneTransitionManager>(true);

                if (manager != null)
                {
                    Debug.Log($"LoginFlowController: Instantiated SceneTransitionManager from persistent catalog prefab '{prefab.name}'.", this);
                }
                else
                {
                    Debug.LogWarning($"LoginFlowController: Prefab '{prefab.name}' did not produce a SceneTransitionManager instance after instantiation.", instance);
                }

                return manager;
            }

            Debug.LogWarning("LoginFlowController: Persistent object catalog does not contain a prefab with SceneTransitionManager. Manual transitions cannot be prepared.", this);
            return null;
        }
    }
}
