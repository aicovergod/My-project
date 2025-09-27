// Refactor: OSRS-style login with per-account saves; removed Overworld hop; atomic file IO; PBKDF2.
using System;
using System.Threading.Tasks;
using Core.Save;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UI.Login
{
    /// <summary>
    /// Coordinates the login panel so users can authenticate before gameplay loads. The
    /// controller validates input, forwards credential checks to
    /// <see cref="AccountManager"/>, and hands off to <see cref="LoginFlowController"/>
    /// to resume the saved scene after a successful login.
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
        private const string LastUsedUsernameKey = "AccountManager.LastUsername";

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

        [SerializeField, Tooltip("Coordinates the scene loading and player placement once authentication succeeds.")]
        private LoginFlowController loginFlowController;
        private GameObject lastSelectedInputField;

        private void Awake()
        {
            EnsureUiHierarchy();

            if (loginFlowController == null)
                loginFlowController = GetComponent<LoginFlowController>();

            if (loginFlowController != null)
                loginFlowController.SetScreen(this);

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

            string lastUsed = PlayerPrefs.GetString(LastUsedUsernameKey, string.Empty);
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

        private async void HandleLoginClicked()
        {
            if (loginButton != null)
                loginButton.interactable = false;

            if (statusText != null)
                SetStatus("Authenticating...", infoColour);

            string username = usernameField != null ? usernameField.text : string.Empty;
            string password = passwordField != null ? passwordField.text : string.Empty;

            try
            {
                bool accountExists = AccountManager.TryLoadAccount(username, out AccountSave save);

                // Capture the login moment so the account history reflects the successful authentication.
                string loginTimestamp = DateTime.UtcNow.ToString("O");

                if (accountExists)
                {
                    if (!AccountManager.VerifyPassword(save, password))
                    {
                        SetStatus("Invalid credentials.", errorColour);
                        SetLoginButtonInteractable(true);
                        return;
                    }

                    SetStatus($"Welcome back, {save.username}.", successColour);
                }
                else
                {
                    save = AccountManager.CreateNewAccount(username, password);
                    SetStatus($"Created new account for {save.username}.", successColour);
                }

                // Persist the login timestamp explicitly rather than letting autosaves advance it.
                save.lastLoginUtc = loginTimestamp;

                SaveManager.BindAccount(save, reload: true);
                await AccountManager.SaveAsync(save);
                CacheLastUsedAccount(save.username);

                if (loginFlowController != null)
                {
                    await loginFlowController.BeginLoginFlowAsync(save);
                }
                else
                {
                    Debug.LogError("LoginScreenController: LoginFlowController reference is missing. Cannot continue into gameplay.", this);
                    SetStatus("Login succeeded but the gameplay flow is misconfigured.", errorColour);
                    SetLoginButtonInteractable(true);
                }
            }
            catch (ArgumentException ex)
            {
                Debug.LogWarning($"LoginScreenController: {ex.Message}", this);
                SetStatus(ex.Message, errorColour);
                SetLoginButtonInteractable(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"LoginScreenController: Failed to process login: {ex}", this);
                SetStatus("Unexpected error while logging in.", errorColour);
                SetLoginButtonInteractable(true);
            }
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
            // The login button art already contains the required lettering, so we pass an empty
            // label to avoid drawing a duplicate Text component on top of the sprite graphics.
            loginButton = CreateButton(panelRect, "LoginButton", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                buttonPosition, buttonSize, buttonSprite, string.Empty);

            var loginButtonLabel = loginButton.GetComponentInChildren<Text>();
            if (loginButtonLabel != null)
                LegacyFontProvider.ApplyTo(loginButtonLabel);
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

        internal void SetStatus(string message, Color colour)
        {
            if (statusText == null)
                return;

            statusText.text = message;
            statusText.color = colour;
        }

        internal Color InfoColour => infoColour;

        internal Color ErrorColour => errorColour;

        internal Color SuccessColour => successColour;

        internal void SetLoginButtonInteractable(bool interactable)
        {
            if (loginButton != null)
                loginButton.interactable = interactable;
        }

        private static void CacheLastUsedAccount(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return;

            PlayerPrefs.SetString(LastUsedUsernameKey, username);
            PlayerPrefs.Save();
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

            if (!string.IsNullOrWhiteSpace(label))
            {
                int fontSize = Mathf.Clamp(Mathf.RoundToInt(sizeDelta.y * 0.45f), 20, 30);
                var text = CreateText(rect, "Text", label, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, fontSize, TextAnchor.MiddleCenter, FontStyle.Bold);
                text.color = new Color32(46, 32, 20, 255);
            }

            return button;
        }
    }
}
