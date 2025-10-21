using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UI.Utilities;
using UI;
using Player.Ranks;

namespace UI.Chat
{
    /// <summary>
    /// Runtime-generated OSRS-style chat HUD that subscribes to <see cref="ChatService"/>
    /// and renders channel filtered chat history.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChatHudController : MonoBehaviour
    {
        private static readonly Vector2 ReferenceResolution = new Vector2(1024f, 768f);
        private static readonly Color32 PanelColor = new Color32(46, 39, 30, 220);
        private static readonly Color32 ScrollBackgroundColor = new Color32(22, 18, 14, 140);
        private static readonly Color32 InputBackgroundColor = new Color32(24, 20, 16, 200);
        private static readonly Color32 ChannelToggleEnabledColor = new Color32(88, 70, 46, 255);
        private static readonly Color32 ChannelToggleDisabledColor = new Color32(48, 38, 28, 180);
        private static readonly Color32 ChannelToggleEnabledTextColor = new Color32(255, 238, 170, 255);
        private static readonly Color32 ChannelToggleDisabledTextColor = new Color32(180, 170, 140, 160);
        private static readonly Color32 GameMessageColor = new Color32(170, 255, 170, 255);
        private static readonly Color32 CompanionMessageColor = new Color32(160, 215, 255, 255);
        private static readonly Color32 PublicMessageColor = new Color32(230, 230, 230, 255);
        private static readonly Color32 LocalPlayerMessageColor = new Color32(255, 255, 255, 255);
        private static readonly Color32 PlaceholderColor = new Color32(210, 210, 210, 140);

        private static readonly ChatChannel[] ChannelValues = ChatChannelUtility.GetOrderedChannels();
        private const string EmojiMarkupPrefix = "<emoji=";
        private const int InputCharacterLimit = 64;

        private const float WindowWidth = 520f;
        private const float WindowHeight = 220f;
        private const float ChannelPanelHeight = 44f;
        private const float WindowMargin = 18f;
        private const float WindowSpacing = 6f;

        private readonly Dictionary<ChatChannel, bool> channelFilters = new Dictionary<ChatChannel, bool>();
        private readonly Dictionary<ChatChannel, List<ChatMessage>> channelHistory = new Dictionary<ChatChannel, List<ChatMessage>>();
        private readonly Dictionary<ChatChannel, ChannelToggleState> channelToggleLookup = new Dictionary<ChatChannel, ChannelToggleState>();
        private readonly List<ChatMessage> mergedMessages = new List<ChatMessage>();
        private readonly List<ChatMessageRow> activeRows = new List<ChatMessageRow>();
        private readonly Queue<ChatMessageRow> pooledRows = new Queue<ChatMessageRow>();

        private Canvas canvas;
        private ScrollRect scrollRect;
        private RectTransform chatRoot;
        private RectTransform channelWindowRoot;
        private RectTransform windowRoot;
        private RectTransform contentRect;
        private InputField inputField;
        private RectTransform inputNameContainer;
        private Image inputNameModIcon;
        private Text inputNameLabel;
        private Text placeholderLabel;
        private EmojiTokenLayout inputPreviewRenderer;
        private Button emojiButton;
        private EmojiPickerPanel emojiPickerPanel;
        private bool autoScrollToBottom = true;
        private bool inputFocused;
        private bool inputFocusBlocked;
        private bool suppressInputValueChanged; // Prevents recursive onValueChanged handling while mutating text.
        private string previousInputText = string.Empty; // Snapshot of the previous input text for emoji deletion heuristics.

        /// <summary>
        /// Raised whenever the chat input focus state changes. The boolean argument is <c>true</c>
        /// when the input gains focus and <c>false</c> when focus is lost.
        /// </summary>
        public event Action<bool> InputFocusChanged;
        private ChatService chatService;
        private Coroutine bindRetryCoroutine;

        /// <summary>
        /// Provides runtime access to the active HUD instance.
        /// </summary>
        public static ChatHudController Instance { get; private set; }

        /// <summary>
        /// Instantiates the chat HUD under the supplied parent transform, wiring the overlay
        /// canvas through <see cref="OverlayCanvasFactory"/>.
        /// </summary>
        public static ChatHudController Create(Transform parent = null)
        {
            var components = OverlayCanvasFactory.CreateOverlayCanvas("ChatHUD", ReferenceResolution, parent, dontDestroyOnLoad: true, assignToUiLayer: true, matchWidthOrHeight: 1f);
            components.Canvas.sortingOrder = 25;
            var controller = components.Root.AddComponent<ChatHudController>();
            controller.canvas = components.Canvas;
            return controller;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            canvas ??= GetComponent<Canvas>();

            ConfigureRoot();
            BuildUi();
        }

        private void OnEnable()
        {
            InitialiseChannelState();

            if (!TryBindChatService())
            {
                Debug.LogWarning("ChatHudController: ChatService unavailable; HUD will wait for service bootstrap.");
                StartChatServiceRetry();
            }
        }

        private void OnDisable()
        {
            StopChatServiceRetry();

            if (chatService != null)
            {
                chatService.MessageReceived -= HandleMessageReceived;
                chatService.HistoryRefreshed -= HandleHistoryRefreshed;
                chatService.ActiveUsernameChanged -= HandleActiveUsernameChanged;
                chatService = null;
            }

            emojiPickerPanel?.Close();
        }

        /// <summary>
        /// Attempts to bind to the <see cref="ChatService"/> singleton and wire runtime callbacks.
        /// </summary>
        /// <returns><c>true</c> when the service is available and successfully bound, otherwise <c>false</c>.</returns>
        private bool TryBindChatService()
        {
            if (chatService != null)
                return true;

            var instance = ChatService.Instance;
            if (instance == null)
                return false;

            chatService = instance;
            chatService.MessageReceived += HandleMessageReceived;
            chatService.HistoryRefreshed += HandleHistoryRefreshed;
            chatService.ActiveUsernameChanged += HandleActiveUsernameChanged;

            UpdateActiveUsername(chatService.ActiveUsername);
            chatService.RequestFullRefresh();
            return true;
        }

        /// <summary>
        /// Starts a lightweight retry coroutine that polls for the chat service becoming available.
        /// </summary>
        private void StartChatServiceRetry()
        {
            if (bindRetryCoroutine != null)
                return;

            bindRetryCoroutine = StartCoroutine(BindChatServiceWhenAvailable());
        }

        /// <summary>
        /// Stops the retry coroutine if it is running.
        /// </summary>
        private void StopChatServiceRetry()
        {
            if (bindRetryCoroutine == null)
                return;

            StopCoroutine(bindRetryCoroutine);
            bindRetryCoroutine = null;
        }

        /// <summary>
        /// Coroutine that waits until the chat service singleton is initialised before binding callbacks.
        /// </summary>
        private IEnumerator BindChatServiceWhenAvailable()
        {
            while (!TryBindChatService())
                yield return null;

            bindRetryCoroutine = null;
        }

        /// <summary>
        /// Request focus for the chat input field, allowing the player to begin typing.
        /// </summary>
        public void FocusInput()
        {
            if (inputField == null)
                return;

            if (inputFocusBlocked)
                return;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
                eventSystem.SetSelectedGameObject(inputField.gameObject);

            inputField.ActivateInputField();
            ApplyInputFocusState(true);
            UpdateInputNameVisibility();
            RefreshInputPreview();
            CollapseInputSelection(inputField.text != null ? inputField.text.Length : 0);
        }

        /// <summary>
        /// Collapses the chat input selection to a single caret position, ensuring the
        /// text is not highlighted after programmatic focus changes.
        /// </summary>
        /// <param name="caretIndex">Requested caret index before clamping.</param>
        private void CollapseInputSelection(int caretIndex)
        {
            if (inputField == null)
                return;

            string text = inputField.text ?? string.Empty;
            int clamped = Mathf.Clamp(caretIndex, 0, text.Length);
            // UnityEngine.UI.InputField exposes caret/selection positions but lacks
            // the TMP-specific stringPosition property, so we only touch the fields
            // that exist on the legacy input component to keep compatibility.
            inputField.caretPosition = clamped;
            inputField.selectionAnchorPosition = clamped;
            inputField.selectionFocusPosition = clamped;
        }

        /// <summary>
        /// Clears the input field content without altering focus state.
        /// </summary>
        public void ClearInput()
        {
            if (inputField == null)
                return;

            SetInputFieldText(string.Empty);
            UpdateInputNameVisibility();
            RefreshInputPreview();
        }

        /// <summary>
        /// Consumes the current input text if it contains characters.
        /// </summary>
        public bool TryConsumeInput(out string message)
        {
            message = string.Empty;
            if (inputField == null)
                return false;

            string raw = inputField.text ?? string.Empty;
            string trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return false;

            message = trimmed;
            SetInputFieldText(string.Empty);
            UpdateInputNameVisibility();
            RefreshInputPreview();
            return true;
        }

        /// <summary>
        /// Defocuses the chat input field, mirroring Escape behaviour.
        /// </summary>
        public void CancelInput()
        {
            if (inputField == null)
                return;

            inputField.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == inputField.gameObject)
                EventSystem.current.SetSelectedGameObject(null);

            SetInputFieldText(string.Empty);
            ApplyInputFocusState(false);
            UpdateInputNameVisibility();
            RefreshInputPreview();
        }

