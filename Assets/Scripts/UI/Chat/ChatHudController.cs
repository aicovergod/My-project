using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UI.Utilities;

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
        private static readonly Color32 PublicMessageColor = new Color32(230, 230, 230, 255);
        private static readonly Color32 LocalPlayerMessageColor = new Color32(255, 255, 255, 255);
        private static readonly Color32 PlaceholderColor = new Color32(210, 210, 210, 140);

        private static readonly ChatChannel[] ChannelValues = (ChatChannel[])Enum.GetValues(typeof(ChatChannel));

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
        private Text inputNameLabel;
        private Text reminderLabel;
        private Text placeholderLabel;
        private bool autoScrollToBottom = true;
        private bool inputFocused;
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

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
                eventSystem.SetSelectedGameObject(inputField.gameObject);

            inputField.ActivateInputField();
            inputFocused = true;
        }

        /// <summary>
        /// Clears the input field content without altering focus state.
        /// </summary>
        public void ClearInput()
        {
            if (inputField == null)
                return;

            inputField.text = string.Empty;
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
            inputField.text = string.Empty;
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

            inputFocused = false;
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
            rect.sizeDelta = new Vector2(20f, 8f);

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
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            inputNameLabel = CreateTextLabel(row.transform, "Player name: ", 16, PublicMessageColor);

            var inputContainer = new GameObject("InputContainer", typeof(RectTransform), typeof(Image));
            var inputContainerRect = inputContainer.GetComponent<RectTransform>();
            inputContainerRect.SetParent(row.transform, false);
            inputContainerRect.sizeDelta = new Vector2(260f, 32f);

            var inputBackground = inputContainer.GetComponent<Image>();
            inputBackground.color = InputBackgroundColor;

            inputField = inputContainer.AddComponent<InputField>();
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.characterLimit = 200;
            inputField.transition = Selectable.Transition.ColorTint;
            RegisterInputFocusCallbacks(inputField);

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

            reminderLabel = CreateTextLabel(row.transform, "Press Enter to chat", 14, ChannelToggleEnabledTextColor);

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
            inputFocused = true;
        }

        /// <summary>
        /// Marks the chat input as unfocused when the EventSystem deselects the input field.
        /// </summary>
        /// <param name="_">Unused event payload.</param>
        private void HandleInputDeselected(BaseEventData _)
        {
            inputFocused = false;
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

            if (string.IsNullOrEmpty(username))
                inputNameLabel.text = "Player name: Adventurer";
            else
                inputNameLabel.text = $"Player name: {username}";
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
                row.SetText(FormatMessage(message), ResolveMessageColor(message));
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

        private string FormatMessage(ChatMessage message)
        {
            DateTime localTime = message.TimestampUtc.ToLocalTime();
            string timestamp = localTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            string prefix = message.Channel == ChatChannel.Game ? "Game" : (!string.IsNullOrEmpty(message.Sender) ? message.Sender : "Player");
            return $"[{timestamp}] {prefix}: {message.Text}";
        }

        private Color ResolveMessageColor(ChatMessage message)
        {
            if (message.Channel == ChatChannel.Game)
                return GameMessageColor;
            return message.IsLocalPlayerAuthor ? LocalPlayerMessageColor : PublicMessageColor;
        }

        private sealed class ChatMessageRow
        {
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

                var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
                var textRect = textObject.GetComponent<RectTransform>();
                textRect.SetParent(Root.transform, false);
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                Text = textObject.GetComponent<Text>();
                Text.fontSize = 16;
                Text.alignment = TextAnchor.MiddleLeft;
                Text.horizontalOverflow = HorizontalWrapMode.Wrap;
                Text.verticalOverflow = VerticalWrapMode.Overflow;
                LegacyFontProvider.ApplyTo(Text);
            }

            public GameObject Root { get; }
            public RectTransform RectTransform { get; }
            public Text Text { get; }

            public void SetText(string text, Color color)
            {
                Text.text = text;
                Text.color = color;
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
