using System.Collections;
using Core.Save;
using Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UI;
using UnityEngine.InputSystem;

namespace UI.Login
{
    /// <summary>
    /// Coordinates the login panel so users can authenticate before the overworld loads. The
    /// controller validates input, forwards credential checks to
    /// <see cref="AccountProfileService"/>, and transitions to the gameplay scene after a
    /// successful login.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoginScreenController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField]
        private InputField usernameField;

        [SerializeField]
        private InputField passwordField;

        [SerializeField]
        private Text statusText;

        [SerializeField]
        private Button loginButton;

        [SerializeField]
        private Image backgroundImage;

        [SerializeField]
        private Image loginPanelImage;

        [SerializeField, Tooltip("Resources path for the login screen background sprite.")]
        private string backgroundSpritePath = "Sprites/LoginScreen/Background";

        private const string LoginPanelSpritePath = "Sprites/LoginScreen/LoginBox";

        [SerializeField, Tooltip("Resources path for the login button sprite.")]
        private string loginButtonSpritePath = "Sprites/LoginScreen/LoginButton";

        [SerializeField, Tooltip("Fallback resolution that keeps the UI sized correctly when the background sprite cannot be loaded.")]
        private Vector2 fallbackReferenceResolution = new Vector2(1024f, 768f);

        private static readonly Vector2 DefaultReferenceResolution = new Vector2(1024f, 768f);

        private static readonly Vector2 InputFieldNormalizedSize = new Vector2(0.8f, 0.11f);
        private static readonly Vector2 StatusTextNormalizedSize = new Vector2(0.82f, 0.09f);
        private static readonly Vector2 LoginButtonNormalizedSize = new Vector2(0.36f, 0.12f);
        private static readonly Vector2 LoginButtonNormalizedCenter = new Vector2(0.5f, 0.88f);

        private static readonly Vector2 UsernameFieldAnchoredPosition = new Vector2(75f, 38f);
        private static readonly Vector2 PasswordFieldAnchoredPosition = new Vector2(75f, -138f);
        private static readonly Vector2 StatusTextAnchoredPosition = new Vector2(0f, -192f);
        private static readonly Vector2 LoginPanelAnchoredPosition = new Vector2(0f, -50f);
        private static readonly Vector3 LoginPanelScale = Vector3.one;

        [Header("Status Colours")]
        [SerializeField]
        private Color successColour = new Color32(197, 183, 110, 255);

        [SerializeField]
        private Color errorColour = new Color32(198, 60, 49, 255);

        [SerializeField]
        private Color infoColour = new Color32(212, 212, 212, 255);

        [SerializeField, Tooltip("Name of the gameplay scene to load after authentication.")]
        private string gameplaySceneName = "OverWorld";

        private Coroutine loadRoutine;
        private Coroutine postLoadRoutine; // Coroutine that waits for persistent gameplay services before finishing the login flow.
        private GameObject lastSelectedInputField;
        private bool loginResumeRequested;
        private bool loadHandlerActive; // Indicates whether the login resume handler should survive scene transitions.
        private bool sceneLoadedHandlerRegistered; // Tracks the SceneManager.sceneLoaded subscription so we can unsubscribe safely.
        private bool persistentDuringLoad; // True while this controller has been marked DontDestroyOnLoad for the active transition.
        private string pendingSceneName; // Target scene that should be loaded following authentication.
        private Vector3 pendingSpawnPosition; // Position that the PlayerMover should use after the gameplay scene is ready.

        private const float PendingSpawnLogInterval = 2f;

