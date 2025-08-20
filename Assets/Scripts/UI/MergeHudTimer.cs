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

            text = GetComponentInChildren<Text>();
            if (text == null)
            {
                var textGO = new GameObject("Text", typeof(Text));
                textGO.transform.SetParent(transform, false);
                text = textGO.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            text.alignment = TextAnchor.UpperCenter;
            text.fontSize = 24;
            text.color = Color.red;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -10f);
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
