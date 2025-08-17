using UnityEngine;
using UnityEngine.UI;

namespace Pets
{
    /// <summary>
    /// Simple top-center toast messages for pet notifications.
    /// </summary>
    public class PetToastUI : MonoBehaviour
    {
        private static PetToastUI instance;
        private Text text;
        private CanvasGroup group;
        private float timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            var go = new GameObject("PetToastUI", typeof(Canvas), typeof(CanvasScaler), typeof(PetToastUI), typeof(CanvasGroup));
            DontDestroyOnLoad(go);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            instance = go.GetComponent<PetToastUI>();
            instance.group = go.GetComponent<CanvasGroup>();

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(go.transform, false);
            instance.text = textGO.GetComponent<Text>();
            instance.text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            instance.text.alignment = TextAnchor.UpperCenter;
            var rect = instance.text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -20f);

            go.SetActive(false);
        }

        /// <summary>
        /// Show a toast message for a short duration.
        /// </summary>
        public static void Show(string message, Color? color = null)
        {
            if (instance == null)
                return;
            instance.text.text = message;
            instance.text.color = color ?? Color.white;
            instance.timer = 3f;
            instance.group.alpha = 1f;
            instance.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (timer > 0f)
            {
                timer -= Time.deltaTime;
                group.alpha = Mathf.Clamp01(timer / 3f);
                if (timer <= 0f)
                    gameObject.SetActive(false);
            }
        }
    }
}