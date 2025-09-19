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

        private static readonly Vector2 UsernameFieldAnchoredPosition = new Vector2(75f, -50f);
        private static readonly Vector2 PasswordFieldAnchoredPosition = new Vector2(75f, -200f);
        private static readonly Vector2 StatusTextAnchoredPosition = new Vector2(0f, -275f);
        private const float LoginPanelVerticalOffsetFactor = 0.35f;
        private static readonly Vector3 LoginPanelScale = new Vector3(2.5f, 2f, 1f);

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

        private void Awake()
        {
            legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

            Vector2 panelAnchoredPosition = CalculatePanelAnchoredPosition(referenceResolution);

            var panelRect = CreateRectTransform("LoginPanel", rootRect,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                panelAnchoredPosition, panelSize);
            panelRect.SetAsLastSibling();
            // Apply the bespoke offset/scale so the login box matches the reference mock-up.
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
            ApplyLegacyFont(statusText);

            Vector2 buttonPosition = CalculateAnchoredPosition(LoginButtonNormalizedCenter, panelSize);
            Vector2 buttonSize = CalculateSize(LoginButtonNormalizedSize, panelSize);
            loginButton = CreateButton(panelRect, "LoginButton", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                buttonPosition, buttonSize, buttonSprite, "Login");
            ApplyLegacyFont(loginButton.GetComponentInChildren<Text>());
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
        /// Calculates the anchored position for the login panel based on the active reference
        /// resolution. This keeps the panel's relative offset consistent across 16:9 and 4:3 art.
        /// </summary>
        private static Vector2 CalculatePanelAnchoredPosition(Vector2 referenceResolution)
        {
            return new Vector2(0f, -referenceResolution.y * LoginPanelVerticalOffsetFactor);
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
            ApplyLegacyFont(text);
            text.color = new Color32(46, 32, 20, 255);

            return button;
        }
    }
}
