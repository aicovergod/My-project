using System.Collections;
using Core.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

        [Header("Sprite Resources")]
        [SerializeField, Tooltip("Resources path for the fullscreen background sprite.")]
        private string backgroundSpritePath = "Sprites/LoginScreen/Background";

        [SerializeField, Tooltip("Resources path for the login panel sprite.")]
        private string loginPanelSpritePath = "Sprites/LoginScreen/LoginBox";

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
        private Font legacyFont;
        private Sprite backgroundSprite;
        private Sprite loginPanelSprite;

        private void Awake()
        {
            legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            LoadLoginSprites();
            ApplyLoadedSpritesToUi();
            EnsureUiHierarchy();

            ApplyLegacyFont(usernameField);
            ApplyLegacyFont(passwordField);
            ApplyLegacyFont(statusText);
            if (loginButton != null)
            {
                var buttonLabel = loginButton.GetComponentInChildren<Text>();
                ApplyLegacyFont(buttonLabel);
            }
        }

        private void OnEnable()
        {
            if (loginButton != null)
                loginButton.onClick.AddListener(HandleLoginClicked);

            if (usernameField != null)
                usernameField.onValueChanged.AddListener(HandleInputChanged);

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

            if (loadRoutine != null)
                StopCoroutine(loadRoutine);
            loadRoutine = StartCoroutine(LoadGameplayScene());
        }

        private IEnumerator LoadGameplayScene()
        {
            SetStatus("Loading world...", infoColour);

            var operation = SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                SetStatus("Failed to load the overworld scene.", errorColour);
                if (loginButton != null)
                    loginButton.interactable = true;
                yield break;
            }

            operation.allowSceneActivation = true;
            while (!operation.isDone)
                yield return null;
        }

        private void EnsureUiHierarchy()
        {
            if (usernameField == null)
                Debug.LogWarning("LoginScreenController is missing a reference to the username InputField.", this);

            if (passwordField == null)
                Debug.LogWarning("LoginScreenController is missing a reference to the password InputField.", this);

            if (statusText == null)
                Debug.LogWarning("LoginScreenController is missing a reference to the status Text component.", this);

            if (loginButton == null)
                Debug.LogWarning("LoginScreenController is missing a reference to the login Button.", this);

            if (backgroundImage == null)
                Debug.LogWarning("LoginScreenController is missing a reference to the background Image.", this);

            if (loginPanelImage == null)
                Debug.LogWarning("LoginScreenController is missing a reference to the login panel Image.", this);
        }

        /// <summary>
        /// Loads the login screen sprites from the Resources folder so the runtime UI
        /// uses the art-authored assets instead of the default Unity skin.
        /// </summary>
        private void LoadLoginSprites()
        {
            backgroundSprite = LoadSpriteFromResources(backgroundSpritePath);
            loginPanelSprite = LoadSpriteFromResources(loginPanelSpritePath);
        }

        /// <summary>
        /// Applies the loaded sprites to the serialized UI components when available so the
        /// art-authored assets appear without modifying the existing hierarchy.
        /// </summary>
        private void ApplyLoadedSpritesToUi()
        {
            if (backgroundSprite != null && backgroundImage != null)
                backgroundImage.sprite = backgroundSprite;

            if (loginPanelSprite != null && loginPanelImage != null)
            {
                loginPanelImage.sprite = loginPanelSprite;

                if (loginPanelSprite.border.sqrMagnitude > 0f)
                    loginPanelImage.type = Image.Type.Sliced;
            }
        }

        /// <summary>
        /// Attempts to load a sprite at the provided Resources path and logs a warning
        /// if the sprite cannot be found so designers can correct missing references.
        /// </summary>
        private Sprite LoadSpriteFromResources(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            Sprite sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
                Debug.LogWarning($"LoginScreenController could not locate a sprite at Resources/{resourcePath}.");
            return sprite;
        }

        private void ApplyLegacyFont(InputField field)
        {
            if (field == null || legacyFont == null)
                return;

            if (field.textComponent != null)
                field.textComponent.font = legacyFont;

            if (field.placeholder is Text placeholderText)
                placeholderText.font = legacyFont;
        }

        private void ApplyLegacyFont(Text text)
        {
            if (text == null || legacyFont == null)
                return;

            text.font = legacyFont;
        }

        private void SetStatus(string message, Color colour)
        {
            if (statusText == null)
                return;

            statusText.text = message;
            statusText.color = colour;
        }

    }
}
