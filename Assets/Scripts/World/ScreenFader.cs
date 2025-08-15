using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace World
{
    /// <summary>
    /// Handles fading the screen in and out using a full screen canvas.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        public static ScreenFader Instance;

        [Tooltip("Default duration of the fade animations.")]
        public float fadeDuration = 0.5f;

        private CanvasGroup _group;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            var canvas = GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                canvas = new GameObject("Canvas", typeof(Canvas)).GetComponent<Canvas>();
                canvas.transform.SetParent(transform, false);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            _group = canvas.GetComponent<CanvasGroup>();
            if (_group == null)
                _group = canvas.gameObject.AddComponent<CanvasGroup>();

            if (canvas.GetComponentInChildren<Image>() == null)
            {
                var img = new GameObject("Image", typeof(Image)).GetComponent<Image>();
                img.transform.SetParent(canvas.transform, false);
                img.color = Color.black;
                var rect = img.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            _group.alpha = 0f;
        }

        public IEnumerator FadeOut(float duration = -1f)
        {
            if (duration <= 0f) duration = fadeDuration;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Clamp01(t / duration);
                yield return null;
            }
        }

        public IEnumerator FadeIn(float duration = -1f)
        {
            if (duration <= 0f) duration = fadeDuration;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(t / duration);
                yield return null;
            }
        }
    }
}
