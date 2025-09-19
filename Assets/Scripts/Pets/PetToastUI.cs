using UnityEngine;
using UnityEngine.UI;
using World;

namespace Pets
{
    /// <summary>
    /// Simple top-center toast messages for pet notifications.
    /// </summary>
    public class PetToastUI : MonoBehaviour
    {
        public static PetToastUI Instance => PersistentSceneSingleton<PetToastUI>.Instance;

        private Text text;
        private CanvasGroup group;
        private float timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            PersistentSceneSingleton<PetToastUI>.Bootstrap(CreateSingleton);
        }

        private static PetToastUI CreateSingleton()
        {
            var go = new GameObject(nameof(PetToastUI), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            return go.AddComponent<PetToastUI>();
        }

        private void Awake()
        {
            if (!PersistentSceneSingleton<PetToastUI>.HandleAwake(this))
                return;

            FinaliseSetup();
        }

        private void FinaliseSetup()
        {
            ConfigureCanvas();
            EnsureTextElement();
            group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            timer = 0f;

            gameObject.SetActive(false);
        }

        private void ConfigureCanvas()
        {
            var canvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        }

        private void EnsureTextElement()
        {
            if (text != null)
                return;

            var existing = GetComponentInChildren<Text>();
            if (existing == null)
            {
                var textGO = new GameObject("Text", typeof(Text));
                textGO.transform.SetParent(transform, false);
                existing = textGO.GetComponent<Text>();
                existing.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                existing.alignment = TextAnchor.UpperCenter;

                var rect = existing.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -20f);
            }

            text = existing;
        }

        /// <summary>
        /// Show a toast message for a short duration.
        /// </summary>
        public static void Show(string message, Color? color = null)
        {
            var toast = Instance;
            if (toast == null)
                return;

            toast.text.text = message;
            toast.text.color = color ?? Color.white;
            toast.timer = 3f;
            toast.group.alpha = 1f;
            toast.gameObject.SetActive(true);
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

        private void OnDestroy()
        {
            PersistentSceneSingleton<PetToastUI>.HandleOnDestroy(this);
        }
    }
}
