using UnityEngine;
using UnityEngine.UI;
using Status;
using Util;

namespace UI.HUD
{
    /// <summary>
    /// Displays a single buff infobox mirroring the Old School RuneScape UI style.
    /// </summary>
    public class BuffInfoBox : MonoBehaviour
    {
        [SerializeField] private Image frameImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private Text nameText;
        [SerializeField] private Text timerText;
        [SerializeField] private Color normalTimerColor = Color.white;
        [SerializeField] private Color warningTimerColor = new Color32(255, 196, 0, 255);
        [SerializeField] private CanvasGroup canvasGroup;

        private Sprite loadedIcon;

        public BuffTimerInstance BoundBuff { get; private set; }

        /// <summary>
        /// Creates a fully wired buff infobox at runtime using the expected OSRS styling.
        /// </summary>
        /// <param name="parent">Container that will hold the created infobox.</param>
        public static BuffInfoBox Create(RectTransform parent)
        {
            if (parent == null)
                throw new System.ArgumentNullException(nameof(parent));

            const float slotSize = 64f;
            const float iconSize = 56f;
            const float textPadding = 2f;

            // Root object that mimics the prefab layout.  The anchors/pivot align
            // with the HUD expectation of stacking boxes downward from the
            // minimap's top-right corner.
            var rootGO = new GameObject("BuffInfoBox", typeof(RectTransform), typeof(CanvasGroup));
            int parentLayer = parent.gameObject.layer;
            rootGO.layer = parentLayer;

            var rootRect = rootGO.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.sizeDelta = new Vector2(slotSize, slotSize);

            var component = rootGO.AddComponent<BuffInfoBox>();

            var canvasGroup = rootGO.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0.92f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            component.canvasGroup = canvasGroup;

            var frameGO = new GameObject("Slot", typeof(Image));
            frameGO.layer = parentLayer;
            frameGO.transform.SetParent(rootGO.transform, false);
            var frameImage = frameGO.GetComponent<Image>();
            frameImage.raycastTarget = false;
            frameImage.sprite = Resources.Load<Sprite>("Interfaces/Equipment/Empty_Slot");
            frameImage.color = Color.white;
            var frameRect = frameImage.rectTransform;
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = Vector2.zero;
            component.frameImage = frameImage;

            var iconGO = new GameObject("Icon", typeof(Image));
            iconGO.layer = parentLayer;
            iconGO.transform.SetParent(frameGO.transform, false);
            var iconImage = iconGO.GetComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            var iconRect = iconImage.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);
            iconRect.anchoredPosition = Vector2.zero;
            component.iconImage = iconImage;
            iconImage.color = Color.clear;

            var legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var nameGO = new GameObject("Name", typeof(Text));
            nameGO.layer = parentLayer;
            nameGO.transform.SetParent(frameGO.transform, false);
            var nameText = nameGO.GetComponent<Text>();
            nameText.font = legacyFont;
            nameText.fontSize = 14;
            nameText.alignment = TextAnchor.UpperCenter;
            nameText.color = new Color32(255, 240, 187, 255);
            nameText.raycastTarget = false;
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.verticalOverflow = VerticalWrapMode.Truncate;
            nameText.supportRichText = false;
            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = new Vector2(0.5f, 1f);
            nameRect.anchorMax = new Vector2(0.5f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -textPadding);
            nameRect.sizeDelta = new Vector2(slotSize - (textPadding * 2f), 18f);
            component.nameText = nameText;

            nameText.text = string.Empty;

            var timerGO = new GameObject("Timer", typeof(Text));
            timerGO.layer = parentLayer;
            timerGO.transform.SetParent(frameGO.transform, false);

            var timerText = timerGO.GetComponent<Text>();
            timerText.font = legacyFont;
            timerText.fontSize = 12;
            timerText.alignment = TextAnchor.LowerCenter;
            timerText.color = new Color32(212, 212, 212, 255);
            timerText.raycastTarget = false;
            timerText.horizontalOverflow = HorizontalWrapMode.Overflow;
            timerText.verticalOverflow = VerticalWrapMode.Truncate;
            timerText.supportRichText = false;
            timerText.text = string.Empty;
            var timerRect = timerText.rectTransform;
            timerRect.anchorMin = new Vector2(0.5f, 0f);
            timerRect.anchorMax = new Vector2(0.5f, 0f);
            timerRect.pivot = new Vector2(0.5f, 0f);
            timerRect.anchoredPosition = new Vector2(0f, textPadding);
            timerRect.sizeDelta = new Vector2(slotSize - (textPadding * 2f), 18f);
            component.timerText = timerText;

            component.normalTimerColor = timerText.color;

            component.ResetVisuals();

            return component;
        }

        private void Awake()
        {
            var legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (nameText != null && nameText.font == null)
                nameText.font = legacyFont;
            if (timerText != null && timerText.font == null)
                timerText.font = legacyFont;
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();
        }

        /// <summary>
        /// Initialise the infobox with the provided buff instance.
        /// </summary>
        public void Bind(BuffTimerInstance instance)
        {
            BoundBuff = instance;
            if (nameText != null)
                nameText.text = instance.DisplayName;
            ApplyIcon(instance.Definition.iconId);
            UpdateTimer(instance);
            SetWarning(false);
        }

        /// <summary>
        /// Update the timer text using the buff's remaining ticks.
        /// </summary>
        public void UpdateTimer(BuffTimerInstance instance)
        {
            if (timerText == null)
                return;

            if (instance.IsIndefinite && !instance.IsRecurring)
            {
                timerText.text = "--";
                return;
            }

            float seconds = Mathf.Max(0f, instance.RemainingTicks) * Ticker.TickDuration;
            timerText.text = FormatTime(seconds);
        }

        /// <summary>
        /// Highlight the timer when an expiry warning is issued.
        /// </summary>
        public void SetWarning(bool active)
        {
            if (timerText != null)
                timerText.color = active ? warningTimerColor : normalTimerColor;
            if (canvasGroup != null)
                canvasGroup.alpha = active ? 1f : 0.92f;
        }

        /// <summary>
        /// Resets any temporary styling and returns to the default state.
        /// </summary>
        public void ResetVisuals()
        {
            SetWarning(false);
        }

        /// <summary>
        /// Loads an icon from Resources/UI/Buffs when an explicit sprite is not assigned via the inspector.
        /// </summary>
        private void ApplyIcon(string iconId)
        {
            if (iconImage == null)
                return;

            if (!string.IsNullOrEmpty(iconId))
            {
                string path = $"UI/Buffs/{iconId}";
                loadedIcon = Resources.Load<Sprite>(path);
            }

            iconImage.sprite = loadedIcon;
            iconImage.color = loadedIcon != null ? Color.white : Color.clear;
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int totalSeconds = Mathf.RoundToInt(seconds);
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;
            if (minutes > 0)
                return $"{minutes:00}:{secs:00}";
            return secs >= 10 ? secs.ToString() : $"0{secs}";
        }
    }
}