        private void Awake()
        {
            EnsureUiHierarchy();

            if (usernameField != null)
            {
                LegacyFontProvider.ApplyTo(usernameField.textComponent);
                if (usernameField.placeholder is Text usernamePlaceholder)
                    LegacyFontProvider.ApplyTo(usernamePlaceholder);
            }

            if (passwordField != null)
            {
                LegacyFontProvider.ApplyTo(passwordField.textComponent);
                if (passwordField.placeholder is Text passwordPlaceholder)
                    LegacyFontProvider.ApplyTo(passwordPlaceholder);
            }

            LegacyFontProvider.ApplyTo(statusText);
            if (loginButton != null)
            {
                var buttonLabel = loginButton.GetComponentInChildren<Text>();
                LegacyFontProvider.ApplyTo(buttonLabel);
            }
        }

        private void OnEnable()
        {
            if (loginButton != null)
                loginButton.onClick.AddListener(HandleLoginClicked);

            if (usernameField != null)
            {
                usernameField.onValueChanged.AddListener(HandleInputChanged);
                lastSelectedInputField = usernameField.gameObject;
            }

            if (passwordField != null)
                passwordField.onValueChanged.AddListener(HandleInputChanged);

            PrefillLastUsedAccount();
            ValidateInput();
            SetStatus("Enter your credentials.", infoColour);
        }

        private void OnDisable()
        {
            if (loginButton != null)
                loginButton.onClick.RemoveListener(HandleLoginClicked);

            if (usernameField != null)
                usernameField.onValueChanged.RemoveListener(HandleInputChanged);

            if (passwordField != null)
                passwordField.onValueChanged.RemoveListener(HandleInputChanged);

            lastSelectedInputField = null;

            if (loginResumeRequested && !loadHandlerActive)
            {
                PlayerMover.CompleteLoginResume();
                loginResumeRequested = false;
            }
        }

        private void Update()
        {
            CacheActiveInputFieldSelection();
            HandleKeyboardSubmitNavigation();
        }

        private void HandleInputChanged(string _)
        {
            ValidateInput();
        }

        private void PrefillLastUsedAccount()
        {
            if (usernameField == null)
                return;

            string lastUsed = AccountProfileService.GetLastUsedDisplayName();
            if (!string.IsNullOrEmpty(lastUsed))
            {
                usernameField.text = lastUsed;
                usernameField.MoveTextEnd(false);
            }
        }

        private void ValidateInput()
        {
            if (loginButton == null)
                return;

            bool valid = usernameField != null && !string.IsNullOrWhiteSpace(usernameField.text)
                && passwordField != null && !string.IsNullOrEmpty(passwordField.text);

            loginButton.interactable = valid;
        }

        private void HandleLoginClicked()
        {
            if (loginButton != null)
                loginButton.interactable = false;

            if (statusText != null)
                SetStatus("Authenticating...", infoColour);

            string username = usernameField != null ? usernameField.text : string.Empty;
            string password = passwordField != null ? passwordField.text : string.Empty;

            bool created;
            bool success = AccountProfileService.TryAuthenticate(username, password, out created, out AccountEntry entry, out string message);

            if (!success)
            {
                SetStatus(message, errorColour);
                if (loginButton != null)
                    loginButton.interactable = true;
                return;
            }

            SetStatus(message, successColour);

            string activationMessage = AccountProfileService.ActivateAccount(entry);
            SetStatus(activationMessage, successColour);
            SaveManager.LoadAll();

            bool hasSnapshot = PlayerMover.TryGetLastSavedSnapshot(out var savedSnapshot);
            string sceneToLoad = hasSnapshot && !string.IsNullOrEmpty(savedSnapshot.SceneName)
                ? savedSnapshot.SceneName
                : gameplaySceneName;

            if (string.IsNullOrEmpty(sceneToLoad))
                sceneToLoad = gameplaySceneName;

            Vector3 spawnPosition = hasSnapshot ? savedSnapshot.Position : Vector3.zero;
            var loginSnapshot = hasSnapshot
                ? savedSnapshot
                : PlayerMover.PlayerPositionSnapshot.Create(sceneToLoad, spawnPosition, hasValidData: false);

            PlayerMover.BeginLoginResume(loginSnapshot);
            loginResumeRequested = true;

            if (postLoadRoutine != null)
            {
                StopCoroutine(postLoadRoutine);
                postLoadRoutine = null;
            }

            if (sceneLoadedHandlerRegistered)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedHandlerRegistered = false;
            }

