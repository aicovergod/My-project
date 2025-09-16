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