        /// <summary>
        /// Updates the visibility filter for the supplied channel.
        /// </summary>
        public void SetFilter(ChatChannel channel, bool enabled)
        {
            channelFilters[channel] = enabled;
            UpdateChannelToggleVisuals(channel);
            RebuildVisibleMessages();
        }

        /// <summary>
        /// Determines whether the chat input currently holds focus.
        /// </summary>
        public bool IsInputFocused => inputFocused;

        /// <summary>
        /// Indicates whether an external system (such as the expanded minimap)
        /// is currently preventing the chat input from receiving focus.
        /// </summary>
        public bool IsInputFocusBlocked => inputFocusBlocked;

        /// <summary>
        /// Enables or disables an external focus block for the chat input. When blocked,
        /// the input field is made non-interactable and any active focus is cancelled.
        /// </summary>
        /// <param name="blocked">True to prevent the chat input from accepting focus.</param>
        public void SetInputFocusBlocked(bool blocked)
        {
            if (inputFocusBlocked == blocked)
                return;

            inputFocusBlocked = blocked;

            if (blocked)
                CancelInput();

            if (inputField != null)
                inputField.interactable = !blocked;

            UpdateInputNameVisibility();
            RefreshInputPreview();
        }

        /// <summary>
        /// Applies the supplied focus state and notifies any subscribers if the state changed.
        /// </summary>
        /// <param name="focused">Whether the input should be considered focused.</param>
        private void ApplyInputFocusState(bool focused)
        {
            if (inputFocused == focused)
                return;

            inputFocused = focused;
            InputFocusChanged?.Invoke(focused);

            if (!focused && emojiPickerPanel != null)
                emojiPickerPanel.Close();
        }

        private void ConfigureRoot()
        {
            if (chatRoot == null)
            {
                var chatRootObject = new GameObject("ChatRoot", typeof(RectTransform), typeof(VerticalLayoutGroup));
                chatRoot = chatRootObject.GetComponent<RectTransform>();
            }

            if (channelWindowRoot == null)
            {
                var channelRootObject = new GameObject("ChannelWindowRoot", typeof(RectTransform));
                channelWindowRoot = channelRootObject.GetComponent<RectTransform>();
            }

            if (windowRoot == null)
            {
                var windowRootObject = new GameObject("WindowRoot", typeof(RectTransform));
                windowRoot = windowRootObject.GetComponent<RectTransform>();
            }

            chatRoot.SetParent(transform, false);
            chatRoot.localScale = Vector3.one;
            chatRoot.anchorMin = new Vector2(0f, 0f);
            chatRoot.anchorMax = new Vector2(0f, 0f);
            chatRoot.pivot = new Vector2(0f, 0f);
            chatRoot.sizeDelta = new Vector2(WindowWidth, WindowHeight + ChannelPanelHeight + WindowSpacing);
            chatRoot.anchoredPosition = new Vector2(WindowMargin, WindowMargin);

            var layoutGroup = chatRoot.GetComponent<VerticalLayoutGroup>();
            layoutGroup.padding = new RectOffset(0, 0, 0, 0);
            layoutGroup.spacing = WindowSpacing;
            layoutGroup.childAlignment = TextAnchor.LowerLeft;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;

            windowRoot.SetParent(chatRoot, false);
            windowRoot.localScale = Vector3.one;
            windowRoot.anchorMin = new Vector2(0f, 0f);
            windowRoot.anchorMax = new Vector2(1f, 0f);
            windowRoot.pivot = new Vector2(0f, 0f);
            windowRoot.sizeDelta = new Vector2(0f, WindowHeight);
            windowRoot.SetAsFirstSibling();

            channelWindowRoot.SetParent(chatRoot, false);
            channelWindowRoot.localScale = Vector3.one;
            channelWindowRoot.anchorMin = new Vector2(0f, 0f);
            channelWindowRoot.anchorMax = new Vector2(1f, 0f);
            channelWindowRoot.pivot = new Vector2(0f, 0f);
            channelWindowRoot.sizeDelta = new Vector2(0f, ChannelPanelHeight);
            channelWindowRoot.SetAsLastSibling();

            var windowLayout = windowRoot.GetComponent<LayoutElement>();
            if (windowLayout == null)
                windowLayout = windowRoot.gameObject.AddComponent<LayoutElement>();
            windowLayout.preferredHeight = WindowHeight;
            windowLayout.minHeight = WindowHeight;
            windowLayout.flexibleHeight = 0f;
            windowLayout.flexibleWidth = 0f;

            var channelLayout = channelWindowRoot.GetComponent<LayoutElement>();
            if (channelLayout == null)
                channelLayout = channelWindowRoot.gameObject.AddComponent<LayoutElement>();
            channelLayout.preferredHeight = ChannelPanelHeight;
            channelLayout.minHeight = ChannelPanelHeight;
            channelLayout.flexibleHeight = 0f;
            channelLayout.flexibleWidth = 0f;
        }

