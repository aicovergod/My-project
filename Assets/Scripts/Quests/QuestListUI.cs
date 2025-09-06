using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Player;
using UI;

namespace Quests
{
    /// <summary>
    /// Displays available quests in a scrollable list and shows details when selected.
    /// </summary>
    public class QuestListUI : MonoBehaviour, IUIWindow
    {
        private RectTransform listContent;
        private Text titleText;
        private Text descriptionText;
        private Text rewardsText;
        private GameObject detailsPanel;
        private QuestDefinition[] quests;
        private Canvas canvas;
        private PlayerMover playerMover;

        public bool IsOpen => canvas != null && canvas.enabled;

        private void Awake()
        {
            name = "QuestListUI";
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.AddComponent<GraphicRaycaster>();
            BuildLayout();
            DontDestroyOnLoad(gameObject);
            canvas.enabled = false;
            UIManager.Instance.RegisterWindow(this);
        }

        private void Start()
        {
            Preload();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            UIManager.Instance.OpenWindow(this);
            canvas.enabled = true;
            if (playerMover == null)
                playerMover = FindObjectOfType<PlayerMover>();
            if (playerMover != null)
                playerMover.enabled = false;
        }

        public void Close()
        {
            canvas.enabled = false;
            detailsPanel.SetActive(false);
            titleText.text = descriptionText.text = rewardsText.text = string.Empty;
            if (playerMover != null)
                playerMover.enabled = true;
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
            bg.GetComponent<Image>().color = Color.clear;

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
            viewport.GetComponent<Image>().color = new Color32(0x26, 0x26, 0x26, 0xF2);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            listContent = content.GetComponent<RectTransform>();
            listContent.SetParent(viewport.transform, false);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.offsetMin = listContent.offsetMax = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(0, 0, 5, 0);

            var scroll = listGO.GetComponent<ScrollRect>();
            scroll.viewport = vpRect;
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            detailsPanel = new GameObject("Details", typeof(Image));
            var detRect = detailsPanel.GetComponent<RectTransform>();
            detRect.SetParent(bgRect, false);
            detRect.anchorMin = new Vector2(0.35f, 0f);
            detRect.anchorMax = new Vector2(1f, 1f);
            detRect.offsetMin = new Vector2(10f, 10f);
            detRect.offsetMax = new Vector2(-40f, -10f);
            detailsPanel.GetComponent<Image>().color = new Color32(0x26, 0x26, 0x26, 0xF2);

            titleText = CreateText("Title", detRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0, -10));
            titleText.fontStyle = FontStyle.Bold;
            descriptionText = CreateText("Description", detRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.9f), Vector2.zero);
            rewardsText = CreateText("Rewards", detRect, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(0, 10));

            detailsPanel.SetActive(false);

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
            closeBtnGO.GetComponent<Button>().onClick.AddListener(Close);
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
            txt.raycastTarget = false;
            return txt;
        }

        private void Preload()
        {
            quests = QuestManager.Instance.GetAvailableQuests().ToArray();
            foreach (Transform child in listContent)
                Destroy(child.gameObject);

            var btnGO = new GameObject("ToolsOfSuccess", typeof(Image), typeof(Button));
            var rect = btnGO.GetComponent<RectTransform>();
            rect.SetParent(listContent, false);
            rect.sizeDelta = new Vector2(0f, 30f);
            var txt = CreateText("Label", rect, Vector2.zero, Vector2.one, Vector2.zero);
            txt.text = "Tools Of Success";
            txt.alignment = TextAnchor.MiddleLeft;
            btnGO.GetComponent<Image>().color = Color.clear;
            btnGO.GetComponent<Button>().onClick.AddListener(() => OpenQuest("ToolsOfSuccess"));
        }

        /// <summary>
        /// Opens the quest with the given identifier and populates the details panel.
        /// </summary>
        public void OpenQuest(string questId)
        {
            var quest = QuestManager.Instance.GetQuest(questId);
            if (quest == null)
                quest = QuestManager.Instance.GetAvailableQuests().FirstOrDefault(q => q.QuestID == questId);
            if (quest == null)
                return;
            titleText.text = quest.Title;
            descriptionText.text = quest.Description;
            rewardsText.text = quest.Rewards;
            detailsPanel.SetActive(true);
        }
    }
}