            if (loadRoutine != null)
                StopCoroutine(loadRoutine);
            loadRoutine = StartCoroutine(LoadGameplayScene(sceneToLoad, spawnPosition, hasSnapshot));
        }

        private IEnumerator LoadGameplayScene(string sceneToLoad, Vector3 spawnPosition, bool resumingFromSnapshot)
        {
            PrepareForSceneLoad(sceneToLoad, spawnPosition);

            SetStatus(resumingFromSnapshot ? "Restoring last location..." : "Preparing the overworld...", infoColour);

            var operation = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Single);
            if (operation == null)
            {
                HandleSceneTransitionFailure($"Failed to load scene '{sceneToLoad}'.");
                loadRoutine = null;
                yield break;
            }

            operation.allowSceneActivation = true;
            while (!operation.isDone)
                yield return null;

            loadRoutine = null;
        }

        /// <summary>
        /// Marks the controller as persistent, caches the requested spawn data, and subscribes to the
        /// sceneLoaded callback so the post-load handler executes from a context that survives the
        /// upcoming scene swap.
        /// </summary>
        private void PrepareForSceneLoad(string sceneToLoad, Vector3 spawnPosition)
        {
            pendingSceneName = sceneToLoad;
            pendingSpawnPosition = spawnPosition;
            loadHandlerActive = true;

            if (!persistentDuringLoad)
            {
                DontDestroyOnLoad(gameObject);
                persistentDuringLoad = true;
            }

            if (!sceneLoadedHandlerRegistered)
            {
                SceneManager.sceneLoaded += HandleSceneLoaded;
                sceneLoadedHandlerRegistered = true;
            }
        }

        /// <summary>
        /// Starts the post-load coroutine once the requested gameplay scene finishes loading.
        /// </summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!loadHandlerActive)
                return;

            if (!string.Equals(scene.name, pendingSceneName))
                return;

            if (sceneLoadedHandlerRegistered)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedHandlerRegistered = false;
            }

            if (postLoadRoutine != null)
            {
                StopCoroutine(postLoadRoutine);
                postLoadRoutine = null;
            }

            postLoadRoutine = StartCoroutine(HandlePostSceneLoad());
        }

        /// <summary>
        /// Waits for persistent gameplay services to finish bootstrapping before finalising the login
        /// resume flow.
        /// </summary>
        private IEnumerator HandlePostSceneLoad()
        {
#if ENABLE_INPUT_SYSTEM
            // Poll for a PlayerInputManager so the player prefab can be spawned even when the
            // scene takes a frame to bootstrap its persistent objects. While waiting we also
            // watch for any active PlayerInput so duplicates are avoided when returning to the
            // overworld mid-session. The coroutine now remains patient until the manager
            // actually appears instead of aborting after an arbitrary timeout so cold boots on
            // slower devices do not bounce players back to the login screen.
            float managerLogTimer = 0f;
            bool playerInputAlreadyPresent = false;
            PlayerInputManager inputManager = null;
            while (loadHandlerActive)
            {
                playerInputAlreadyPresent = false;
                foreach (var playerInput in PlayerInput.all)
                {
                    if (playerInput != null && playerInput.isActiveAndEnabled)
                    {
                        playerInputAlreadyPresent = true;
                        break;
                    }
                }

                if (playerInputAlreadyPresent)
                    break;

                inputManager = PlayerInputManager.instance;
                if (inputManager == null)
                    inputManager = Object.FindObjectOfType<PlayerInputManager>();

                if (inputManager != null)
                    break;

                if (!IsPendingSceneStillLoaded())
                {
                    Debug.LogError("LoginScreenController: Pending gameplay scene unloaded before the PlayerInputManager became available.", this);
                    HandleSceneTransitionFailure("The gameplay scene unloaded before input could initialise. Please try again.");
                    postLoadRoutine = null;
                    yield break;
                }

                yield return null;

                managerLogTimer += Time.unscaledDeltaTime;
                if (managerLogTimer >= PendingSpawnLogInterval)
                {
                    Debug.Log($"LoginScreenController: Waiting for PlayerInputManager in scene '{pendingSceneName}'.", this);
                    managerLogTimer = 0f;
                }
            }

            if (!loadHandlerActive)
            {
                postLoadRoutine = null;
                yield break;
            }

            // If no PlayerInput existed when the manager became available, perform a final duplicate
            // check before requesting a new player join. This protects against race conditions where
            // the PlayerInput spawns naturally while we were yielding.
            if (!playerInputAlreadyPresent && inputManager != null)
            {
                foreach (var playerInput in PlayerInput.all)
                {
                    if (playerInput != null && playerInput.isActiveAndEnabled)
                    {
                        playerInputAlreadyPresent = true;
                        break;
                    }
                }

                if (!playerInputAlreadyPresent)
                {
                    Debug.Log("LoginScreenController: Requesting PlayerInputManager to spawn the local player.", this);
                    inputManager.JoinPlayer();
                }
            }
#endif

            float moverLogTimer = 0f;
            PlayerMover mover = PlayerMover.Instance;
            if (mover == null)
                mover = FindObjectOfType<PlayerMover>();

            while (loadHandlerActive && mover == null)
            {
                if (!IsPendingSceneStillLoaded())
                {
                    Debug.LogError("LoginScreenController: Gameplay scene unloaded before the PlayerMover spawned.", this);
                    HandleSceneTransitionFailure("The gameplay scene unloaded before the player prefab could spawn. Please try again.");
                    postLoadRoutine = null;
                    yield break;
                }

                yield return null;

                moverLogTimer += Time.unscaledDeltaTime;
                if (moverLogTimer >= PendingSpawnLogInterval)
                {
                    Debug.Log($"LoginScreenController: Waiting for PlayerMover in scene '{pendingSceneName}'.", this);
                    moverLogTimer = 0f;
                }

                mover = PlayerMover.Instance;
                if (mover == null)
                    mover = FindObjectOfType<PlayerMover>();
            }

            if (!loadHandlerActive)
            {
                postLoadRoutine = null;
                yield break;
            }

            if (mover == null)
            {
                Debug.LogError("LoginScreenController: PlayerMover never became available despite the gameplay scene staying loaded.", this);
                HandleSceneTransitionFailure("The gameplay scene failed to provide a player prefab. Please try again.");
                postLoadRoutine = null;
                yield break;
            }

            mover.transform.position = pendingSpawnPosition;
            mover.SavePosition(pendingSceneName, allowDuringLoginResume: true);

            if (loginResumeRequested)
            {
                PlayerMover.CompleteLoginResume();
                loginResumeRequested = false;
            }

            CompleteSuccessfulSceneTransition();
            postLoadRoutine = null;
        }

        /// <summary>
        /// Determines whether the pending gameplay scene is still present and loaded. The login
        /// resume flow only treats the transition as failed when the scene actually unloads or is
        /// replaced, preventing false negatives when the bootstrap work simply takes longer than
        /// expected on a cold start.
        /// </summary>
        private bool IsPendingSceneStillLoaded()
        {
            if (string.IsNullOrEmpty(pendingSceneName))
                return false;

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name == pendingSceneName)
                return true;

            var pendingScene = SceneManager.GetSceneByName(pendingSceneName);
            return pendingScene.IsValid() && pendingScene.isLoaded;
        }

        /// <summary>
        /// Finalises the login flow once the player has been spawned and positioned in the gameplay
        /// scene.
        /// </summary>
        private void CompleteSuccessfulSceneTransition()
        {
            loadHandlerActive = false;
            pendingSceneName = null;
            pendingSpawnPosition = Vector3.zero;

            if (loginButton != null)
                loginButton.interactable = true;

            if (persistentDuringLoad)
                persistentDuringLoad = false;

            HideAndDestroyLoginUi();
        }

        /// <summary>
        /// Restores the login UI so the player can retry when the gameplay scene fails to initialise
        /// correctly.
        /// </summary>
        private void HandleSceneTransitionFailure(string message)
        {
            loadHandlerActive = false;

            if (postLoadRoutine != null)
                postLoadRoutine = null;

            if (sceneLoadedHandlerRegistered)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneLoadedHandlerRegistered = false;
            }

            SetStatus(message, errorColour);

            if (loginButton != null)
                loginButton.interactable = true;

            if (loginResumeRequested)
            {
                PlayerMover.CompleteLoginResume();
                loginResumeRequested = false;
            }

            pendingSceneName = null;
            pendingSpawnPosition = Vector3.zero;

            if (persistentDuringLoad)
            {
                SceneManager.MoveGameObjectToScene(gameObject, SceneManager.GetActiveScene());
                persistentDuringLoad = false;
            }
        }

        /// <summary>
        /// Hides every login element before destroying the controller so the gameplay scene is free
        /// from lingering menu visuals.
        /// </summary>
        private void HideAndDestroyLoginUi()
        {
            if (backgroundImage != null)
                backgroundImage.enabled = false;

            if (loginPanelImage != null)
                loginPanelImage.enabled = false;

            if (usernameField != null)
                usernameField.gameObject.SetActive(false);

            if (passwordField != null)
                passwordField.gameObject.SetActive(false);

            if (statusText != null)
                statusText.gameObject.SetActive(false);

            if (loginButton != null)
                loginButton.gameObject.SetActive(false);

            var canvas = GetComponent<Canvas>();
            if (canvas != null)
                canvas.enabled = false;

            var raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster != null)
                raycaster.enabled = false;

            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void EnsureUiHierarchy()
        {
            if (gameObject.layer != 5)
                gameObject.layer = 5;

            var canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 0;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = SanitizeResolution(fallbackReferenceResolution);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var rootRect = transform as RectTransform;
            if (rootRect == null)
                rootRect = gameObject.AddComponent<RectTransform>();

            var backgroundSprite = Resources.Load<Sprite>(backgroundSpritePath);
            if (backgroundSprite == null)
            {
                Debug.LogWarning($"LoginScreenController: Unable to load background sprite at Resources/{backgroundSpritePath}. The login screen will use the existing solid-colour fallback.");
            }
            else
            {
                ConfigureBackground(rootRect, backgroundSprite);
            }

            Vector2 referenceResolution = DetermineReferenceResolution(backgroundSprite);
            scaler.referenceResolution = referenceResolution;

            if (usernameField != null && passwordField != null && statusText != null && loginButton != null && loginPanelImage != null)
                return;

            var panelSprite = Resources.Load<Sprite>(LoginPanelSpritePath);
            if (panelSprite == null)
            {
                Debug.LogWarning($"LoginScreenController: Unable to load login panel sprite at Resources/{LoginPanelSpritePath}. Falling back to built-in UI sprite.");
                panelSprite = Resources.GetBuiltinResource<Sprite>("UISprite.psd");
            }

            Vector2 panelSize = DeterminePanelSize(panelSprite, referenceResolution);

            Vector2 panelAnchoredPosition = LoginPanelAnchoredPosition;

            var panelRect = CreateRectTransform("LoginPanel", rootRect,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                panelAnchoredPosition, panelSize);
            panelRect.SetAsLastSibling();
            // Apply the serialized scale so the login box respects the configured pixel dimensions.
            panelRect.localScale = LoginPanelScale;
            loginPanelImage = panelRect.gameObject.AddComponent<Image>();
            if (panelSprite != null)
            {
                loginPanelImage.sprite = panelSprite;
                loginPanelImage.type = Image.Type.Simple;
                loginPanelImage.preserveAspect = true;
            }
            loginPanelImage.color = Color.white;

            Sprite buttonSprite = null;
            if (!string.IsNullOrEmpty(loginButtonSpritePath))
                buttonSprite = Resources.Load<Sprite>(loginButtonSpritePath);
            if (buttonSprite == null)
            {
                Debug.LogWarning($"LoginScreenController: Unable to load login button sprite at Resources/{loginButtonSpritePath}. Falling back to login panel sprite.");
                buttonSprite = panelSprite;
            }

            Vector2 inputFieldSize = CalculateSize(InputFieldNormalizedSize, panelSize);
            usernameField = CreateInputField(panelRect, "UsernameInput", UsernameFieldAnchoredPosition, inputFieldSize, false, "Enter username", panelSprite, true);

            passwordField = CreateInputField(panelRect, "PasswordInput", PasswordFieldAnchoredPosition, inputFieldSize, true, "Enter password", panelSprite, true);

            Vector2 statusSize = CalculateSize(StatusTextNormalizedSize, panelSize);
            int statusFontSize = Mathf.Clamp(Mathf.RoundToInt(statusSize.y * 0.28f), 18, 24);
            statusText = CreateText(panelRect, "StatusText", string.Empty, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                StatusTextAnchoredPosition, statusSize, statusFontSize, TextAnchor.MiddleCenter, FontStyle.Normal);
            LegacyFontProvider.ApplyTo(statusText);

            Vector2 buttonPosition = CalculateAnchoredPosition(LoginButtonNormalizedCenter, panelSize);
            Vector2 buttonSize = CalculateSize(LoginButtonNormalizedSize, panelSize);
            loginButton = CreateButton(panelRect, "LoginButton", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                buttonPosition, buttonSize, buttonSprite, "Login");
            LegacyFontProvider.ApplyTo(loginButton.GetComponentInChildren<Text>());
        }

        /// <summary>
        /// Allows the keyboard Enter/Return key to navigate between fields and submit the login
        /// request so players can authenticate without touching the mouse.
        /// </summary>
        private void HandleKeyboardSubmitNavigation()
        {
            if (!WasSubmitPressedThisFrame())
                return;

            GameObject selection = ResolveCurrentInputSelection();
            if (selection == null)
                return;

            if (usernameField != null && selection == usernameField.gameObject)
            {
                FocusPasswordField();
                return;
            }

            if (passwordField != null && selection == passwordField.gameObject)
            {
                TrySubmitFromPasswordField();
            }
        }

        /// <summary>
        /// Detects whether the user pressed the Enter/Return key this frame using whichever input
        /// backends are enabled in the project (new Input System and/or the legacy manager).
        /// </summary>
        private bool WasSubmitPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                return true;
