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
            if (usernameField != null && passwordField != null && statusText != null && loginButton != null && backgroundImage != null)
                return;

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
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var rootRect = transform as RectTransform;
            if (rootRect == null)
                rootRect = gameObject.AddComponent<RectTransform>();

            Sprite panelSprite = Resources.GetBuiltinResource<Sprite>("UISprite.psd");

            var backgroundRect = CreateRectTransform("Background", rootRect,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            backgroundImage = backgroundRect.gameObject.AddComponent<Image>();
            Sprite appliedBackgroundSprite = backgroundSprite != null ? backgroundSprite : panelSprite;
            backgroundImage.sprite = appliedBackgroundSprite;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.color = backgroundSprite != null ? Color.white : new Color32(16, 12, 8, 255);
            backgroundImage.preserveAspect = backgroundSprite != null;
            backgroundImage.raycastTarget = false;
            backgroundRect.SetAsFirstSibling();

            Vector2 panelSize = GetSpriteSize(loginPanelSprite, new Vector2(640f, 440f));
            var panelRect = CreateRectTransform("LoginPanel", rootRect,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, panelSize);
            var panelImage = panelRect.gameObject.AddComponent<Image>();
            Sprite appliedPanelSprite = loginPanelSprite != null ? loginPanelSprite : panelSprite;
            panelImage.sprite = appliedPanelSprite;
            bool panelHasBorder = loginPanelSprite != null && loginPanelSprite.border.sqrMagnitude > 0f;
            panelImage.type = panelHasBorder ? Image.Type.Sliced : Image.Type.Simple;
            panelImage.color = loginPanelSprite != null ? Color.white : new Color32(28, 24, 20, 220);
            panelRect.SetAsLastSibling();

            var title = CreateText(panelRect, "Title", "RuneRealm Login", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -32f), new Vector2(520f, 48f), 34, TextAnchor.UpperCenter, FontStyle.Bold);
            ApplyLegacyFont(title);

            var usernameLabel = CreateText(panelRect, "UsernameLabel", "Username", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(40f, -110f), new Vector2(200f, 32f), 20, TextAnchor.MiddleLeft, FontStyle.Bold);
            ApplyLegacyFont(usernameLabel);

            usernameField = CreateInputField(panelRect, "UsernameInput", new Vector2(40f, -150f), false, "Enter username", panelSprite);

            var passwordLabel = CreateText(panelRect, "PasswordLabel", "Password", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(40f, -210f), new Vector2(200f, 32f), 20, TextAnchor.MiddleLeft, FontStyle.Bold);
            ApplyLegacyFont(passwordLabel);

            passwordField = CreateInputField(panelRect, "PasswordInput", new Vector2(40f, -250f), true, "Enter password", panelSprite);

            statusText = CreateText(panelRect, "StatusText", string.Empty, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 96f), new Vector2(560f, 80f), 18, TextAnchor.MiddleCenter, FontStyle.Normal);
            ApplyLegacyFont(statusText);

            loginButton = CreateButton(panelRect, "LoginButton", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 28f), new Vector2(220f, 56f), panelSprite, "Login");
            ApplyLegacyFont(loginButton.GetComponentInChildren<Text>());
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

        /// <summary>
        /// Converts a sprite's pixel rect into UI units so the RectTransform matches the
        /// art-authored dimensions when rendered inside the canvas. Returns the provided
        /// fallback when a sprite is unavailable.
        /// </summary>
        private Vector2 GetSpriteSize(Sprite sprite, Vector2 fallback)
        {
            if (sprite == null)
                return fallback;

            float pixelsPerUnit = sprite.pixelsPerUnit <= 0f ? 100f : sprite.pixelsPerUnit;
            return sprite.rect.size / pixelsPerUnit;
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

        private InputField CreateInputField(RectTransform parent, string name, Vector2 anchoredPosition, bool isPassword, string placeholderText, Sprite backgroundSprite)
        {
            var rect = CreateRectTransform(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, new Vector2(360f, 44f));
            rect.gameObject.AddComponent<CanvasRenderer>();
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = backgroundSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color32(46, 40, 32, 255);

            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = image;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = isPassword ? InputField.ContentType.Password : InputField.ContentType.Standard;
            field.characterValidation = InputField.CharacterValidation.None;

            var textRect = CreateRectTransform("Text", rect, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            textRect.offsetMin = new Vector2(12f, 8f);
            textRect.offsetMax = new Vector2(-12f, -8f);
            textRect.gameObject.AddComponent<CanvasRenderer>();
            var text = textRect.gameObject.AddComponent<Text>();
            text.text = string.Empty;
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.color = new Color32(238, 225, 171, 255);
            text.raycastTarget = false;

            var placeholderRect = CreateRectTransform("Placeholder", rect, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            placeholderRect.offsetMin = new Vector2(12f, 8f);
            placeholderRect.offsetMax = new Vector2(-12f, -8f);
            placeholderRect.gameObject.AddComponent<CanvasRenderer>();
            var placeholder = placeholderRect.gameObject.AddComponent<Text>();
            placeholder.text = placeholderText;
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.fontSize = 20;
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
            image.type = Image.Type.Sliced;
            image.color = new Color32(92, 58, 30, 255);

            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = new Color32(92, 58, 30, 255);
            colors.highlightedColor = new Color32(120, 78, 40, 255);
            colors.pressedColor = new Color32(70, 46, 24, 255);
            colors.selectedColor = new Color32(120, 78, 40, 255);
            colors.disabledColor = new Color32(50, 32, 18, 180);
            button.colors = colors;

            var text = CreateText(rect, "Text", label, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 24, TextAnchor.MiddleCenter, FontStyle.Bold);
            ApplyLegacyFont(text);
            text.color = new Color32(238, 225, 171, 255);

            return button;
        }
    }
}
