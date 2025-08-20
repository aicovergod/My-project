using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Displays the remaining merge time at the top centre of the screen.
    /// </summary>
    public class MergeHudTimer : MonoBehaviour
    {
        private Text text;
        private Image background;

        private void Awake()
        {
            var canvas = GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("MergeHudCanvas", typeof(Canvas));
                canvas = canvasGO.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                DontDestroyOnLoad(canvasGO);
                transform.SetParent(canvasGO.transform, false);
            }
            var rectTransform = GetComponent<RectTransform>();
            if (rectTransform == null)
                rectTransform = gameObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = Vector2.zero;

            var bgGO = new GameObject("Background", typeof(Image));
            bgGO.transform.SetParent(transform, false);
            background = bgGO.GetComponent<Image>();
            background.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            var bgRect = background.rectTransform;
            bgRect.anchorMin = new Vector2(0.5f, 1f);
            bgRect.anchorMax = new Vector2(0.5f, 1f);
            bgRect.pivot = new Vector2(0.5f, 1f);
            bgRect.anchoredPosition = new Vector2(0f, -10f);
            bgRect.sizeDelta = new Vector2(160f, 40f);

            text = bgGO.GetComponentInChildren<Text>();
            if (text == null)
            {
                var textGO = new GameObject("Text", typeof(Text));
                textGO.transform.SetParent(bgGO.transform, false);
                text = textGO.GetComponent<Text>();
            }
            var customFont = Resources.Load<Font>("ThaleahFAT_TTF") ?? Resources.Load<Font>("ThaleahFat_TTF");
            text.font = customFont != null ? customFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 24;
            text.color = new Color32(0xFF, 0x8C, 0x00, 0xFF);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            Hide();
        }

        public void Show(TimeSpan remaining)
        {
            gameObject.SetActive(true);
            UpdateTime(remaining);
        }

        public void UpdateTime(TimeSpan remaining)
        {
            if (text != null)
                text.text = $"Merged: {remaining.Minutes:00}:{remaining.Seconds:00}";
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