#endif
            return false;
        }

        /// <summary>
        /// Records which of the login input fields the event system considers selected so the
        /// Enter key logic can still resolve the previous focus after Unity blurs the field.
        /// </summary>
        private void CacheActiveInputFieldSelection()
        {
            if (EventSystem.current == null)
                return;

            var current = EventSystem.current.currentSelectedGameObject;
            if (current == null)
                return;

            if ((usernameField != null && current == usernameField.gameObject) ||
                (passwordField != null && current == passwordField.gameObject))
            {
                lastSelectedInputField = current;
            }
        }

        /// <summary>
        /// Resolves the object that should respond to the current Enter key press by checking the
        /// event system and falling back to the last cached input field reference.
        /// </summary>
        private GameObject ResolveCurrentInputSelection()
        {
            if (EventSystem.current != null)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected != null)
                    return selected;
            }

            return lastSelectedInputField;
        }

        /// <summary>
        /// Moves keyboard focus to the password field so players can type their password after
        /// entering a username and pressing Enter.
        /// </summary>
        private void FocusPasswordField()
        {
            if (passwordField == null)
                return;

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(passwordField.gameObject);

            passwordField.ActivateInputField();
            passwordField.MoveTextEnd(false);
            passwordField.caretPosition = passwordField.text.Length;
            passwordField.selectionAnchorPosition = passwordField.caretPosition;
            passwordField.selectionFocusPosition = passwordField.caretPosition;
            lastSelectedInputField = passwordField.gameObject;
        }

        /// <summary>
        /// Attempts to submit the login form when the password field is active and the player presses
        /// Enter.
        /// </summary>
        private void TrySubmitFromPasswordField()
        {
            if (loginButton == null || !loginButton.interactable)
                return;

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            if (passwordField != null)
                passwordField.DeactivateInputField();

            HandleLoginClicked();
        }

        private void SetStatus(string message, Color colour)
        {
            if (statusText == null)
                return;

            statusText.text = message;
            statusText.color = colour;
        }

        private RectTransform CreateRectTransform(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private Text CreateText(RectTransform parent, string name, string content, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, int fontSize, TextAnchor alignment, FontStyle style)
        {
            var rect = CreateRectTransform(name, parent, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.fontStyle = style;
            text.color = new Color32(212, 212, 212, 255);
            text.supportRichText = false;
            text.raycastTarget = false;
            LegacyFontProvider.ApplyTo(text);
            return text;
        }

        /// <summary>
        /// Creates or updates the background image so the login screen displays the provided sprite.
        /// The RectTransform is configured to stretch across the full canvas while preserving the
        /// sprite's aspect ratio.
        /// </summary>
        private void ConfigureBackground(RectTransform rootRect, Sprite backgroundSprite)
        {
            RectTransform backgroundRect;
            if (backgroundImage == null)
            {
                backgroundRect = CreateRectTransform("Background", rootRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                backgroundImage = backgroundRect.gameObject.AddComponent<Image>();
            }
            else
            {
                backgroundRect = backgroundImage.rectTransform;
                backgroundRect.SetParent(rootRect, false);
                backgroundRect.name = "Background";
            }

            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = Vector2.zero;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            backgroundRect.SetAsFirstSibling();

            backgroundImage.sprite = backgroundSprite;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.preserveAspect = true;
            backgroundImage.color = Color.white;
            backgroundImage.raycastTarget = false;
        }

        /// <summary>
        /// Returns the reference resolution that should drive the login layout. When a background
        /// sprite is available we match its pixel dimensions; otherwise we fall back to the serialized
        /// resolution so the UI retains predictable sizing.
        /// </summary>
        private Vector2 DetermineReferenceResolution(Sprite backgroundSprite)
        {
            if (backgroundSprite != null)
            {
                Vector2 spriteSize = backgroundSprite.rect.size;
                if (spriteSize.x > 0f && spriteSize.y > 0f)
                    return spriteSize;
            }

            return SanitizeResolution(fallbackReferenceResolution);
        }

        /// <summary>
        /// Derives an appropriate panel size from the login box sprite and the current reference
        /// resolution. The logic mirrors the original sizing rules but now adapts to the background's
        /// pixel grid.
        /// </summary>
        private static Vector2 DeterminePanelSize(Sprite panelSprite, Vector2 referenceResolution)
        {
            Vector2 panelSize = new Vector2(640f, 440f);
            if (panelSprite == null)
                return panelSize;

            float targetHeight = Mathf.Min(referenceResolution.y * 0.7f, panelSprite.rect.height);
            float width = panelSprite.rect.width * targetHeight / panelSprite.rect.height;
            float maxWidth = referenceResolution.x * 0.6f;
            if (width > maxWidth)
            {
                float widthScale = maxWidth / width;
                targetHeight *= widthScale;
                width *= widthScale;
            }

            panelSize = new Vector2(width, targetHeight);
            return panelSize;
        }

        /// <summary>
        /// Ensures the provided resolution is valid. The layout defaults to a 1024x768 grid if the
        /// serialized fallback is missing or zero to match the new login background art.
        /// </summary>
        private static Vector2 SanitizeResolution(Vector2 resolution)
        {
            if (resolution.x <= 0f || resolution.y <= 0f)
                return DefaultReferenceResolution;

            return resolution;
        }

        private static Vector2 CalculateAnchoredPosition(Vector2 normalizedCenter, Vector2 parentSize)
        {
            return new Vector2(
                (normalizedCenter.x - 0.5f) * parentSize.x,
                (normalizedCenter.y - 0.5f) * parentSize.y);
        }

        private static Vector2 CalculateSize(Vector2 normalizedSize, Vector2 parentSize)
        {
            return new Vector2(
                normalizedSize.x * parentSize.x,
                normalizedSize.y * parentSize.y);
        }

        private InputField CreateInputField(RectTransform parent, string name, Vector2 anchoredPosition, Vector2 sizeDelta, bool isPassword, string placeholderText, Sprite backgroundSprite, bool useTransparentBackground)
        {
            var rect = CreateRectTransform(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, sizeDelta);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var image = rect.gameObject.AddComponent<Image>();
            if (!useTransparentBackground && backgroundSprite != null)
            {
                image.sprite = backgroundSprite;
                image.type = Image.Type.Sliced;
                image.color = new Color32(46, 40, 32, 255);
            }
            else
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = useTransparentBackground ? new Color(1f, 1f, 1f, 0f) : new Color32(46, 40, 32, 255);
            }
            image.raycastTarget = true;

            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = image;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = isPassword ? InputField.ContentType.Password : InputField.ContentType.Standard;
            field.characterValidation = InputField.CharacterValidation.None;
            field.keyboardType = TouchScreenKeyboardType.Default;
            field.customCaretColor = true;
            field.caretBlinkRate = 0.5f;
            field.caretWidth = 2;
            field.caretColor = new Color32(238, 225, 171, 255);
            field.selectionColor = new Color32(120, 98, 70, 160);

            var textRect = CreateRectTransform("Text", rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            textRect.offsetMin = new Vector2(24f, 14f);
            textRect.offsetMax = new Vector2(-24f, -14f);
            textRect.gameObject.AddComponent<CanvasRenderer>();
            var text = textRect.gameObject.AddComponent<Text>();
            text.text = string.Empty;
            text.fontSize = 24;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.color = new Color32(238, 225, 171, 255);
            text.raycastTarget = false;
            LegacyFontProvider.ApplyTo(text);

            var placeholderRect = CreateRectTransform("Placeholder", rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            placeholderRect.offsetMin = new Vector2(24f, 14f);
            placeholderRect.offsetMax = new Vector2(-24f, -14f);
            placeholderRect.gameObject.AddComponent<CanvasRenderer>();
            var placeholder = placeholderRect.gameObject.AddComponent<Text>();
            placeholder.text = placeholderText;
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.fontSize = 24;
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.color = new Color32(150, 150, 150, 255);
            placeholder.supportRichText = false;
            placeholder.raycastTarget = false;
            LegacyFontProvider.ApplyTo(placeholder);

            field.textComponent = text;
            field.placeholder = placeholder;

            return field;
        }

        private Button CreateButton(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, Sprite backgroundSprite, string label)
        {
            var rect = CreateRectTransform(name, parent, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = backgroundSprite;
            image.type = backgroundSprite != null ? Image.Type.Simple : Image.Type.Sliced;
            image.preserveAspect = backgroundSprite != null;
            image.color = Color.white;

            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color32(255, 244, 203, 255);
            colors.pressedColor = new Color32(214, 187, 126, 255);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color32(140, 120, 90, 180);
            button.colors = colors;

            int fontSize = Mathf.Clamp(Mathf.RoundToInt(sizeDelta.y * 0.45f), 20, 30);
            var text = CreateText(rect, "Text", label, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, fontSize, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.color = new Color32(46, 32, 20, 255);

            return button;
        }
    }
}
