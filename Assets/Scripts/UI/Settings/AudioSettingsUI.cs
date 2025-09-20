using System;
using Audio;
using UnityEngine;
using UnityEngine.UI;
using World;

namespace UI.Settings
{
    /// <summary>
    /// OSRS-inspired audio settings window that exposes a sound effect volume slider and mute toggle.
    /// The window is created at runtime, listens for volume changes emitted by <see cref="SoundManager"/>,
    /// and allows the player to tweak the preference via UI controls. The panel can be toggled with F3
    /// until a broader settings hub is implemented.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioSettingsUI : MonoBehaviour, IUIWindow
    {
        private const KeyCode ToggleKey = KeyCode.F3;
        private const float WindowWidth = 420f;
        private const float WindowHeight = 220f;
        private const float SliderHorizontalPadding = 24f;
        private const float MinAudibleVolume = 0.001f;

        private static readonly Color PanelColor = new(0.078f, 0.063f, 0.047f, 0.94f);
        private static readonly Color HeaderColor = new(0.118f, 0.094f, 0.055f, 0.98f);
        private static readonly Color BorderColor = new(0.039f, 0.027f, 0.016f, 1f);
        private static readonly Color PrimaryTextColor = new(0.95f, 0.9f, 0.65f, 1f);
        private static readonly Color SecondaryTextColor = new(0.82f, 0.76f, 0.55f, 1f);
        private static readonly Color AccentColor = new(0.9f, 0.75f, 0.25f, 1f);
        private static readonly Color AccentHighlight = new(1f, 0.88f, 0.45f, 1f);
        private static readonly Color AccentPressed = new(0.75f, 0.6f, 0.2f, 1f);
        private static readonly Color DisabledColor = new(0.3f, 0.25f, 0.15f, 0.6f);

        [SerializeField]
        [Tooltip("Runtime canvas hosting the audio options window.")]
        private Canvas canvas;

        [SerializeField]
        [Tooltip("Root panel for the OSRS-style settings window.")]
        private RectTransform windowRoot;

        [SerializeField]
        [Tooltip("Title text rendered inside the parchment header.")]
        private Text titleText;

        [SerializeField]
        [Tooltip("Button that closes the window when clicked.")]
        private Button closeButton;

        [SerializeField]
        [Tooltip("Slider used to adjust the global sound effect volume.")]
        private Slider volumeSlider;

        [SerializeField]
        [Tooltip("Text element that displays the current sound effect percentage.")]
        private Text volumeValueText;

        [SerializeField]
        [Tooltip("Label describing the sound effect slider.")]
        private Text volumeLabel;

        [SerializeField]
        [Tooltip("Toggle that mutes all sound effects when enabled.")]
        private Toggle muteToggle;

        [SerializeField]
        [Tooltip("Label rendered alongside the mute toggle.")]
        private Text muteLabel;

        private Font legacyFont;
        private SoundManager soundManager;
        private bool suppressCallbacks;
        private float lastNonZeroVolume = 1f;

        /// <summary>
        /// Indicates whether the window is currently visible to the player.
        /// </summary>
        public bool IsOpen => windowRoot != null && windowRoot.gameObject.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            PersistentSceneSingleton<AudioSettingsUI>.Bootstrap(CreateSingleton);
        }

        private static AudioSettingsUI CreateSingleton()
        {
            var go = new GameObject(nameof(AudioSettingsUI));
            return go.AddComponent<AudioSettingsUI>();
        }

        /// <summary>
        /// Cache dependencies, build the UI when required and synchronise with the SoundManager.
        /// </summary>
        private void Awake()
        {
            if (!PersistentSceneSingleton<AudioSettingsUI>.HandleAwake(this))
                return;

            legacyFont = LoadLegacyFont();
            if (canvas == null || windowRoot == null || volumeSlider == null || muteToggle == null)
                BuildUi();

            soundManager = SoundManager.Instance;
            soundManager.SfxVolumeChanged += HandleSfxVolumeChanged;

            if (volumeSlider != null)
                volumeSlider.onValueChanged.AddListener(HandleVolumeSliderChanged);
            if (muteToggle != null)
                muteToggle.onValueChanged.AddListener(HandleMuteToggleChanged);
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            windowRoot.gameObject.SetActive(false);
            UIManager.Instance?.RegisterWindow(this);
            ApplyVolumeToUi(soundManager.SfxVolume);
        }

