using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Dialogue
{
    /// <summary>
    /// Constructs dialogue UI elements and exposes a simple API for updating them.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        private Text nameText;
        private Text bodyText;
        private RectTransform optionsParent;

        private void Awake()
        {
            name = "DialogueUI";
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            Build();
            gameObject.SetActive(false);
        }

        private void Build()
        {
            var panel = new GameObject("Panel", typeof(Image));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.SetParent(transform, false);
            panelRect.anchorMin = new Vector2(0.1f, 0f);
            panelRect.anchorMax = new Vector2(0.9f, 0.25f);
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;

            // Remove the default white background so the panel is invisible
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = Color.clear;
            panelImage.raycastTarget = false;

            nameText = CreateText("Name", panelRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0, -10));
            nameText.fontStyle = FontStyle.Bold;
            bodyText = CreateText("Body", panelRect, new Vector2(0f, 0.4f), new Vector2(1f, 1f), new Vector2(0, -40));

            var options = new GameObject("Options", typeof(RectTransform), typeof(VerticalLayoutGroup));
            optionsParent = options.GetComponent<RectTransform>();
            optionsParent.SetParent(panelRect, false);
            optionsParent.anchorMin = new Vector2(0f, 0f);
            optionsParent.anchorMax = new Vector2(1f, 0.4f);
            optionsParent.offsetMin = optionsParent.offsetMax = Vector2.zero;
            var layout = options.GetComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
        }

        private Text CreateText(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offset)
        {
            var go = new GameObject(name, typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = offset;
            var txt = go.GetComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.alignment = TextAnchor.UpperLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = Color.white;
            return txt;
        }

        /// <summary>
        /// Populates the dialogue UI with new content and displays it.
        /// </summary>
        public void Show(string npcName, DialogueNode node, System.Action<int> onOption)
        {
            // Ensure UI elements are built before use in case initialization
            // has not yet occurred (e.g., when DialogueManager adds this
            // component at runtime and Show is called immediately).
            if (nameText == null || bodyText == null || optionsParent == null)
                Build();

            gameObject.SetActive(true);
            nameText.text = npcName;
            bodyText.text = node.Text;

            foreach (Transform child in optionsParent)
                Destroy(child.gameObject);

            for (int i = 0; i < node.Options.Count; i++)
            {
                var opt = node.Options[i];
                var btnGO = new GameObject($"Option{i}", typeof(Image), typeof(Button));
                var rect = btnGO.GetComponent<RectTransform>();
                rect.SetParent(optionsParent, false);
                rect.sizeDelta = new Vector2(0, 30f);
                btnGO.GetComponent<Image>().color = Color.clear;
                var txt = CreateText("Text", rect, Vector2.zero, Vector2.one, Vector2.zero);
                txt.text = opt.Text;
                txt.alignment = TextAnchor.MiddleLeft;
                int index = i;
                btnGO.GetComponent<Button>().onClick.AddListener(() => onOption(index));
            }
        }

        public void Hide() => gameObject.SetActive(false);
        
        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
                return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(go);
        }
    }
}