        private void BuildUi()
        {
            if (channelWindowRoot == null)
            {
                Debug.LogError("ChatHudController: Channel window root missing during UI build.");
                return;
            }

            if (windowRoot == null)
            {
                Debug.LogError("ChatHudController: Window root missing during UI build.");
                return;
            }

            var channelBackground = new GameObject("ChannelBackground", typeof(RectTransform), typeof(Image));
            var channelBackgroundRect = channelBackground.GetComponent<RectTransform>();
            channelBackgroundRect.SetParent(channelWindowRoot, false);
            channelBackgroundRect.anchorMin = new Vector2(0f, 0f);
            channelBackgroundRect.anchorMax = new Vector2(1f, 1f);
            channelBackgroundRect.offsetMin = Vector2.zero;
            channelBackgroundRect.offsetMax = Vector2.zero;

            var channelBackgroundImage = channelBackground.GetComponent<Image>();
            channelBackgroundImage.color = PanelColor;

            var channelLayout = channelBackground.AddComponent<VerticalLayoutGroup>();
            channelLayout.padding = new RectOffset(8, 8, 6, 6);
            channelLayout.spacing = 4f;
            channelLayout.childAlignment = TextAnchor.MiddleLeft;
            channelLayout.childControlWidth = true;
            channelLayout.childControlHeight = true;
            channelLayout.childForceExpandWidth = false;
            channelLayout.childForceExpandHeight = false;

            var channelRow = CreateChannelRow(channelBackground.transform);
            float availableChannelHeight = Mathf.Max(0f, ChannelPanelHeight - channelLayout.padding.vertical);
            channelRow.preferredHeight = availableChannelHeight;
            channelRow.minHeight = availableChannelHeight;
            channelRow.flexibleWidth = 1f;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.SetParent(windowRoot, false);
            backgroundRect.anchorMin = new Vector2(0f, 0f);
            backgroundRect.anchorMax = new Vector2(1f, 1f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = PanelColor;

            var layout = background.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.LowerLeft;

            var scrollElement = CreateMessageScroll(background.transform);
            var inputRow = CreateInputRow(background.transform);

            scrollElement.flexibleHeight = 1f;
            inputRow.preferredHeight = 48f;
        }

        private LayoutElement CreateMessageScroll(Transform parent)
        {
            var scrollRoot = new GameObject("Messages", typeof(RectTransform), typeof(LayoutElement), typeof(ScrollRect));
            var scrollRectTransform = scrollRoot.GetComponent<RectTransform>();
            scrollRectTransform.SetParent(parent, false);
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = Vector2.zero;
            scrollRectTransform.offsetMax = Vector2.zero;

            scrollRect = scrollRoot.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);

            var layout = scrollRoot.GetComponent<LayoutElement>();

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(scrollRoot.transform, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewportRect.pivot = new Vector2(0.5f, 0.5f);

            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = ScrollBackgroundColor;
            viewportImage.raycastTarget = true;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentRect = content.GetComponent<RectTransform>();
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(1f, 0f);
            contentRect.pivot = new Vector2(0.5f, 0f);
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);

            var contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(4, 4, 4, 4);
            contentLayout.spacing = 2f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childAlignment = TextAnchor.LowerLeft;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            return layout;
        }

        private LayoutElement CreateChannelRow(Transform parent)
        {
            var row = new GameObject("ChannelFilters", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            var rect = row.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 0; i < ChannelValues.Length; i++)
                CreateChannelToggle(row.transform, ChannelValues[i]);

            return row.GetComponent<LayoutElement>();
        }