        /// <summary>
        /// Cleanup subscriptions when the singleton is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            if (!PersistentSceneSingleton<AudioSettingsUI>.HandleOnDestroy(this))
                return;

            UIManager.Instance?.UnregisterWindow(this);

            if (volumeSlider != null)
                volumeSlider.onValueChanged.RemoveListener(HandleVolumeSliderChanged);
            if (muteToggle != null)
                muteToggle.onValueChanged.RemoveListener(HandleMuteToggleChanged);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);

            if (soundManager != null)
                soundManager.SfxVolumeChanged -= HandleSfxVolumeChanged;
        }

        /// <summary>
        /// Listen for the temporary hotkey used to show the settings panel.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                if (IsOpen)
                    Close();
                else
                    Open();
            }
        }

        /// <summary>
        /// Display the settings window and ensure the controls reflect the saved volume.
        /// </summary>
        public void Open()
        {
            if (windowRoot == null)
                return;

            windowRoot.gameObject.SetActive(true);
            UIManager.Instance?.OpenWindow(this);
            ApplyVolumeToUi(soundManager.SfxVolume);
        }

        /// <summary>
        /// Hide the settings window.
        /// </summary>
        public void Close()
        {
            if (windowRoot == null)
                return;

            windowRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// React to changes coming from the SoundManager so the UI mirrors external adjustments.
        /// </summary>
        private void HandleSfxVolumeChanged(float volume)
        {
            ApplyVolumeToUi(volume);
        }

        /// <summary>
        /// Adjust the sound effect volume when the slider value changes.
        /// </summary>
        private void HandleVolumeSliderChanged(float value)
        {
            if (suppressCallbacks)
                return;

            if (value > MinAudibleVolume && muteToggle != null && muteToggle.isOn)
            {
                suppressCallbacks = true;
                muteToggle.isOn = false;
                suppressCallbacks = false;
            }

            soundManager.SfxVolume = value;
        }

        /// <summary>
        /// Toggle muting by forcing the slider to zero or restoring the previous non-zero value.
        /// </summary>
        private void HandleMuteToggleChanged(bool muted)
        {
            if (suppressCallbacks)
                return;

            if (muted)
            {
                if (volumeSlider != null && volumeSlider.value > MinAudibleVolume)
                    lastNonZeroVolume = volumeSlider.value;

                suppressCallbacks = true;
                if (volumeSlider != null)
                {
                    volumeSlider.interactable = false;
                    volumeSlider.value = 0f;
                }
                suppressCallbacks = false;

                soundManager.SfxVolume = 0f;
            }
            else
            {
                float restored = lastNonZeroVolume > MinAudibleVolume ? lastNonZeroVolume : 1f;

                suppressCallbacks = true;
                if (volumeSlider != null)
                {
                    volumeSlider.interactable = true;
                    volumeSlider.value = restored;
                }
                suppressCallbacks = false;

                soundManager.SfxVolume = restored;
            }
        }

        /// <summary>
        /// Update slider, labels and toggle to match the supplied volume without triggering feedback loops.
        /// </summary>
        private void ApplyVolumeToUi(float volume)
        {
            suppressCallbacks = true;

            if (volumeSlider != null)
                volumeSlider.value = volume;

            bool isMuted = volume <= MinAudibleVolume;

            if (volumeSlider != null)
                volumeSlider.interactable = !isMuted;

            if (volumeValueText != null)
            {
                int percent = Mathf.RoundToInt(volume * 100f);
                volumeValueText.text = $"{percent}%";
                volumeValueText.color = isMuted ? SecondaryTextColor : PrimaryTextColor;
            }

            if (volumeLabel != null)
                volumeLabel.color = PrimaryTextColor;

            if (muteToggle != null)
                muteToggle.isOn = isMuted;

            if (muteLabel != null)
                muteLabel.color = PrimaryTextColor;

            if (!isMuted)
                lastNonZeroVolume = volume;

            suppressCallbacks = false;
        }

        /// <summary>
        /// Construct the full OSRS-style canvas, header, slider and toggle hierarchy at runtime.
        /// </summary>
        private void BuildUi()
        {
            canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;

            var scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 1f;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var panelGO = new GameObject("Window", typeof(RectTransform), typeof(Image));
            panelGO.transform.SetParent(transform, false);
            windowRoot = panelGO.GetComponent<RectTransform>();
            windowRoot.anchorMin = new Vector2(0.5f, 0.5f);
            windowRoot.anchorMax = new Vector2(0.5f, 0.5f);
            windowRoot.pivot = new Vector2(0.5f, 0.5f);
            windowRoot.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            windowRoot.anchoredPosition = new Vector2(-260f, 80f);
            var panelImage = panelGO.GetComponent<Image>();
            panelImage.color = PanelColor;

            var borderGO = new GameObject("Border", typeof(Image));
            borderGO.transform.SetParent(windowRoot, false);
            var borderRect = borderGO.GetComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-4f, -4f);
            borderRect.offsetMax = new Vector2(4f, 4f);
            borderGO.transform.SetAsFirstSibling();
            var borderImage = borderGO.GetComponent<Image>();
            borderImage.color = BorderColor;
            borderImage.raycastTarget = false;

            var headerGO = new GameObject("Header", typeof(Image));
            headerGO.transform.SetParent(windowRoot, false);
            var headerRect = headerGO.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 40f);
            headerRect.anchoredPosition = Vector2.zero;
            var headerImage = headerGO.GetComponent<Image>();
            headerImage.color = HeaderColor;

            var titleGO = new GameObject("Title", typeof(Text));
            titleGO.transform.SetParent(headerGO.transform, false);
            titleText = titleGO.GetComponent<Text>();
            titleText.font = legacyFont;
            titleText.text = "Options - Audio";
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = PrimaryTextColor;
            titleText.fontSize = 24;
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(12f, 0f);
            titleRect.offsetMax = new Vector2(-48f, 0f);

            var closeGO = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGO.transform.SetParent(headerGO.transform, false);
            closeButton = closeGO.GetComponent<Button>();
            var closeRect = closeGO.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(32f, 26f);
            closeRect.anchoredPosition = new Vector2(-6f, 0f);
            var closeImage = closeGO.GetComponent<Image>();
            closeImage.color = new Color(0.28f, 0.18f, 0.09f, 1f);

            var closeLabelGO = new GameObject("Label", typeof(Text));
            closeLabelGO.transform.SetParent(closeGO.transform, false);
            var closeLabel = closeLabelGO.GetComponent<Text>();
            closeLabel.font = legacyFont;
            closeLabel.text = "X";
            closeLabel.alignment = TextAnchor.MiddleCenter;
            closeLabel.color = PrimaryTextColor;
            closeLabel.fontSize = 18;
            var closeLabelRect = closeLabel.rectTransform;
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;

            var closeColors = closeButton.colors;
            closeColors.normalColor = AccentColor;
            closeColors.highlightedColor = AccentHighlight;
            closeColors.pressedColor = AccentPressed;
            closeColors.selectedColor = AccentColor;
            closeColors.disabledColor = DisabledColor;
            closeButton.colors = closeColors;

            volumeLabel = CreateText("VolumeLabel", windowRoot, "Sound effects volume", new Vector2(24f, -66f), new Vector2(280f, 24f));
            volumeLabel.alignment = TextAnchor.MiddleLeft;
            volumeLabel.fontSize = 20;

            volumeValueText = CreateText("VolumeValue", windowRoot, "100%", new Vector2(WindowWidth - 24f, -66f), new Vector2(80f, 24f));
            volumeValueText.alignment = TextAnchor.MiddleRight;
            volumeValueText.fontSize = 20;
            var valueRect = volumeValueText.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 1f);
            valueRect.anchoredPosition = new Vector2(-24f, -66f);
            valueRect.sizeDelta = new Vector2(80f, 24f);

            BuildSlider(windowRoot);
            BuildMuteToggle(windowRoot);
        }

        /// <summary>
        /// Helper that spawns a configured <see cref="Text"/> element using the Legacy font.
        /// </summary>
        private Text CreateText(string name, RectTransform parent, string defaultText, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = legacyFont;
            text.text = defaultText;
            text.color = PrimaryTextColor;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return text;
        }

        /// <summary>
        /// Generate the OSRS-inspired slider visuals and hook up the component references.
        /// </summary>
        private void BuildSlider(RectTransform parent)
        {
            var sliderGO = new GameObject("SfxSlider", typeof(RectTransform), typeof(Slider));
            sliderGO.transform.SetParent(parent, false);
            var sliderRect = sliderGO.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 1f);
            sliderRect.anchorMax = new Vector2(0f, 1f);
            sliderRect.pivot = new Vector2(0f, 1f);
            sliderRect.anchoredPosition = new Vector2(SliderHorizontalPadding, -106f);
            sliderRect.sizeDelta = new Vector2(WindowWidth - SliderHorizontalPadding * 2f, 28f);

            var backgroundGO = new GameObject("Background", typeof(Image));
            backgroundGO.transform.SetParent(sliderGO.transform, false);
            var backgroundRect = backgroundGO.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var backgroundImage = backgroundGO.GetComponent<Image>();
            backgroundImage.color = new Color(0.18f, 0.14f, 0.09f, 1f);

            var fillAreaGO = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGO.transform.SetParent(sliderGO.transform, false);
            var fillAreaRect = fillAreaGO.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRect.offsetMin = new Vector2(8f, 0f);
            fillAreaRect.offsetMax = new Vector2(-8f, 0f);

            var fillGO = new GameObject("Fill", typeof(Image));
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fillGO.GetComponent<Image>();
            fillImage.color = AccentColor;

            var handleAreaGO = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGO.transform.SetParent(sliderGO.transform, false);
            var handleAreaRect = handleAreaGO.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);

            var handleGO = new GameObject("Handle", typeof(Image));
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            var handleRect = handleGO.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(22f, 26f);
            var handleImage = handleGO.GetComponent<Image>();
            handleImage.color = AccentHighlight;

            var slider = sliderGO.GetComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.value = SoundManager.Instance.SfxVolume;

            var colors = slider.colors;
            colors.normalColor = AccentHighlight;
            colors.highlightedColor = Color.white;
            colors.pressedColor = AccentPressed;
            colors.selectedColor = AccentHighlight;
            colors.disabledColor = DisabledColor;
            slider.colors = colors;

            volumeSlider = slider;
        }

        /// <summary>
        /// Build the mute toggle with RuneScape-inspired palette and layout.
        /// </summary>
        private void BuildMuteToggle(RectTransform parent)
        {
            var toggleGO = new GameObject("MuteToggle", typeof(RectTransform), typeof(Toggle));
            toggleGO.transform.SetParent(parent, false);
            var toggleRect = toggleGO.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(0f, 1f);
            toggleRect.anchorMax = new Vector2(0f, 1f);
            toggleRect.pivot = new Vector2(0f, 1f);
            toggleRect.anchoredPosition = new Vector2(SliderHorizontalPadding, -156f);
            toggleRect.sizeDelta = new Vector2(200f, 26f);

            var backgroundGO = new GameObject("Background", typeof(Image));
            backgroundGO.transform.SetParent(toggleGO.transform, false);
            var backgroundRect = backgroundGO.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.pivot = new Vector2(0f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(22f, 22f);
            var backgroundImage = backgroundGO.GetComponent<Image>();
            backgroundImage.color = new Color(0.18f, 0.14f, 0.09f, 1f);

            var checkmarkGO = new GameObject("Checkmark", typeof(Image));
            checkmarkGO.transform.SetParent(backgroundGO.transform, false);
            var checkmarkRect = checkmarkGO.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkmarkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;
            var checkmarkImage = checkmarkGO.GetComponent<Image>();
            checkmarkImage.color = AccentHighlight;

            var labelGO = new GameObject("Label", typeof(Text));
            labelGO.transform.SetParent(toggleGO.transform, false);
            muteLabel = labelGO.GetComponent<Text>();
            muteLabel.font = legacyFont;
            muteLabel.text = "Mute";
            muteLabel.alignment = TextAnchor.MiddleLeft;
            muteLabel.color = PrimaryTextColor;
            muteLabel.fontSize = 20;
            var labelRect = muteLabel.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(30f, 0f);
            labelRect.offsetMax = Vector2.zero;

            var toggle = toggleGO.GetComponent<Toggle>();
            toggle.isOn = false;
            toggle.graphic = checkmarkImage;
            toggle.targetGraphic = backgroundImage;
            var colors = toggle.colors;
            colors.normalColor = AccentColor;
            colors.highlightedColor = AccentHighlight;
            colors.pressedColor = AccentPressed;
            colors.selectedColor = AccentColor;
            colors.disabledColor = DisabledColor;
            toggle.colors = colors;

            muteToggle = toggle;
        }

        /// <summary>
        /// Retrieve the default LegacyRuntime font while falling back gracefully if Unity cannot find it.
        /// </summary>
        private Font LoadLegacyFont()
        {
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }
    }
}
