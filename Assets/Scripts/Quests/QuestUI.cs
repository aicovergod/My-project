using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Quests
{
    /// <summary>
    /// Simple quest log UI built entirely in code.
    /// </summary>
    public class QuestUI : MonoBehaviour
    {
        private RectTransform listContent;
        private Text titleText;
        private Text descriptionText;
        private Text stepsText;
        private Text rewardsText;
        private QuestDefinition selected;

        private void Awake()
        {
            name = "QuestUI";
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.AddComponent<GraphicRaycaster>();

            BuildLayout();
            gameObject.SetActive(false);
        }

        private void Start()
        {
            if (QuestManager.Instance != null)
                QuestManager.Instance.QuestsUpdated.AddListener(Refresh);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                gameObject.SetActive(!gameObject.activeSelf);
                if (gameObject.activeSelf) Refresh();
            }
        }

        private void BuildLayout()
        {
            var bg = new GameObject("Background", typeof(Image));
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.SetParent(transform, false);
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Hide the full-screen background to avoid a white overlay
            var bgImage = bg.GetComponent<Image>();
            bgImage.color = Color.clear;

            // Left quest list
            var listGO = new GameObject("QuestList", typeof(Image), typeof(ScrollRect));
            var listRect = listGO.GetComponent<RectTransform>();
            listRect.SetParent(bgRect, false);
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(0.35f, 1f);
            listRect.offsetMin = new Vector2(10f, 10f);
            listRect.offsetMax = new Vector2(-10f, -10f);
            listGO.GetComponent<Image>().color = Color.clear;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Mask), typeof(Image));
            viewport.transform.SetParent(listGO.transform, false);
            var vpRect = viewport.GetComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.2f);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            listContent = content.GetComponent<RectTransform>();
            listContent.SetParent(viewport.transform, false);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;

            var scroll = listGO.GetComponent<ScrollRect>();
            scroll.viewport = vpRect;
            scroll.content = listContent;

            // Right details panel
            var details = new GameObject("Details", typeof(Image));
            var detRect = details.GetComponent<RectTransform>();
            detRect.SetParent(bgRect, false);
            detRect.anchorMin = new Vector2(0.35f, 0f);
            detRect.anchorMax = new Vector2(1f, 1f);
            detRect.offsetMin = new Vector2(10f, 10f);
            detRect.offsetMax = new Vector2(-40f, -10f);
            details.GetComponent<Image>().color = Color.clear;

            titleText = CreateText("Title", detRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0, -10));
            titleText.fontStyle = FontStyle.Bold;
            descriptionText = CreateText("Description", detRect, new Vector2(0f, 0.7f), new Vector2(1f, 1f), new Vector2(0, -40));
            stepsText = CreateText("Steps", detRect, new Vector2(0f, 0.3f), new Vector2(1f, 0.7f), Vector2.zero);
            rewardsText = CreateText("Rewards", detRect, new Vector2(0f, 0f), new Vector2(1f, 0.3f), new Vector2(0, 10));

            // Close button
            var closeBtnGO = new GameObject("Close", typeof(Image), typeof(Button));
            var closeRect = closeBtnGO.GetComponent<RectTransform>();
            closeRect.SetParent(bgRect, false);
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(30f, 30f);
            closeRect.anchoredPosition = new Vector2(-15f, -15f);
            var closeText = CreateText("X", closeRect, Vector2.zero, Vector2.one, Vector2.zero);
            closeText.alignment = TextAnchor.MiddleCenter;
            closeBtnGO.GetComponent<Image>().color = Color.clear;
            closeBtnGO.GetComponent<Button>().onClick.AddListener(() => gameObject.SetActive(false));
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
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.UpperLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = Color.white;
            return txt;
        }

        private void Refresh()
        {
            foreach (Transform child in listContent)
                Destroy(child.gameObject);

            var allQuests = QuestManager.Instance.GetActiveQuests().Concat(QuestManager.Instance.GetAvailableQuests());
            foreach (var quest in allQuests)
            {
                var btnGO = new GameObject(quest.Title, typeof(Button));
                var btnRect = btnGO.GetComponent<RectTransform>();
                btnRect.SetParent(listContent, false);
                btnRect.sizeDelta = new Vector2(0, 30f);
                var txt = CreateText("Text", btnRect, Vector2.zero, Vector2.one, Vector2.zero);
                txt.text = quest.Title;
                txt.alignment = TextAnchor.MiddleLeft;
                btnGO.GetComponent<Button>().onClick.AddListener(() => SelectQuest(quest));
            }

            if (selected == null && allQuests.Any())
                SelectQuest(allQuests.First());
            else if (selected != null)
                SelectQuest(selected);
        }

        private void SelectQuest(QuestDefinition quest)
        {
            selected = quest;
            if (quest == null)
            {
                titleText.text = descriptionText.text = stepsText.text = rewardsText.text = string.Empty;
                return;
            }
            titleText.text = quest.Title;
            descriptionText.text = quest.Description;
            stepsText.text = string.Join("\n", quest.Steps.Select(s => $"{(s.IsComplete ? "[\u2714]" : "[ ]")} {s.StepDescription}"));
            rewardsText.text = quest.Rewards;
        }
    }
}