        private void CreateChannelToggle(Transform parent, ChatChannel channel)
        {
            var toggleRoot = new GameObject($"{channel}Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = toggleRoot.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(32f, 32f);

            var image = toggleRoot.GetComponent<Image>();
            image.color = ChannelToggleEnabledColor;

            var layout = toggleRoot.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelText = labelObject.GetComponent<Text>();
            labelObject.transform.SetParent(toggleRoot.transform, false);
            labelText.text = channel.ToString();
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.fontSize = 8;
            labelText.resizeTextForBestFit = true;
            labelText.resizeTextMinSize = 4;
            labelText.resizeTextMaxSize = 10;
            labelText.color = ChannelToggleEnabledTextColor;
            LegacyFontProvider.ApplyTo(labelText);

            var stateObject = new GameObject("State", typeof(RectTransform), typeof(Text));
            var stateText = stateObject.GetComponent<Text>();
            stateObject.transform.SetParent(toggleRoot.transform, false);
            stateText.text = "On";
            stateText.alignment = TextAnchor.MiddleCenter;
            stateText.fontSize = 7;
            stateText.resizeTextForBestFit = true;
            stateText.resizeTextMinSize = 4;
            stateText.resizeTextMaxSize = 9;
            stateText.color = ChannelToggleEnabledTextColor;
            LegacyFontProvider.ApplyTo(stateText);

            var button = toggleRoot.GetComponent<Button>();
            button.onClick.AddListener(() => ToggleChannel(channel));

            channelToggleLookup[channel] = new ChannelToggleState
            {
                Button = button,
                Background = image,
                Label = labelText,
                StateLabel = stateText,
                Enabled = true
            };
        }

        private LayoutElement CreateInputRow(Transform parent)
        {
            var row = new GameObject("InputRow", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            var rect = row.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var inputStack = new GameObject("InputStack", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var inputStackRect = inputStack.GetComponent<RectTransform>();
            inputStackRect.SetParent(row.transform, false);
            inputStackRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 460f);

            var stackLayout = inputStack.GetComponent<VerticalLayoutGroup>();
            stackLayout.spacing = 2f;
            stackLayout.padding = new RectOffset(0, 0, 0, 0);
            stackLayout.childAlignment = TextAnchor.UpperLeft;
            stackLayout.childControlWidth = true;
            stackLayout.childControlHeight = true;
            stackLayout.childForceExpandWidth = true;
            stackLayout.childForceExpandHeight = false;

            var stackLayoutElement = inputStack.GetComponent<LayoutElement>();
            stackLayoutElement.preferredWidth = 460f;
            stackLayoutElement.minWidth = 460f;
            stackLayoutElement.flexibleWidth = 0f;

            BuildInputNameRow(inputStack.transform);

            var inputContainer = new GameObject("InputContainer", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var inputContainerRect = inputContainer.GetComponent<RectTransform>();
            inputContainerRect.SetParent(inputStack.transform, false);
            inputContainerRect.anchorMin = new Vector2(0f, 0f);
            inputContainerRect.anchorMax = new Vector2(1f, 0f);
            inputContainerRect.offsetMin = Vector2.zero;
            inputContainerRect.offsetMax = Vector2.zero;

            var inputContainerLayout = inputContainer.GetComponent<LayoutElement>();
            inputContainerLayout.preferredHeight = 32f;
            inputContainerLayout.minHeight = 32f;
            inputContainerLayout.flexibleHeight = 0f;
            inputContainerLayout.flexibleWidth = 0f;

            var inputBackground = inputContainer.GetComponent<Image>();
            inputBackground.color = InputBackgroundColor;

            inputField = inputContainer.AddComponent<InputField>();
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.characterLimit = 0;
            inputField.transition = Selectable.Transition.ColorTint;
            RegisterInputFocusCallbacks(inputField);
            inputField.onValueChanged.AddListener(HandleInputValueChanged);

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(inputContainer.transform, false);
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(6f, 4f);
            textRect.offsetMax = new Vector2(-6f, -4f);

            var inputText = textObject.GetComponent<Text>();
            inputText.text = string.Empty;
            inputText.fontSize = 16;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
            inputText.verticalOverflow = VerticalWrapMode.Overflow;
            inputText.color = LocalPlayerMessageColor;
            LegacyFontProvider.ApplyTo(inputText);
            inputText.color = new Color(inputText.color.r, inputText.color.g, inputText.color.b, 0f);

            var placeholderObject = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            var placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.SetParent(inputContainer.transform, false);
            placeholderRect.anchorMin = new Vector2(0f, 0f);
            placeholderRect.anchorMax = new Vector2(1f, 1f);
            placeholderRect.offsetMin = new Vector2(6f, 4f);
            placeholderRect.offsetMax = new Vector2(-6f, -4f);

            placeholderLabel = placeholderObject.GetComponent<Text>();
            placeholderLabel.text = "Type message...";
            placeholderLabel.fontSize = 16;
            placeholderLabel.alignment = TextAnchor.MiddleLeft;
            placeholderLabel.color = PlaceholderColor;
            LegacyFontProvider.ApplyTo(placeholderLabel);

            inputField.textComponent = inputText;
            inputField.placeholder = placeholderLabel;

            var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(EmojiTokenLayout));
            var previewRect = previewObject.GetComponent<RectTransform>();
            previewRect.SetParent(inputContainer.transform, false);
            previewRect.anchorMin = new Vector2(0f, 0f);
            previewRect.anchorMax = new Vector2(1f, 1f);
            previewRect.offsetMin = new Vector2(6f, 4f);
            previewRect.offsetMax = new Vector2(-6f, -4f);
            inputPreviewRenderer = previewObject.GetComponent<EmojiTokenLayout>();
            previewRect.SetSiblingIndex(placeholderRect.GetSiblingIndex());

            UpdateInputNameVisibility();

            var emojiButtonObject = new GameObject("EmojiButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var emojiButtonRect = emojiButtonObject.GetComponent<RectTransform>();
            emojiButtonRect.SetParent(row.transform, false);
            emojiButtonRect.sizeDelta = new Vector2(32f, 32f);

            var emojiButtonLayout = emojiButtonObject.GetComponent<LayoutElement>();
            emojiButtonLayout.preferredWidth = 32f;
            emojiButtonLayout.preferredHeight = 32f;
            emojiButtonLayout.minWidth = 32f;
            emojiButtonLayout.minHeight = 32f;
            emojiButtonLayout.flexibleWidth = 0f;
            emojiButtonLayout.flexibleHeight = 0f;

            var emojiButtonImage = emojiButtonObject.GetComponent<Image>();
            emojiButtonImage.color = ChannelToggleEnabledColor;
            var emojiSprite = Resources.Load<Sprite>("Sprites/Chatbox/Button");
            if (emojiSprite != null)
            {
                emojiButtonImage.sprite = emojiSprite;
                emojiButtonImage.type = Image.Type.Sliced;
            }

            emojiButton = emojiButtonObject.GetComponent<Button>();
            emojiButton.onClick.AddListener(HandleEmojiButtonClicked);

            EnsureEmojiPicker();
            RefreshInputPreview();
            previousInputText = inputField.text ?? string.Empty;

            return row.GetComponent<LayoutElement>();
        }

        /// <summary>
        /// Wires select and deselect triggers so the HUD can mirror the current focus state
        /// without relying on deprecated <see cref="InputField.onSelect"/> events.
        /// </summary>
        /// <param name="field">The input field that should report focus changes.</param>
        private void RegisterInputFocusCallbacks(InputField field)
        {
            if (field == null)
                return;

            var trigger = field.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = field.gameObject.AddComponent<EventTrigger>();

            trigger.triggers ??= new List<EventTrigger.Entry>();

            AppendEventTrigger(trigger, EventTriggerType.Select, HandleInputSelected);
            AppendEventTrigger(trigger, EventTriggerType.Deselect, HandleInputDeselected);
        }

        /// <summary>
        /// Adds an <see cref="EventTrigger"/> entry that forwards to the provided handler.
        /// </summary>
        /// <param name="trigger">Trigger instance to append the entry to.</param>
        /// <param name="eventType">The UI event that should invoke the callback.</param>
        /// <param name="handler">Callback invoked when the event fires.</param>
        private static void AppendEventTrigger(EventTrigger trigger, EventTriggerType eventType, Action<BaseEventData> handler)
        {
            if (trigger == null || handler == null)
                return;

            var entry = new EventTrigger.Entry
            {
                eventID = eventType
            };
            entry.callback.AddListener(evt => handler(evt));
            trigger.triggers.Add(entry);
        }

        /// <summary>
        /// Marks the chat input as focused when the EventSystem selects the input field.
        /// </summary>
        /// <param name="_">Unused event payload.</param>
        private void HandleInputSelected(BaseEventData _)
        {
            if (inputFocusBlocked)
            {
                CancelInput();
                return;
            }

            ApplyInputFocusState(true);
            UpdateInputNameVisibility();
            CollapseInputSelection(inputField != null && inputField.text != null ? inputField.text.Length : 0);
        }

        /// <summary>
        /// Marks the chat input as unfocused when the EventSystem deselects the input field.
        /// </summary>
        /// <param name="_">Unused event payload.</param>
        private void HandleInputDeselected(BaseEventData _)
        {
            ApplyInputFocusState(false);
            UpdateInputNameVisibility();
        }

        /// <summary>
        /// Reacts to runtime text changes so the input name label can mirror the current state.
        /// </summary>
        /// <param name="_">Unused text payload.</param>
        private void HandleInputValueChanged(string _)
        {
            if (suppressInputValueChanged)
                return;

            TryCollapseEmojiBackspace();
            EnforceInputCharacterLimit();
            UpdateInputNameVisibility();
            RefreshInputPreview();
            previousInputText = inputField != null ? inputField.text ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// Ensures the input name label is only visible when the input is not actively being edited.
        /// </summary>
        private void UpdateInputNameVisibility()
        {
            if (inputNameContainer == null)
                return;

            bool hasInputField = inputField != null;
            bool hasText = hasInputField && !string.IsNullOrEmpty(inputField.text);
            bool visible = !inputFocused || !hasText;
            inputNameContainer.gameObject.SetActive(visible);
        }

        private void RefreshInputPreview()
        {
            if (inputPreviewRenderer == null)
                return;

            string text = inputField != null ? inputField.text ?? string.Empty : string.Empty;
            // Prevent moderator badge markup from rendering in the live preview so non-moderators cannot spoof icons while typing.
            var tokens = EmojiMarkupParser.Parse(text, allowModeratorIcons: false);
            inputPreviewRenderer.RenderTokens(tokens, LocalPlayerMessageColor, 16, TextAnchor.MiddleLeft);
        }

        private void HandleEmojiButtonClicked()
        {
            if (emojiPickerPanel == null)
                return;

            if (emojiPickerPanel.IsOpen)
                emojiPickerPanel.Close();
            else
                emojiPickerPanel.Open(emojiButton?.GetComponent<RectTransform>(), HandleEmojiSelected);
        }

        private void HandleEmojiSelected(string key)
        {
            if (string.IsNullOrEmpty(key) || inputField == null)
                return;

            string markup = $"<emoji={key}>";
            string current = inputField.text ?? string.Empty;
            string updated = current + markup;
            int caret = updated.Length;
            SetInputFieldText(updated, caret);
            EnforceInputCharacterLimit();
            RefreshInputPreview();
            UpdateInputNameVisibility();
            inputField.ActivateInputField();
            int enforcedCaret = inputField != null ? inputField.caretPosition : caret;
            CollapseInputSelection(enforcedCaret);
        }

        /// <summary>
        /// Applies a new value to the chat input while suppressing recursive
        /// <see cref="InputField.onValueChanged"/> callbacks so helper logic can
        /// safely mutate the text.
        /// </summary>
        /// <param name="text">The string that should be assigned to the input field.</param>
        /// <param name="caretPosition">Optional caret index to apply after setting the text.</param>
        private void SetInputFieldText(string text, int? caretPosition = null)
        {
            if (inputField == null)
                return;

            suppressInputValueChanged = true;
            inputField.text = text ?? string.Empty;
            suppressInputValueChanged = false;
            previousInputText = inputField.text ?? string.Empty;

            if (caretPosition.HasValue)
                CollapseInputSelection(caretPosition.Value);
        }

        /// <summary>
        /// Detects when the player attempts to backspace through an emoji tag and
        /// removes the full markup sequence so the emoji behaves like a single unit.
        /// </summary>
        private void TryCollapseEmojiBackspace()
        {
            if (inputField == null)
                return;

            string current = inputField.text ?? string.Empty;
            string previous = previousInputText ?? string.Empty;
            if (previous.Length <= current.Length)
                return;

            int removedCount = previous.Length - current.Length;
            if (removedCount != 1)
                return;

            int caret = inputField.caretPosition;
            int anchor = inputField.selectionAnchorPosition;
            int focus = inputField.selectionFocusPosition;
            if (caret != anchor || caret != focus)
                return;

            if (caret < 0 || caret > previous.Length)
                return;

            int searchStart = Mathf.Clamp(caret - 1, 0, Math.Max(previous.Length - 1, 0));
            int startIndex = previous.LastIndexOf(EmojiMarkupPrefix, searchStart, StringComparison.Ordinal);
            if (startIndex < 0)
                return;

            int closingIndex = previous.IndexOf('>', startIndex);
            if (closingIndex != caret)
                return;

            int keyStart = startIndex + EmojiMarkupPrefix.Length;
            if (keyStart >= closingIndex)
                return;

            for (int i = keyStart; i < closingIndex; i++)
            {
                char keyChar = previous[i];
                if (char.IsWhiteSpace(keyChar) || keyChar == '<' || keyChar == '>')
                    return;
            }

            string updated = previous.Remove(startIndex, closingIndex - startIndex + 1);
            SetInputFieldText(updated, startIndex);
        }

        /// <summary>
        /// Ensures the chat input text and caret respect the configured logical character limit.
        /// </summary>
        private void EnforceInputCharacterLimit()
        {
            if (inputField == null)
                return;

            string text = inputField.text ?? string.Empty;
            int caret = Mathf.Clamp(inputField.caretPosition, 0, text.Length);

            if (!TryTruncateEmojiAware(text, caret, out string truncatedText, out int truncatedCaret))
                return;

            if (!string.Equals(truncatedText, text, StringComparison.Ordinal))
            {
                SetInputFieldText(truncatedText, truncatedCaret);
            }
            else if (caret != truncatedCaret)
            {
                CollapseInputSelection(truncatedCaret);
                previousInputText = truncatedText;
            }
        }

        /// <summary>
        /// Truncates the supplied text so that no more than the logical character limit is represented.
        /// </summary>
        /// <param name="text">Raw chat input text.</param>
        /// <param name="caretPosition">Current caret index within the raw text.</param>
        /// <param name="truncatedText">Resulting truncated text if trimming was required.</param>
        /// <param name="truncatedCaret">Caret index aligned to the truncated text.</param>
        /// <returns>True if the caret or text were modified, otherwise false.</returns>
        private bool TryTruncateEmojiAware(string text, int caretPosition, out string truncatedText, out int truncatedCaret)
        {
            truncatedText = text ?? string.Empty;
            truncatedCaret = Mathf.Clamp(caretPosition, 0, truncatedText.Length);

            if (string.IsNullOrEmpty(truncatedText))
                return false;

            int logicalCount = 0;
            int index = 0;
            int allowedIndex = 0;
            bool caretAdjusted = false;

            while (index < truncatedText.Length)
            {
                if (logicalCount >= InputCharacterLimit)
                    break;

                int tokenLength = 1;
                if (TryReadEmojiMarkup(truncatedText, index, out int emojiLength))
                    tokenLength = emojiLength;

                int nextIndex = index + tokenLength;
                logicalCount++;
                allowedIndex = nextIndex;

                if (truncatedCaret > index && truncatedCaret < nextIndex)
                {
                    truncatedCaret = nextIndex;
                    caretAdjusted = true;
                }

                index = nextIndex;
            }

            bool truncated = allowedIndex < truncatedText.Length;
            if (truncated)
            {
                truncatedText = truncatedText.Substring(0, allowedIndex);
                if (truncatedCaret > allowedIndex)
                {
                    truncatedCaret = allowedIndex;
                    caretAdjusted = true;
                }
            }

            truncatedCaret = Mathf.Clamp(truncatedCaret, 0, truncatedText.Length);
            return truncated || caretAdjusted;
        }

        /// <summary>
        /// Attempts to read a complete emoji markup sequence beginning at the supplied index.
        /// </summary>
        /// <param name="text">Source text to inspect.</param>
        /// <param name="startIndex">Potential starting index of the markup sequence.</param>
        /// <param name="length">Length of the markup token if a valid sequence was discovered.</param>
        /// <returns>True when the substring matches <c>&lt;emoji=KEY&gt;</c> format, otherwise false.</returns>
        private bool TryReadEmojiMarkup(string text, int startIndex, out int length)
        {
            length = 0;

            if (string.IsNullOrEmpty(text))
                return false;

            if (startIndex < 0 || startIndex >= text.Length)
                return false;

            if (text.IndexOf(EmojiMarkupPrefix, startIndex, StringComparison.Ordinal) != startIndex)
                return false;

            int keyStart = startIndex + EmojiMarkupPrefix.Length;
            if (keyStart >= text.Length)
                return false;

            int closingIndex = text.IndexOf('>', keyStart);
            if (closingIndex < 0)
                return false;

            if (closingIndex == keyStart)
                return false;

            for (int i = keyStart; i < closingIndex; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c) || c == '<' || c == '>')
                    return false;
            }

            length = closingIndex - startIndex + 1;
            return length > 0;
        }

        private void EnsureEmojiPicker()
        {
            if (emojiPickerPanel != null)
                return;

            emojiPickerPanel = EmojiPickerPanel.Create(chatRoot);
        }

        private void BuildInputNameRow(Transform parent)
        {
            var nameRow = new GameObject("InputNameRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            inputNameContainer = nameRow.GetComponent<RectTransform>();
            inputNameContainer.SetParent(parent, false);
            inputNameContainer.anchorMin = new Vector2(0f, 0f);
            inputNameContainer.anchorMax = new Vector2(1f, 0f);
            inputNameContainer.offsetMin = Vector2.zero;
            inputNameContainer.offsetMax = Vector2.zero;

            var layout = nameRow.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(6, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var iconObject = new GameObject("ModIcon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.SetParent(inputNameContainer, false);
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);

            inputNameModIcon = iconObject.GetComponent<Image>();
            inputNameModIcon.raycastTarget = false;
            inputNameModIcon.gameObject.SetActive(false);

            var iconLayout = iconObject.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = 16f;
            iconLayout.preferredHeight = 16f;
            iconLayout.minWidth = 16f;
            iconLayout.minHeight = 16f;
            iconLayout.flexibleWidth = 0f;
            iconLayout.flexibleHeight = 0f;

            inputNameLabel = CreateTextLabel(inputNameContainer, string.Empty, 16, PublicMessageColor);
        }

        private Text CreateTextLabel(Transform parent, string text, int fontSize, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = Vector2.zero;

            var uiText = go.GetComponent<Text>();
            uiText.text = text;
            uiText.fontSize = fontSize;
            uiText.alignment = TextAnchor.MiddleLeft;
            uiText.color = color;
            uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;
            LegacyFontProvider.ApplyTo(uiText);

            return uiText;
        }

        private void InitialiseChannelState()
        {
            for (int i = 0; i < ChannelValues.Length; i++)
            {
                var channel = ChannelValues[i];
                channelFilters[channel] = true;
                if (!channelHistory.TryGetValue(channel, out var list))
                    channelHistory[channel] = new List<ChatMessage>();
                else
                    list.Clear();

                UpdateChannelToggleVisuals(channel);
            }
        }

        private void ToggleChannel(ChatChannel channel)
        {
            bool current = channelFilters.TryGetValue(channel, out bool enabled) ? enabled : true;
            SetFilter(channel, !current);
        }

        private void UpdateChannelToggleVisuals(ChatChannel channel)
        {
            if (!channelToggleLookup.TryGetValue(channel, out var state))
                return;

            bool enabled = channelFilters.TryGetValue(channel, out var value) ? value : true;
            state.Enabled = enabled;
            state.Background.color = enabled ? ChannelToggleEnabledColor : ChannelToggleDisabledColor;
            state.Label.color = enabled ? ChannelToggleEnabledTextColor : ChannelToggleDisabledTextColor;
            state.StateLabel.color = enabled ? ChannelToggleEnabledTextColor : ChannelToggleDisabledTextColor;
            state.StateLabel.text = enabled ? "On" : "Off";
        }

        private void HandleMessageReceived(ChatMessage message)
        {
            if (!channelHistory.TryGetValue(message.Channel, out var list))
            {
                list = new List<ChatMessage>();
                channelHistory[message.Channel] = list;
            }

            list.Add(message);

            if (chatService != null)
            {
                int limit = Mathf.Max(1, chatService.HistoryLimit);
                if (list.Count > limit)
                    list.RemoveRange(0, list.Count - limit);
            }

            bool channelEnabled = channelFilters.TryGetValue(message.Channel, out bool enabled) ? enabled : true;
            if (!channelEnabled)
                return;

            RebuildVisibleMessages();
            ScrollToBottom();
        }

        private void HandleHistoryRefreshed(ChatChannel channel, IReadOnlyList<ChatMessage> history)
        {
            if (!channelHistory.TryGetValue(channel, out var list))
            {
                list = new List<ChatMessage>(history.Count);
                channelHistory[channel] = list;
            }
            else
            {
                list.Clear();
            }

            for (int i = 0; i < history.Count; i++)
                list.Add(history[i]);

            RebuildVisibleMessages();
        }

        private void HandleActiveUsernameChanged(string username)
        {
            UpdateActiveUsername(username);
        }

        private void UpdateActiveUsername(string username)
        {
            if (inputNameLabel == null)
                return;

            string displayName = string.IsNullOrEmpty(username) ? "Adventurer" : username;
            inputNameLabel.text = $"{displayName}:";
            UpdateInputNameModIcon(username);
        }

        private void UpdateInputNameModIcon(string username)
        {
            if (inputNameModIcon == null)
                return;

            if (string.IsNullOrWhiteSpace(username))
            {
                inputNameModIcon.gameObject.SetActive(false);
                inputNameModIcon.sprite = null;
                return;
            }

            var rankService = PlayerRankService.Instance;
            PlayerRank? rank = rankService?.GetRankForUsername(username);
            if (!rank.HasValue || !TryGetRankIconKey(rank.Value, out string iconKey))
            {
                inputNameModIcon.gameObject.SetActive(false);
                inputNameModIcon.sprite = null;
                return;
            }

            if (!ModIconAtlas.Instance.TryGetEmoji(iconKey, out var definition))
            {
                inputNameModIcon.gameObject.SetActive(false);
                inputNameModIcon.sprite = null;
                return;
            }

            definition.ApplyTo(inputNameModIcon);
            inputNameModIcon.gameObject.SetActive(true);
        }

        private void RebuildVisibleMessages()
        {
            mergedMessages.Clear();
            for (int i = 0; i < ChannelValues.Length; i++)
            {
                var channel = ChannelValues[i];
                if (!channelFilters.TryGetValue(channel, out bool enabled) || !enabled)
                    continue;

                if (!channelHistory.TryGetValue(channel, out var list) || list.Count == 0)
                    continue;

                mergedMessages.AddRange(list);
            }

            mergedMessages.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));

            for (int i = 0; i < mergedMessages.Count; i++)
            {
                var row = GetRow(i);
                var message = mergedMessages[i];
                var prefixTokens = BuildMessagePrefixTokens(message);
                // Moderator icons are injected via the prefix when authorised. Disable them in the body so arbitrary markup
                // cannot create fake badges.
                var messageTokens = EmojiMarkupParser.Parse(message.Text ?? string.Empty, EmojiAtlas.Instance, allowModeratorIcons: false);
                row.SetTokens(prefixTokens, messageTokens, ResolveMessageColor(message));
            }

            for (int i = mergedMessages.Count; i < activeRows.Count; i++)
            {
                var row = activeRows[i];
                row.SetActive(false);
                pooledRows.Enqueue(row);
            }

            if (activeRows.Count > mergedMessages.Count)
                activeRows.RemoveRange(mergedMessages.Count, activeRows.Count - mergedMessages.Count);

            if (autoScrollToBottom)
                ScrollToBottom();
        }

        private ChatMessageRow GetRow(int index)
        {
            if (index < activeRows.Count)
            {
                var existing = activeRows[index];
                existing.SetActive(true);
                return existing;
            }

            ChatMessageRow row;
            if (pooledRows.Count > 0)
            {
                row = pooledRows.Dequeue();
                row.SetActive(true);
                row.RectTransform.SetParent(contentRect, false);
            }
            else
            {
                row = new ChatMessageRow(contentRect);
            }

            activeRows.Add(row);
            return row;
        }

        private void ScrollToBottom()
        {
            if (scrollRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        private void OnScrollValueChanged(Vector2 position)
        {
            autoScrollToBottom = position.y <= 0.001f;
        }

        /// <summary>
        /// Generates the tokenised prefix for a chat line, including timestamps and optional moderator icons.
        /// </summary>
        /// <param name="message">Chat message currently being rendered.</param>
        /// <returns>List of tokens representing the formatted prefix.</returns>
        private List<EmojiMarkupToken> BuildMessagePrefixTokens(ChatMessage message)
        {
            string markup = ComposeMessagePrefixMarkup(message);
            return EmojiMarkupParser.Parse(markup, EmojiAtlas.Instance, ModIconAtlas.Instance);
        }

        /// <summary>
        /// Builds the markup string that precedes the chat message payload.
        /// </summary>
        /// <param name="message">Chat message currently being rendered.</param>
        /// <returns>Markup string containing the timestamp, moderator icon (when available), and display name.</returns>
        private string ComposeMessagePrefixMarkup(ChatMessage message)
        {
            var builder = new StringBuilder(64);

            DateTime localTime = message.TimestampUtc.ToLocalTime();
            string timestamp = localTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            builder.Append('[').Append(timestamp).Append("] ");

            if (TryResolveModIconKey(message, out string iconKey))
            {
                builder.Append("<ModIcon=").Append(iconKey).Append("> ");
            }

            builder.Append(ResolvePrefixDisplayName(message)).Append(": ");
            return builder.ToString();
        }

        /// <summary>
        /// Resolves the display name that should appear before the chat message body.
        /// </summary>
        /// <param name="message">Chat message being rendered.</param>
        /// <returns>Display name selected for the prefix.</returns>
        private static string ResolvePrefixDisplayName(ChatMessage message)
        {
            if (message.Channel == ChatChannel.Game)
                return "Game";

            if (message.Channel == ChatChannel.Companion && string.IsNullOrEmpty(message.Sender))
                return "Companion";

            return !string.IsNullOrEmpty(message.Sender) ? message.Sender : "Player";
        }

        /// <summary>
        /// Attempts to map the message author to a moderator icon key using <see cref="PlayerRankService"/>.
        /// </summary>
        /// <param name="message">Chat message being rendered.</param>
        /// <param name="iconKey">Resolved icon key when the sender qualifies for an icon.</param>
        /// <returns><c>true</c> when an icon should be rendered; otherwise <c>false</c>.</returns>
        private bool TryResolveModIconKey(ChatMessage message, out string iconKey)
        {
            iconKey = string.Empty;

            if (message.Channel == ChatChannel.Game)
                return false;

            if (string.IsNullOrWhiteSpace(message.Sender))
                return false;

            PlayerRank? rank = PlayerRankService.Instance?.GetRankForUsername(message.Sender);
            if (!rank.HasValue)
                return false;

            return TryGetRankIconKey(rank.Value, out iconKey);
        }

        /// <summary>
        /// Maps a resolved <see cref="PlayerRank"/> to the corresponding moderator icon key.
        /// </summary>
        /// <param name="rank">Rank associated with the message author.</param>
        /// <param name="iconKey">Icon key that should be injected into the chat prefix.</param>
        /// <returns><c>true</c> when the rank has an associated icon.</returns>
        private static bool TryGetRankIconKey(PlayerRank rank, out string iconKey)
        {
            switch (rank)
            {
                case PlayerRank.Support:
                    iconKey = "01";
                    return true;
                case PlayerRank.Moderator:
                    iconKey = "02";
                    return true;
                case PlayerRank.Admin:
                    iconKey = "03";
                    return true;
                case PlayerRank.Developer:
                    iconKey = "04";
                    return true;
                default:
                    iconKey = string.Empty;
                    return false;
            }
        }

        private Color ResolveMessageColor(ChatMessage message)
        {
            if (message.Channel == ChatChannel.Game)
                return GameMessageColor;

            if (message.Channel == ChatChannel.Companion)
                return CompanionMessageColor;

            return message.IsLocalPlayerAuthor ? LocalPlayerMessageColor : PublicMessageColor;
        }

        private sealed class ChatMessageRow
        {
            private const int FontSize = 16;

            public ChatMessageRow(Transform parent)
            {
                Root = new GameObject("MessageRow", typeof(RectTransform));
                RectTransform = Root.GetComponent<RectTransform>();
                RectTransform.SetParent(parent, false);
                RectTransform.anchorMin = new Vector2(0f, 0f);
                RectTransform.anchorMax = new Vector2(1f, 0f);
                RectTransform.offsetMin = Vector2.zero;
                RectTransform.offsetMax = Vector2.zero;

                var layout = Root.AddComponent<LayoutElement>();
                layout.minHeight = 20f;
                layout.flexibleHeight = 0f;

                var horizontalGroup = Root.AddComponent<HorizontalLayoutGroup>();
                horizontalGroup.childControlWidth = true;
                horizontalGroup.childControlHeight = true;
                horizontalGroup.childForceExpandWidth = false;
                horizontalGroup.childForceExpandHeight = false;
                horizontalGroup.childAlignment = TextAnchor.UpperLeft;
                horizontalGroup.spacing = 0f;

                var prefixObject = new GameObject("Prefix", typeof(RectTransform), typeof(EmojiTokenLayout), typeof(LayoutElement));
                var prefixRect = prefixObject.GetComponent<RectTransform>();
                prefixRect.SetParent(Root.transform, false);
                prefixRect.anchorMin = new Vector2(0f, 0f);
                prefixRect.anchorMax = new Vector2(0f, 1f);
                prefixRect.pivot = new Vector2(0f, 0.5f);
                prefixRect.offsetMin = Vector2.zero;
                prefixRect.offsetMax = Vector2.zero;

                var prefixLayout = prefixObject.GetComponent<LayoutElement>();
                prefixLayout.flexibleWidth = 0f;
                prefixLayout.minWidth = 0f;
                prefixLayout.preferredWidth = -1f;

                prefixTokenLayout = prefixObject.GetComponent<EmojiTokenLayout>();

                var contentObject = new GameObject("Content", typeof(RectTransform), typeof(EmojiTokenLayout), typeof(LayoutElement));
                var contentRect = contentObject.GetComponent<RectTransform>();
                contentRect.SetParent(Root.transform, false);
                contentRect.anchorMin = new Vector2(0f, 0f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.offsetMin = Vector2.zero;
                contentRect.offsetMax = Vector2.zero;

                var contentLayout = contentObject.GetComponent<LayoutElement>();
                contentLayout.flexibleWidth = 1f;
                contentLayout.minWidth = 0f;
                contentLayout.preferredWidth = -1f;

                TokenLayout = contentObject.GetComponent<EmojiTokenLayout>();
            }

            public GameObject Root { get; }
            public RectTransform RectTransform { get; }
            private readonly EmojiTokenLayout prefixTokenLayout;
            private EmojiTokenLayout TokenLayout { get; }

            public void SetTokens(IReadOnlyList<EmojiMarkupToken> prefixTokens, IReadOnlyList<EmojiMarkupToken> messageTokens, Color color)
            {
                prefixTokenLayout?.RenderTokens(prefixTokens, color, FontSize, TextAnchor.MiddleLeft);
                TokenLayout?.RenderTokens(messageTokens, color, FontSize, TextAnchor.MiddleLeft);
            }

            public void SetActive(bool active)
            {
                Root.SetActive(active);
            }
        }

        private sealed class ChannelToggleState
        {
            public Button Button;
            public Image Background;
            public Text Label;
            public Text StateLabel;
            public bool Enabled;
        }
    }
}
