using UnityEngine;
using UnityEngine.UI;

namespace Pets
{
    /// <summary>
    /// Simple context menu for the pet level bar.
    /// </summary>
    public partial class PetLevelBarMenu : MonoBehaviour
    {
        private Button xpButton;
        private Button guardButton;
        private Text guardText;
        private PetLevelBarHUD current;

        private static PetLevelBarMenu instance;
        private static Canvas menuCanvas;

        public static void Show(PetLevelBarHUD hud, Vector2 position)
        {
            if (instance == null)
                CreateInstance();
            instance.current = hud;
            instance.guardText.text = PetDropSystem.GuardModeEnabled ? "Guard Mode: On" : "Guard Mode: Off";
            instance.transform.position = position;
            instance.gameObject.SetActive(true);
            instance.OnMenuShown();
        }

        private static void CreateInstance()
        {
            var canvasGO = new GameObject("PetBarMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            menuCanvas = canvasGO.GetComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Object.DontDestroyOnLoad(canvasGO);

            var menuGO = new GameObject("PetLevelBarMenu", typeof(Image), typeof(PetLevelBarMenu), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            menuGO.transform.SetParent(canvasGO.transform, false);
            var img = menuGO.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.8f);

            var layout = menuGO.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = true;
            var fitter = menuGO.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            instance = menuGO.GetComponent<PetLevelBarMenu>();
            instance.xpButton = CreateButton(menuGO.transform, "XP Till Next Level");
            instance.xpButton.onClick.AddListener(() =>
            {
                instance.current?.ShowXpToNextLevel();
                instance.Hide();
            });

            instance.guardButton = CreateButton(menuGO.transform, "Guard Mode");
            instance.guardText = instance.guardButton.GetComponentInChildren<Text>();
            instance.guardButton.onClick.AddListener(() =>
            {
                instance.current?.ToggleGuardMode();
                instance.Hide();
            });

            instance.OnMenuCreated(menuGO.transform);

            menuGO.SetActive(false);
        }

        private static Button CreateButton(Transform parent, string label)
        {
            var go = new GameObject(label, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.preferredWidth = 160f;
            var btn = go.GetComponent<Button>();

            var textGO = new GameObject("Text", typeof(Text));
            textGO.transform.SetParent(go.transform, false);
            var txt = textGO.GetComponent<Text>();
            txt.text = label;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            var rect = txt.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return btn;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            current = null;
        }

        private void Update()
        {
            if (gameObject.activeSelf && Input.GetMouseButtonDown(0))
            {
                var rect = GetComponent<RectTransform>();
                if (!RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, menuCanvas != null ? menuCanvas.worldCamera : null))
                    Hide();
            }
        }

        partial void OnMenuCreated(Transform menuRoot);
        partial void OnMenuShown();
    }
}
